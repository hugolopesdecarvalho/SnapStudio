import AppKit
import SwiftUI

// Painel que abre ao clicar no ícone da barra de menus — igual ao TrayPanel.cs da versão Windows:
// cabeçalho, Capturar, 3 botões compactos, Gravar vídeo, recentes (a grelha enche o espaço),
// aviso da área de transferência, interruptor do editor e rodapé. Redimensionável; o tamanho fica guardado.

final class TrayModel: ObservableObject {
    /// Todas as capturas da pasta (imagens e vídeos), da mais recente para a mais antiga.
    @Published var recents: [CaptureItem] = []
    @Published var recording = false
    @Published var openEditor = true
    @Published var clipboardHasImage = false
    @Published var update: ReleaseInfo?
    private var tokens: [NSObjectProtocol] = []
    private var watcher: DispatchSourceFileSystemObject?
    private var watchedPath = ""
    private var watchPending = false

    // seleção na grelha de recentes (ver RecentsGrid.swift)
    /// Capturas selecionadas (clique, ⌘-clique para juntar/retirar, ⇧-clique para um intervalo, ⌘A para todas).
    @Published var selection: Set<URL> = []
    /// Retângulo de seleção a ser arrastado (coordenadas do conteúdo da grelha).
    @Published var marquee: CGRect?
    var anchor: URL?
    var marqueeBase: Set<URL> = []
    var marqueeStart: CGPoint?
    /// Posição do rato no retângulo visível da grelha (para o deslocamento automático).
    var marqueePointer: CGPoint = .zero
    var layout = GridLayout()
    var viewportHeight: CGFloat = 0
    /// minY do conteúdo no retângulo visível (0 no topo, negativo depois de deslocar).
    var scrollOffset: CGFloat = 0
    var autoScrollDir = 0
    var autoScrollTimer: Timer?
    /// Desloca a grelha até uma linha (definido pela vista, com o ScrollViewProxy).
    var scrollToRow: ((Int, UnitPoint) -> Void)?

    init() {
        refresh()
        let nc = NotificationCenter.default
        for n in [Notification.Name.snapCapturesChanged, .snapRecordingChanged, .snapThemeChanged, .snapUpdateChanged] {
            tokens.append(nc.addObserver(forName: n, object: nil, queue: .main) { [weak self] _ in self?.refresh() })
        }
    }
    deinit {
        tokens.forEach(NotificationCenter.default.removeObserver)
        autoScrollTimer?.invalidate()
        watcher?.cancel()
    }

    func refresh() {
        let app = AppController.shared
        watchFolder(app.settings.outputURL)
        let items = CaptureItem.scan(app.settings.outputURL)
        if items != recents { recents = items }
        let urls = Set(items.map { $0.url })
        if !selection.isSubset(of: urls) { selection.formIntersection(urls) }
        if let a = anchor, !urls.contains(a) { anchor = nil }
        recording = app.isRecording
        openEditor = app.settings.afterCapture != "none"
        let types = NSPasteboard.general.types ?? []
        clipboardHasImage = types.contains(.png) || types.contains(.tiff) || types.contains(.fileURL)
        update = UpdateChecker.shared.pending
    }

    /// Vigia a pasta das capturas: ficheiros apagados, renomeados ou acrescentados fora da app (Finder…) atualizam a grelha.
    private func watchFolder(_ url: URL) {
        guard url.path != watchedPath else { return }
        watcher?.cancel()
        watcher = nil
        watchedPath = ""
        let fd = Darwin.open(url.path, O_EVTONLY)
        guard fd >= 0 else { return }      // a pasta ainda não existe: tenta de novo no próximo refresh
        watchedPath = url.path
        let src = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .delete, .rename], queue: .main)
        src.setEventHandler { [weak self] in self?.folderChanged() }
        src.setCancelHandler { Darwin.close(fd) }
        src.resume()
        watcher = src
    }

    private func folderChanged() {
        guard !watchPending else { return }
        watchPending = true
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            self?.watchPending = false
            self?.refresh()
        }
    }
}

// ================= janela =================

