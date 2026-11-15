import AppKit
import SwiftUI

// Painel que abre ao clicar no ícone da barra de menus — igual ao TrayPanel.cs da versão Windows:
// cabeçalho, Capturar, 3 botões compactos, Gravar vídeo, recentes (a grelha enche o espaço),
// aviso da área de transferência, interruptor do editor e rodapé. Redimensionável; o tamanho fica guardado.

final class TrayModel: ObservableObject {
    @Published var recents: [CaptureItem] = []
    @Published var recording = false
    @Published var openEditor = true
    @Published var clipboardHasImage = false
    @Published var update: ReleaseInfo?
    private var tokens: [NSObjectProtocol] = []
    static let maxTiles = 40

    init() {
        refresh()
        let nc = NotificationCenter.default
        for n in [Notification.Name.snapCapturesChanged, .snapRecordingChanged, .snapThemeChanged, .snapUpdateChanged] {
            tokens.append(nc.addObserver(forName: n, object: nil, queue: .main) { [weak self] _ in self?.refresh() })
        }
    }
    deinit { tokens.forEach(NotificationCenter.default.removeObserver) }

    func refresh() {
        let app = AppController.shared
        recents = Array(CaptureItem.scan(app.settings.outputURL).prefix(TrayModel.maxTiles))
        recording = app.isRecording
        openEditor = app.settings.afterCapture != "none"
        let types = NSPasteboard.general.types ?? []
        clipboardHasImage = types.contains(.png) || types.contains(.tiff) || types.contains(.fileURL)
        update = UpdateChecker.shared.pending
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

    let window: TrayWindow
    let model = TrayModel()
    private var lastHidden = Date.distantPast

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
        window.onCancel = { [weak self] in self?.hide() }
        let host = NSHostingView(rootView: TrayPanelView(model: model))
        host.autoresizingMask = [.width, .height]
        window.contentView = host
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
    }

    func windowDidResignKey(_ notification: Notification) {
        if window.inLiveResize { return }
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

            HStack {
                Text(S.t("Recentes", "Recent")).font(.system(size: 13, weight: .bold)).foregroundStyle(Pal.text)
                Spacer()
                Button { app.showHistory() } label: {
                    Text(S.t("Ver histórico", "View history")).font(.system(size: 13)).padding(.horizontal, 12).frame(height: 28)
                }
                .buttonStyle(FlatButtonStyle(kind: .ghost))
            }
            .padding(.leading, 16).padding(.trailing, pad)
            .padding(.top, 8).frame(height: 40, alignment: .center)

            RecentsGrid(items: model.recents, pad: pad)
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
            footerButton(S.t("Sair", "Exit")) { NSApp.terminate(nil) }
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

/// Grelha de recentes: colunas pela largura (mín. 120 pt), linhas pela altura disponível; lugares vazios tracejados.
struct RecentsGrid: View {
    let items: [CaptureItem]
    let pad: CGFloat
    private let gap: CGFloat = 8

    var body: some View {
        GeometryReader { geo in
            let availW = geo.size.width - 2 * pad, availH = geo.size.height
            let cols = max(2, Int((availW + gap) / (120 + gap)))
            let tw = (availW - gap * CGFloat(cols - 1)) / CGFloat(cols)
            let th0 = (tw * 0.58).rounded() + 24
            let rows = max(1, Int((availH + gap) / (th0 + gap)))
            let th = max(48, min(th0, availH))      // painel baixo: os cartões encolhem em vez de tapar o resto
            let count = min(TrayModel.maxTiles, cols * rows)
            VStack(alignment: .leading, spacing: gap) {
                ForEach(0..<rows, id: \.self) { r in
                    HStack(spacing: gap) {
                        ForEach(0..<cols, id: \.self) { c in
                            let i = r * cols + c
                            if i < count && i < items.count {
                                ThumbTile(item: items[i]).frame(width: tw, height: th)
                            } else {
                                RoundedRectangle(cornerRadius: 8)
                                    .strokeBorder(Pal.border, style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                                    .frame(width: tw, height: th)
                            }
                        }
                    }
                }
            }
            .padding(.horizontal, pad)
        }
        .frame(minHeight: 60)
    }
}

struct ThumbTile: View {
    let item: CaptureItem
    @StateObject private var hover = Box(false)

    var body: some View {
        Button {
            AppController.shared.openEditor(item.url, image: nil, compact: !item.isVideo)
        } label: {
            VStack(spacing: 0) {
                // a miniatura não pode influenciar o tamanho do cartão (capturas muito altas ou largas)
                Pal.surface2
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .overlay(alignment: .top) { ThumbView(url: item.url, size: 240) }
                    .clipped()
                Rectangle().fill(Pal.border).frame(height: 1)
                Text(RelTime.short(item.date))
                    .font(.system(size: 11.5)).foregroundStyle(Pal.muted).lineLimit(1)
                    .padding(.horizontal, 7)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .frame(height: 23)
            }
            .background(Pal.surface)
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(hover.value ? Theme.accentColor : Pal.border, lineWidth: hover.value ? 1.5 : 1))
            .contentShape(RoundedRectangle(cornerRadius: 8))
        }
        .buttonStyle(.plain)
        .onHover { hover.value = $0 }
        .help(item.name)
        .onDrag { NSItemProvider(contentsOf: item.url) ?? NSItemProvider() }
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
