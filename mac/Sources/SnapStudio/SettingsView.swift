import AppKit
import Carbon.HIToolbox
import SwiftUI

/// Liga as vistas às definições; cada alteração é guardada e aplicada de imediato.
final class SettingsStore: ObservableObject {
    var s: Settings { AppController.shared.settings }
    @Published var loginOn = Settings.startAtLogin
    @Published var permission = Capture.hasPermission
    @Published var updateTick = 0
    @Published var tab = SettingsStore.pendingTab ?? "general"
    static var pendingTab: String?
    private var tokens: [NSObjectProtocol] = []

    init() {
        SettingsStore.pendingTab = nil
        let nc = NotificationCenter.default
        tokens.append(nc.addObserver(forName: .snapUpdateChanged, object: nil, queue: .main) { [weak self] _ in
            self?.updateTick += 1
        })
        tokens.append(nc.addObserver(forName: .snapShowSettingsTab, object: nil, queue: .main) { [weak self] n in
            if let t = n.object as? String { self?.tab = t }
        })
    }
    deinit { tokens.forEach(NotificationCenter.default.removeObserver) }

    func bind<T>(_ kp: ReferenceWritableKeyPath<Settings, T>, then: (() -> Void)? = nil) -> Binding<T> {
        Binding(get: { self.s[keyPath: kp] }, set: {
            self.objectWillChange.send()
            self.s[keyPath: kp] = $0
            self.s.save()
            then?()
        })
    }
}

struct SettingsView: View {
    @StateObject private var st = SettingsStore()
    @StateObject private var theme = ThemeObserver()

    static let size = CGSize(width: 760, height: 500)