final class TrayWindow: NSPanel {
    var onCancel: (() -> Void)?
    override var canBecomeKey: Bool { true }
    override func cancelOperation(_ sender: Any?) { onCancel?() }
}

final class TrayPanelController: NSObject, NSWindowDelegate {
    static let defaultSize = CGSize(width: 460, height: 620)
    static let minSize = CGSize(width: 360, height: 500)
    /// > 0 enquanto um alerta aberto a partir do painel está no ecrã (o painel não fecha ao perder o foco).
    private(set) static var alertDepth = 0

    let window: TrayWindow
    let model = TrayModel()
    private var lastHidden = Date.distantPast
    private var keyMonitor: Any?

    override init() {
        let st = AppController.shared.settings
        let size = CGSize(width: max(Self.minSize.width, CGFloat(st.panelWidth > 0 ? st.panelWidth : Int(Self.defaultSize.width))),
                          height: max(Self.minSize.height, CGFloat(st.panelHeight > 0 ? st.panelHeight : Int(Self.defaultSize.height))))
        window = TrayWindow(contentRect: CGRect(origin: .zero, size: size), styleMask: [.borderless, .resizable, .fullSizeContentView, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        super.init()
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = true
        window.level = .popUpMenu
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        window.isReleasedWhenClosed = false
        window.becomesKeyOnlyIfNeeded = false
        window.hidesOnDeactivate = false
        window.minSize = Self.minSize
        window.animationBehavior = .utilityWindow
        window.delegate = self
        // Esc: primeiro limpa a seleção da grelha, depois fecha o painel
        window.onCancel = { [weak self] in
            guard let self else { return }
            if !self.model.selection.isEmpty { self.model.clearSelection() } else { self.hide() }
        }
        let host = NSHostingView(rootView: TrayPanelView(model: model))
        host.autoresizingMask = [.width, .height]
        window.contentView = host
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
            guard let self, e.window === self.window, self.window.isVisible else { return e }
            return self.handleKey(e) ? nil : e
        }
    }

    deinit { if let k = keyMonitor { NSEvent.removeMonitor(k) } }

    /// ⌫ / ⌦ (ou ⌘⌫) apagam as capturas selecionadas; ⌘A seleciona todas.
    private func handleKey(_ e: NSEvent) -> Bool {
        let mods = e.modifierFlags.intersection(.deviceIndependentFlagsMask)
        if (e.keyCode == 51 || e.keyCode == 117) && mods.isDisjoint(with: [.shift, .option, .control]) {
            guard !model.selection.isEmpty else { return false }
            let items = model.selectedItems
            // fora do tratamento do evento: o alerta de confirmação corre num ciclo modal
            DispatchQueue.main.async { [weak self] in self?.model.trash(items) }
            return true
        }
        if mods.contains(.command) && mods.isDisjoint(with: [.shift, .option, .control]) && e.charactersIgnoringModifiers?.lowercased() == "a" {
            model.selectAll()
            return true
        }
        return false
    }

    /// Mostra um alerta modal por cima do painel sem o fechar (o painel está ao nível dos menus).
    @discardableResult
    static func runAlert(_ a: NSAlert) -> NSApplication.ModalResponse {
        alertDepth += 1
        NSApp.activate(ignoringOtherApps: true)
        a.window.level = NSWindow.Level(rawValue: NSWindow.Level.popUpMenu.rawValue + 1)
        let r = a.runModal()
        alertDepth -= 1
        if let w = NSApp.windows.first(where: { $0 is TrayWindow && $0.isVisible }) { w.makeKey() }
        return r
    }

    var isVisible: Bool { window.isVisible }

    func toggle(below button: NSStatusBarButton?) {
        if window.isVisible { hide(); return }
        // o clique no ícone fecha o painel (perde o foco) e chega logo a seguir: não reabrir
        if Date().timeIntervalSince(lastHidden) < 0.35 { return }
        show(below: button)
    }

    func show(below button: NSStatusBarButton?) {
        model.refresh()
        let anchor = button?.window?.frame
        let screen = button?.window?.screen ?? NSScreen.main ?? NSScreen.screens[0]
        let vis = screen.visibleFrame
        var size = window.frame.size
        size.width = min(size.width, vis.width - 24)
        size.height = min(size.height, vis.height - 16)
        window.maxSize = CGSize(width: vis.width - 24, height: vis.height - 16)
        var x = (anchor?.midX ?? vis.maxX) - size.width / 2
        x = max(vis.minX + 8, min(vis.maxX - size.width - 8, x))
        let y = vis.maxY - size.height - 6
        window.setFrame(CGRect(x: x, y: y, width: size.width, height: size.height), display: true)
        // painel "não ativante" (como o Spotlight): recebe cliques e teclado sem trazer a app para a frente
        window.makeKeyAndOrderFront(nil)
    }

    func hide() {
        guard window.isVisible else { return }
        window.orderOut(nil)
        lastHidden = Date()
        model.marqueeEnded()
        model.clearSelection()
    }

    func windowDidResignKey(_ notification: Notification) {
        if window.inLiveResize || Self.alertDepth > 0 { return }
        hide()
    }

    func windowDidEndLiveResize(_ notification: Notification) {
        let st = AppController.shared.settings
        st.panelWidth = Int(window.frame.width.rounded())
        st.panelHeight = Int(window.frame.height.rounded())
        st.save()
        window.makeKey()
    }
}

// ================= botões ao estilo da versão Windows =================

enum BtnKind { case primary, secondary, ghost }

struct FlatButtonStyle: ButtonStyle {
    let kind: BtnKind
    var radius: CGFloat = 8

