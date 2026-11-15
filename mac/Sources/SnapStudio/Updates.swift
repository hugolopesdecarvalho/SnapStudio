import AppKit

/// Release publicada no GitHub.
struct ReleaseInfo {
    let version: String      // "1.2.0"
    let tag: String          // "v1.2.0"
    let pageURL: URL
    let notes: String
    let downloadURL: URL?    // SnapStudio-macOS.zip
}

/// Procura versões novas em github.com/hugolopesdecarvalho/SnapStudio/releases (API pública, sem conta).
/// Só faz um pedido GET; não envia dados do utilizador.
final class UpdateChecker {
    static let shared = UpdateChecker()
    static let repo = "hugolopesdecarvalho/SnapStudio"
    static var repoURL: URL { URL(string: "https://github.com/\(repo)")! }
    static var releasesURL: URL { URL(string: "https://github.com/\(repo)/releases")! }

    /// Versão instalada (Info.plist). `--pretend-version X` simula outra versão, para testes.
    static var currentVersion: String {
        let a = CommandLine.arguments
        if let i = a.firstIndex(of: "--pretend-version"), i + 1 < a.count { return a[i + 1] }
        return Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0.0.0"
    }

    private(set) var available: ReleaseInfo?
    private(set) var checking = false
    private var timer: Timer?

    // ---------- comparação de versões ----------

    static func components(_ v: String) -> [Int] {
        let core = v.trimmingCharacters(in: .whitespaces).drop(while: { $0 == "v" || $0 == "V" })
            .split(separator: "-").first.map(String.init) ?? ""
        return core.split(separator: ".").map { Int($0.filter(\.isNumber)) ?? 0 }
    }