    /// Secções da barra lateral (as mesmas da versão Windows).
    private var sections: [(id: String, title: String, symbol: String)] {
        [("general", S.t("Geral", "General"), "gearshape"),
         ("appearance", S.t("Aparência", "Appearance"), "paintpalette"),
         ("after", S.t("Depois de capturar", "After capture"), "square.and.pencil"),
         ("shortcuts", S.t("Atalhos", "Shortcuts"), "keyboard"),
         ("files", S.t("Ficheiros", "Files"), "folder"),
         ("video", S.t("Vídeo", "Video"), "video"),
         ("about", S.t("Sobre", "About"), "info.circle")]
    }

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            Divider()
            VStack(alignment: .leading, spacing: 0) {
                Text(sections.first(where: { $0.id == st.tab })?.title ?? "")
                    .font(.system(size: 22, weight: .bold))
                    .padding(.horizontal, 24)
                    .padding(.top, 22)
                    .padding(.bottom, 4)
                page(st.tab)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(width: Self.size.width, height: Self.size.height)
        .id(theme.tick)
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 10) {
                Image(nsImage: AppIcon.image(size: 28))
                Text(S.t("Definições", "Settings")).font(.system(size: 16, weight: .bold))
            }
            .padding(.horizontal, 18)
            .padding(.top, 20)
            .padding(.bottom, 12)
            List(selection: Binding<String?>(get: { st.tab }, set: { if let v = $0 { st.tab = v } })) {
                ForEach(sections, id: \.id) { s in
                    Label(s.title, systemImage: s.symbol)
                        .padding(.vertical, 3)
                        .tag(s.id as String?)
                }
            }
            .listStyle(.sidebar)
            .scrollContentBackground(.hidden)
            Spacer(minLength: 0)
            Text(S.t("Versão ", "Version ") + UpdateChecker.currentVersion)
                .font(.caption).foregroundStyle(.tertiary)
                .padding(.horizontal, 18).padding(.bottom, 14)
        }
        .frame(width: 210)
        .background(.background.secondary)
    }

    @ViewBuilder
    private func page(_ id: String) -> some View {
        switch id {
        case "appearance": appearance
        case "after": after
        case "shortcuts": shortcuts
        case "files": files
        case "video": video
        case "about": about
        default: general
        }
    }

    // ---------- Geral ----------

    private var permission: Bool { st.permission }

    private var general: some View {
        Form {
            Toggle(S.t("Iniciar ao arrancar o Mac", "Open at login"), isOn: Binding(get: { st.loginOn }, set: {
                Settings.startAtLogin = $0
                st.loginOn = Settings.startAtLogin
            }))
            Section {
                HStack {
                    Image(systemName: permission ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        .foregroundStyle(permission ? .green : .orange)
                    Text(permission ? S.t("Gravação do ecrã autorizada", "Screen recording allowed")
                                    : S.t("Gravação do ecrã não autorizada", "Screen recording not allowed"))
                    Spacer()
                    if !permission {
                        Button(S.t("Abrir Definições do Sistema", "Open System Settings")) {
                            _ = CGRequestScreenCaptureAccess()
                            if let u = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") { NSWorkspace.shared.open(u) }
                        }
                    }
                }
            } footer: {
                Text(S.t("O macOS pede esta autorização para a SnapStudio poder capturar e gravar o ecrã.",
                         "macOS asks for this permission so SnapStudio can capture and record the screen."))
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onAppear {
            st.loginOn = Settings.startAtLogin
            Task { @MainActor in st.permission = await Capture.verifyPermission() }
        }
    }

    // ---------- Aparência ----------

    private var appearance: some View {
        Form {
            Picker(S.t("Tema", "Theme"), selection: st.bind(\.themeMode, then: { Theme.apply(AppController.shared.settings) })) {
                Text(S.t("Igual ao macOS", "Same as macOS")).tag("system")
                Text(S.t("Claro", "Light")).tag("light")
                Text(S.t("Escuro", "Dark")).tag("dark")
            }
            .pickerStyle(.radioGroup)
            Picker(S.t("Cor de destaque", "Accent color"), selection: st.bind(\.accent, then: { Theme.apply(AppController.shared.settings) })) {
                ForEach(Theme.accents, id: \.id) { a in
                    HStack {
                        Circle().fill(Color(nsColor: Theme.accentNS(a.id))).frame(width: 12, height: 12)
                        Text(S.t(a.pt, a.en))
                    }
                    .tag(a.id)
                }
            }
        }
        .formStyle(.grouped)
    }

    // ---------- Depois de capturar ----------

    private var after: some View {
        Form {
            Picker(S.t("Abrir", "Open"), selection: st.bind(\.afterCapture)) {
                VStack(alignment: .leading) {
                    Text(S.t("Editor rápido", "Quick editor"))
                    Text(S.t("Pop-up junto à barra de menus: seta, caixa, texto, esbater, passos, recorte.", "Pop-up by the menu bar: arrow, box, text, blur, steps, crop."))
                        .font(.caption).foregroundStyle(.secondary)
                }.tag("quick")
                VStack(alignment: .leading) {
                    Text(S.t("Editor completo", "Full editor"))
                    Text(S.t("Todas as ferramentas, propriedades e lista de anotações.", "All tools, properties and the annotation list."))
                        .font(.caption).foregroundStyle(.secondary)
                }.tag("full")
                VStack(alignment: .leading) {
                    Text(S.t("Nada", "Nothing"))
                    Text(S.t("Só guardar e copiar, com notificação.", "Just save and copy, with a notification."))
                        .font(.caption).foregroundStyle(.secondary)
                }.tag("none")
            }
            .pickerStyle(.radioGroup)
            Section {
                Picker(S.t("Cantos das janelas", "Window corners"), selection: st.bind(\.windowCorners)) {
                    Text(S.t("Fundo branco", "White background")).tag("white")
                    Text(S.t("Fundo preto", "Black background")).tag("black")
                    Text(S.t("Transparentes (PNG)", "Transparent (PNG)")).tag("transparent")
                    Text(S.t("Fundo do ecrã", "Screen background")).tag("screen")
                }
            } footer: {
                Text(S.t("Ao clicar numa janela no modo captura, ou ao capturar a janela ativa. As janelas do macOS têm cantos arredondados: em vez do que está por trás, os cantos ficam com este fundo.",
                         "When you click a window in capture mode, or capture the active window. macOS windows have rounded corners: instead of whatever is behind them, the corners get this background."))
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Toggle(S.t("Copiar para a área de transferência", "Copy to the clipboard"), isOn: st.bind(\.copyToClipboard))
                Toggle(S.t("Mostrar notificação", "Show a notification"), isOn: st.bind(\.showNotification))
            } footer: {
                Text(S.t("A imagem fica pronta para colar com ⌘V. A notificação aparece quando nada abre depois de capturar e quando um vídeo é guardado.",
                         "The image is ready to paste with ⌘V. The notification appears when nothing opens after a capture and when a video is saved."))
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    // ---------- Atalhos ----------

    private var shortcuts: some View {
        Form {
            Section {
                row(S.t("Modo captura (janela / região)", "Capture mode (window / region)"), \.hotkeyRegion)
                row(S.t("Janela ativa", "Active window"), \.hotkeyWindow)
                row(S.t("Todos os ecrãs", "All screens"), \.hotkeyFull)
                row(S.t("Gravar vídeo (iniciar / parar)", "Record video (start / stop)"), \.hotkeyRecord)
            } footer: {
                Text(S.t("Clique num atalho e prima a nova combinação (com ⌘, ⌃ ou ⌥). ⌫ remove o atalho, Esc cancela. Os atalhos do macOS (⌘⇧3, ⌘⇧4, ⌘⇧5) continuam a funcionar.",
                         "Click a shortcut and press the new combination (with ⌘, ⌃ or ⌥). ⌫ removes it, Esc cancels. The macOS shortcuts (⌘⇧3, ⌘⇧4, ⌘⇧5) keep working."))
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func row(_ title: String, _ kp: ReferenceWritableKeyPath<Settings, Hotkey>) -> some View {
        HStack {
            Text(title)
            Spacer()
            HotkeyField(value: st.bind(kp, then: { AppController.shared.registerHotkeys(notify: true) }))
                .frame(width: 150, height: 24)
        }
    }

    // ---------- Ficheiros ----------

    private var files: some View {
        Form {
            LabeledContent(S.t("Pasta das capturas", "Captures folder")) {
                HStack {
                    Text((st.s.outputFolder as NSString).abbreviatingWithTildeInPath)
                        .lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                    Button(S.t("Escolher…", "Choose…")) { chooseFolder() }
                }
            }
            Button(S.t("Mostrar no Finder", "Show in Finder")) { AppController.shared.openFolder() }
            Picker(S.t("Formato das imagens", "Image format"), selection: st.bind(\.format)) {
                Text("PNG").tag("png")
                Text("JPG").tag("jpg")
            }
            .pickerStyle(.segmented)
        }
        .formStyle(.grouped)
    }

    private func chooseFolder() {
        let p = NSOpenPanel()
        p.canChooseDirectories = true
        p.canChooseFiles = false
        p.canCreateDirectories = true
        p.directoryURL = st.s.outputURL
        if p.runModal() == .OK, let u = p.url {
            st.bind(\.outputFolder).wrappedValue = u.path
            NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
        }
    }

    // ---------- Vídeo ----------

    private var video: some View {
        Form {
            Picker(S.t("Fotogramas por segundo", "Frames per second"), selection: st.bind(\.videoFps)) {
                Text("15").tag(15)
                Text("30").tag(30)
                Text("60").tag(60)
            }
            .pickerStyle(.segmented)
            Toggle(S.t("Mostrar o cursor no vídeo", "Show the cursor in the video"), isOn: st.bind(\.videoCursor))
            Text(S.t("Os vídeos são guardados em MP4 (H.264) na pasta das capturas, sem som.", "Videos are saved as MP4 (H.264) in the captures folder, without sound."))
                .font(.caption).foregroundStyle(.secondary)
        }
        .formStyle(.grouped)
    }
}

// ================= Sobre: versão e atualizações =================

extension SettingsView {
    var about: some View {
        let up = UpdateChecker.shared
        let _ = st.updateTick
        let last = st.s.lastUpdateCheck
        let lastText: String = {
            guard last > 0 else { return S.t("Ainda não foi verificado.", "Not checked yet.") }
            let f = DateFormatter()
            f.locale = Locale(identifier: S.pt ? "pt_PT" : "en_US")
            f.dateStyle = .medium; f.timeStyle = .short
            return S.t("Última verificação: ", "Last checked: ") + f.string(from: Date(timeIntervalSince1970: last))
        }()
        return Form {
            Section {
                HStack(spacing: 14) {
                    Image(nsImage: AppIcon.image(size: 64))
                    VStack(alignment: .leading, spacing: 4) {
                        Text("SnapStudio").font(.title2.bold())
                        Text(S.t("Versão ", "Version ") + UpdateChecker.currentVersion).foregroundStyle(.secondary)
                            .textSelection(.enabled)
                    }
                    Spacer()
                }
                .padding(.vertical, 4)
            }
            Section {
                if let r = up.pending {
                    HStack {
                        Image(systemName: "arrow.down.circle.fill").foregroundStyle(Theme.accentColor)
                        Text(S.t("Está disponível a versão \(r.version).", "Version \(r.version) is available."))
                        Spacer()
                        Button(S.t("Ver e descarregar…", "View and download…")) { up.showUpdateDialog(r) }
                            .buttonStyle(.borderedProminent)
                    }
                }
                HStack {
                    Text(up.checking ? S.t("A procurar…", "Checking…") : lastText).foregroundStyle(.secondary)
                    Spacer()
                    Button(S.t("Procurar atualizações", "Check for Updates")) { up.check(interactive: true) }
                        .disabled(up.checking)
                }
                Toggle(S.t("Procurar atualizações automaticamente", "Check for updates automatically"), isOn: st.bind(\.autoUpdateCheck))
            } footer: {
                Text(S.t("A SnapStudio consulta a página de versões no GitHub uma vez por dia. Não envia dados seus.",
                         "SnapStudio checks the releases page on GitHub once a day. No personal data is sent."))
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Link(S.t("Página do projeto no GitHub", "Project page on GitHub"), destination: UpdateChecker.repoURL)
                Link(S.t("Todas as versões", "All releases"), destination: UpdateChecker.releasesURL)
            }
        }
        .formStyle(.grouped)
    }
}

// ================= campo para gravar um atalho =================

struct HotkeyField: NSViewRepresentable {
    @Binding var value: Hotkey

    func makeNSView(context: Context) -> HotkeyRecorderView {
        let v = HotkeyRecorderView()
        v.onChange = { value = $0 }
        v.value = value
        return v
    }

    func updateNSView(_ v: HotkeyRecorderView, context: Context) {
        v.onChange = { value = $0 }
        if !v.isRecording { v.value = value }
    }
}

final class HotkeyRecorderView: NSView {
    var value = Hotkey.none { didSet { needsDisplay = true } }
    var onChange: ((Hotkey) -> Void)?
    private(set) var isRecording = false { didSet { needsDisplay = true } }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        isRecording = true
        AppController.shared.suspendHotkeys()
    }

    override func resignFirstResponder() -> Bool {
        if isRecording { isRecording = false; AppController.shared.registerHotkeys(notify: false) }
        return true
    }

    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        guard isRecording else { return false }
        keyDown(with: event)
        return true
    }

    override func keyDown(with event: NSEvent) {
        guard isRecording else { super.keyDown(with: event); return }
        let code = Int(event.keyCode)
        let mods = event.modifierFlags.intersection([.command, .control, .option, .shift])
        if code == kVK_Escape { finish(nil); return }
        if (code == kVK_Delete || code == kVK_ForwardDelete) && mods.isEmpty { finish(Hotkey.none); return }
        let isFKey = (kVK_F1...kVK_F20).contains(code) || [kVK_F13, kVK_F14, kVK_F15, kVK_F16, kVK_F17, kVK_F18, kVK_F19].contains(code)
        // é preciso pelo menos ⌘, ⌃ ou ⌥ (ou uma tecla de função), para não roubar teclas normais
        guard isFKey || !mods.intersection([.command, .control, .option]).isEmpty else { NSSound.beep(); return }
        finish(Hotkey(keyCode: UInt32(code), modifiers: Hotkey.carbon(mods)))
    }

    private func finish(_ h: Hotkey?) {
        isRecording = false
        if let h = h { value = h; onChange?(h) }
        window?.makeFirstResponder(nil)
        AppController.shared.registerHotkeys(notify: h != nil)
    }

    override func draw(_ dirtyRect: NSRect) {
        let r = bounds.insetBy(dx: 1, dy: 1)
        let p = NSBezierPath(roundedRect: r, xRadius: 6, yRadius: 6)
        (isRecording ? Theme.accent.withAlphaComponent(0.15) : NSColor.controlBackgroundColor).setFill()
        p.fill()
        (isRecording ? Theme.accent : NSColor.separatorColor).setStroke()
        p.lineWidth = isRecording ? 2 : 1
        p.stroke()
        let text = isRecording ? S.t("Prima o atalho…", "Press shortcut…") : value.display
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 12, weight: isRecording ? .regular : .medium),
            .foregroundColor: isRecording ? NSColor.secondaryLabelColor : NSColor.labelColor,
        ]
        let s = (text as NSString).size(withAttributes: attrs)
        (text as NSString).draw(at: CGPoint(x: r.midX - s.width / 2, y: r.midY - s.height / 2), withAttributes: attrs)
    }
}