    func makeBody(configuration: Configuration) -> some View {
        FlatButtonBody(kind: kind, radius: radius, pressed: configuration.isPressed, label: configuration.label)
    }
}

private struct FlatButtonBody<L: View>: View {
    let kind: BtnKind
    let radius: CGFloat
    let pressed: Bool
    let label: L
    @StateObject private var hover = Box(false)

    var body: some View {
        let h = hover.value
        let bg: Color
        let fg: Color
        switch kind {
        case .primary: bg = (pressed || h) ? Pal.accentHover : Theme.accentColor; fg = .white
        case .secondary: bg = pressed ? Pal.pressed : h ? Pal.hover : Pal.surface2; fg = Pal.text
        case .ghost: bg = pressed ? Pal.pressed : h ? Pal.hover : .clear; fg = Pal.text
        }
        return label
            .foregroundStyle(fg)
            .background(RoundedRectangle(cornerRadius: radius).fill(bg))
            .contentShape(RoundedRectangle(cornerRadius: radius))
            .onHover { hover.value = $0 }
    }
}

// ================= vista =================

struct TrayPanelView: View {
    @ObservedObject var model: TrayModel
    @StateObject private var theme = ThemeObserver()
    private var app: AppController { AppController.shared }
    private let pad: CGFloat = 14