    /// true se `a` for mais recente do que `b` (1.10.0 > 1.9.3; "v" e sufixos "-beta" são ignorados).
    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = components(a), y = components(b)
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }

    // ---------- GitHub ----------

    static func fetchLatest() async throws -> ReleaseInfo {
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("SnapStudio-macOS/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 15
        req.cachePolicy = .reloadIgnoringLocalCacheData
        let (data, resp) = try await URLSession.shared.data(for: req)
        guard let http = resp as? HTTPURLResponse else { throw URLError(.badServerResponse) }
        if http.statusCode == 404 {
            throw NSError(domain: "SnapStudio", code: 404, userInfo: [NSLocalizedDescriptionKey: S.t("Ainda não há versões publicadas.", "No releases have been published yet.")])
        }
        guard http.statusCode == 200,
              let j = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = j["tag_name"] as? String,
              let page = (j["html_url"] as? String).flatMap(URL.init(string:)) else {
            throw NSError(domain: "SnapStudio", code: http.statusCode, userInfo: [NSLocalizedDescriptionKey:
                S.t("Resposta inesperada do GitHub (\(http.statusCode)).", "Unexpected response from GitHub (\(http.statusCode)).")])
        }
        let assets = (j["assets"] as? [[String: Any]]) ?? []
        let mac = assets.first { (($0["name"] as? String) ?? "").lowercased().contains("macos") }
        let dl = (mac?["browser_download_url"] as? String).flatMap(URL.init(string:))
        let version = String(tag.drop(while: { $0 == "v" || $0 == "V" }))
        return ReleaseInfo(version: version, tag: tag, pageURL: page, notes: (j["body"] as? String) ?? "", downloadURL: dl)
    }

    // ---------- verificação ----------

    /// Arranque: verifica pouco depois de abrir e depois uma vez por dia (se a opção estiver ligada).
    func start() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 8) { [weak self] in self?.checkIfDue() }
        timer = Timer.scheduledTimer(withTimeInterval: 3600, repeats: true) { [weak self] _ in self?.checkIfDue() }
    }

    private func checkIfDue() {
        let st = AppController.shared.settings
        guard st.autoUpdateCheck else { return }
        if Date().timeIntervalSince1970 - st.lastUpdateCheck < 20 * 3600 { return }
        check(interactive: false)
    }

    /// `interactive`: pedido do utilizador — mostra sempre o resultado (mesmo "está atualizado" ou erro).
    func check(interactive: Bool) {
        if checking { return }
        checking = true
        NotificationCenter.default.post(name: .snapUpdateChanged, object: nil)
        Task { @MainActor in
            defer {
                checking = false
                NotificationCenter.default.post(name: .snapUpdateChanged, object: nil)
            }
            let st = AppController.shared.settings
            do {
                let r = try await UpdateChecker.fetchLatest()
                st.lastUpdateCheck = Date().timeIntervalSince1970
                st.save()
                if UpdateChecker.isNewer(r.version, than: UpdateChecker.currentVersion) {
                    let isNew = available?.version != r.version
                    available = r
                    if interactive {
                        showUpdateDialog(r)
                    } else if isNew && st.skippedVersion != r.version {
                        AppController.shared.notifyUpdate(r)
                    }
                } else {
                    available = nil
                    if interactive { showUpToDate() }
                }
            } catch {
                if interactive {
                    AppController.shared.showError(S.t("Não foi possível procurar atualizações:\n\n", "Could not check for updates:\n\n") + error.localizedDescription)
                }
            }
        }
    }

    // ---------- diálogos ----------

    func showUpToDate() {
        let a = NSAlert()
        a.messageText = S.t("A SnapStudio está atualizada", "SnapStudio is up to date")
        a.informativeText = S.t("Tem a versão mais recente (\(UpdateChecker.currentVersion)).", "You have the latest version (\(UpdateChecker.currentVersion)).")
        a.icon = AppIcon.image(size: 64)
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }

    func showUpdateDialog(_ r: ReleaseInfo) {
        let a = NSAlert()
        a.icon = AppIcon.image(size: 64)
        a.messageText = S.t("Está disponível a SnapStudio \(r.version)", "SnapStudio \(r.version) is available")
        a.informativeText = S.t("Tem a versão \(UpdateChecker.currentVersion). Descarregue o ficheiro, abra-o e arraste a SnapStudio.app para Aplicações, substituindo a atual.",
                                "You have version \(UpdateChecker.currentVersion). Download the file, open it and drag SnapStudio.app to Applications, replacing the current one.")
        // novidades (texto da release), num campo com deslocamento
        let notes = Self.plainNotes(r.notes)
        if !notes.isEmpty {
            let sv = NSScrollView(frame: CGRect(x: 0, y: 0, width: 420, height: 200))
            sv.hasVerticalScroller = true
            sv.borderType = .bezelBorder
            let tv = NSTextView(frame: sv.bounds)
            tv.isEditable = false
            tv.string = notes
            tv.font = NSFont.systemFont(ofSize: 12)
            tv.textContainerInset = CGSize(width: 6, height: 6)
            tv.autoresizingMask = [.width]
            sv.documentView = tv
            a.accessoryView = sv
        }
        a.addButton(withTitle: S.t("Descarregar", "Download"))
        a.addButton(withTitle: S.t("Mais tarde", "Later"))
        a.addButton(withTitle: S.t("Ignorar esta versão", "Skip This Version"))
        NSApp.activate(ignoringOtherApps: true)
        switch a.runModal() {
        case .alertFirstButtonReturn:
            NSWorkspace.shared.open(r.downloadURL ?? r.pageURL)
        case .alertThirdButtonReturn:
            let st = AppController.shared.settings
            st.skippedVersion = r.version
            st.save()
            NotificationCenter.default.post(name: .snapUpdateChanged, object: nil)
        default: break
        }
    }

    /// Versão mais recente ainda não ignorada (para o painel e o menu).
    var pending: ReleaseInfo? {
        guard let r = available, r.version != AppController.shared.settings.skippedVersion else { return nil }
        return r
    }

    /// Markdown simplificado para texto (títulos, negrito, código, links).
    static func plainNotes(_ md: String) -> String {
        var s = md.replacingOccurrences(of: "\r\n", with: "\n")
        for (p, r) in [("\\*\\*([^*]+)\\*\\*", "$1"), ("`([^`]+)`", "$1"), ("\\[([^\\]]+)\\]\\([^)]+\\)", "$1"),
                       ("(?m)^#{1,6}\\s*", ""), ("(?m)^>\\s?", ""), ("(?m)^- ", "• ")] {
            s = s.replacingOccurrences(of: p, with: r, options: .regularExpression)
        }
        return s.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