    var body: some View {
        VStack(spacing: 0) {
            header
            VStack(spacing: 6) {
                captureButton
                HStack(spacing: 6) {
                    compact("display", S.t("Ecrã inteiro", "Full screen"), S.t("Capturar todos os ecrãs", "Capture all screens")) { app.startCapture(.fullScreen) }
                    compact("macwindow", S.t("Janela ativa", "Active window"), S.t("Capturar a janela que estava ativa", "Capture the window that was active")) { app.startCapture(.activeWindow) }
                    compact("timer", S.t("Atraso 3 s", "3 s delay"), S.t("Modo captura daqui a 3 segundos", "Capture mode in 3 seconds")) { app.startCapture(.region, delay: 3) }
                }
                recordButton
            }
            .padding(.horizontal, pad)
            .padding(.bottom, 14)

            Rectangle().fill(Pal.border).frame(height: 1)

            HStack(spacing: 6) {
                Text(S.t("Recentes", "Recent")).font(.system(size: 13, weight: .bold)).foregroundStyle(Pal.text)
                Spacer()
                if !model.selection.isEmpty {
                    Text(selectedLabel(model.selection.count)).font(.system(size: 12.5)).foregroundStyle(Pal.muted)
                    Button { model.trash(model.selectedItems) } label: {
                        Image(systemName: "trash").font(.system(size: 14)).frame(width: 32, height: 28)
                    }
                    .buttonStyle(FlatButtonStyle(kind: .ghost))
                    .help(S.t("Mover as capturas selecionadas para o Lixo (⌫)", "Move the selected captures to the Trash (⌫)"))
                }
            }
            .padding(.leading, 16).padding(.trailing, pad)
            .padding(.top, 8).frame(height: 40, alignment: .center)

            RecentsGrid(model: model, pad: pad)
                .padding(.bottom, 12)

            clipBox
                .padding(.horizontal, pad)
                .padding(.bottom, 10)

            Rectangle().fill(Pal.border).frame(height: 1)

            ToggleRowView(title: S.t("Abrir no editor depois de capturar", "Open in the editor after capture"), isOn: Binding(
                get: { model.openEditor },
                set: { on in
                    let st = app.settings
                    let v = on ? (st.afterCapture == "full" ? "full" : "quick") : "none"
                    if st.afterCapture != v { st.afterCapture = v; st.save() }
                    model.openEditor = on
                }))
                .padding(.horizontal, 16)
                .frame(height: 44)

            footer
        }
        .background(Pal.panel)
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Pal.borderStrong, lineWidth: 1))
        .tint(Theme.accentColor)
        .id(theme.tick)
    }

    // ---------- partes ----------

    private func selectedLabel(_ n: Int) -> String {
        n == 1 ? S.t("1 selecionada", "1 selected") : S.t("\(n) selecionadas", "\(n) selected")
    }

    private var header: some View {
        HStack(spacing: 8) {
            Image(nsImage: AppIcon.image(size: 26)).padding(.leading, 16)
            Text("SnapStudio").font(.system(size: 15, weight: .bold)).foregroundStyle(Pal.text)
            Spacer()
            iconButton(Theme.isDark ? "sun.max" : "moon",
                       Theme.isDark ? S.t("Mudar para tema claro", "Switch to light theme") : S.t("Mudar para tema escuro", "Switch to dark theme")) { app.toggleTheme() }
            iconButton("gearshape", S.t("Definições", "Settings")) { app.showSettings() }
                .padding(.trailing, pad)
        }
        .frame(height: 58)
    }

    private func iconButton(_ symbol: String, _ help: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 16)).frame(width: 36, height: 36)
        }
        .buttonStyle(FlatButtonStyle(kind: .ghost))
        .help(help)
    }

    private var captureButton: some View {
        Button { app.startCapture(.region) } label: {
            HStack(spacing: 8) {
                Image(systemName: "viewfinder").font(.system(size: 17, weight: .semibold))
                Text(S.t("Capturar janela ou região", "Capture window or region")).font(.system(size: 13.5, weight: .bold))
                badge(app.settings.hotkeyRegion).padding(.leading, 2)
            }
            .frame(maxWidth: .infinity).frame(height: 40)
        }
        .buttonStyle(FlatButtonStyle(kind: .primary))
    }

    private func badge(_ k: Hotkey) -> some View {
        Group {
            if !k.isNone {
                Text(k.display).font(.system(size: 12, weight: .bold))
                    .padding(.horizontal, 6).frame(height: 22)
                    .background(RoundedRectangle(cornerRadius: 5).fill(Color.white.opacity(0.2)))
            }
        }
    }

    private func compact(_ symbol: String, _ title: String, _ help: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 6) {
                Image(systemName: symbol).font(.system(size: 14))
                Text(title).font(.system(size: 12.5)).lineLimit(1).minimumScaleFactor(0.8)
            }
            .frame(maxWidth: .infinity).frame(height: 34)
        }
        .buttonStyle(FlatButtonStyle(kind: .secondary, radius: 7))
        .help(help)
    }

    private var recordButton: some View {
        Button { app.toggleRecording() } label: {
            HStack(spacing: 8) {
                Image(systemName: model.recording ? "stop.circle.fill" : "record.circle")
                    .font(.system(size: 16)).foregroundStyle(Color(nsColor: .systemRed))
                Text(model.recording ? S.t("Parar a gravação", "Stop recording") : S.t("Gravar vídeo do ecrã", "Record screen video"))
                    .font(.system(size: 13))
                Spacer()
                if !app.settings.hotkeyRecord.isNone {
                    Text(app.settings.hotkeyRecord.display).font(.system(size: 12)).foregroundStyle(Pal.muted)
                }
            }
            .padding(.horizontal, 12).frame(height: 36)
        }
        .buttonStyle(FlatButtonStyle(kind: .secondary, radius: 7))
    }

    private var clipBox: some View {
        Group {
            if let u = model.update {
                Button { UpdateChecker.shared.showUpdateDialog(u) } label: {
                    HStack(spacing: 10) {
                        Image(systemName: "arrow.down.circle.fill").font(.system(size: 16))
                        Text(S.t("A SnapStudio \(u.version) está disponível", "SnapStudio \(u.version) is available"))
                            .font(.system(size: 12.5, weight: .semibold))
                        Spacer(minLength: 0)
                        Text(S.t("Atualizar", "Update")).font(.system(size: 12.5, weight: .bold))
                    }
                    .foregroundStyle(.white)
                    .padding(.horizontal, 12)
                    .frame(maxWidth: .infinity, minHeight: 44)
                    .background(RoundedRectangle(cornerRadius: 8).fill(Theme.accentColor))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            } else if model.clipboardHasImage {
                HStack(spacing: 10) {
                    Image(systemName: "doc.on.clipboard").font(.system(size: 15))
                    Text(S.t("A última captura está na área de transferência — cole com ⌘V", "Your last capture is on the clipboard — paste with ⌘V"))
                        .font(.system(size: 12.5)).fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                }
                .foregroundStyle(Pal.accentSoftText)
                .padding(.horizontal, 12)
                .frame(maxWidth: .infinity, minHeight: 44)
                .background(RoundedRectangle(cornerRadius: 8).fill(Pal.accentSoft))
            } else {
                Text(S.t("Dica: \(app.settings.hotkeyRegion.display) captura · \(app.settings.hotkeyRecord.display) grava vídeo · arraste as bordas para aumentar o painel",
                         "Tip: \(app.settings.hotkeyRegion.display) captures · \(app.settings.hotkeyRecord.display) records video · drag the edges to enlarge this panel"))
                    .font(.system(size: 12)).foregroundStyle(Pal.muted)
                    .multilineTextAlignment(.center)
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
        }
    }

    private var footer: some View {
        HStack(spacing: 4) {
            footerButton(S.t("Abrir imagem…", "Open image…")) { app.openImageDialog() }
            footerButton(S.t("Pasta", "Folder")) { app.openFolder() }
            Spacer()
            Button { app.showSettings(tab: "about") } label: {
                Text("v" + UpdateChecker.currentVersion).font(.system(size: 11.5)).foregroundStyle(Pal.muted)
                    .padding(.horizontal, 6).frame(height: 36)
            }
            .buttonStyle(.plain)
            .help(S.t("Versão e atualizações", "Version and updates"))
            footerButton(S.t("Sair", "Exit")) { app.confirmQuit() }
        }
        .padding(.horizontal, 10)
        .frame(height: 52)
        .frame(maxWidth: .infinity)
        .background(Pal.footer)
        .overlay(alignment: .top) { Rectangle().fill(Pal.border).frame(height: 1) }
    }

    private func footerButton(_ title: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title).font(.system(size: 13)).padding(.horizontal, 12).frame(height: 36)
        }
        .buttonStyle(FlatButtonStyle(kind: .ghost))
    }
}

/// Interruptor como o ToggleRow da versão Windows: texto à esquerda, interruptor à direita.
struct ToggleRowView: View {
    let title: String
    @Binding var isOn: Bool

    var body: some View {
        HStack {
            Text(title).font(.system(size: 13)).foregroundStyle(Pal.text)
            Spacer()
            Toggle("", isOn: $isOn).toggleStyle(.switch).labelsHidden().controlSize(.small)
        }
        .contentShape(Rectangle())
        .onTapGesture { isOn.toggle() }
    }
}
