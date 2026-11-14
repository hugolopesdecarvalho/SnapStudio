import AppKit
import SwiftUI

final class TrayModel: ObservableObject {
    @Published var recents: [CaptureItem] = []
    @Published var recording = false
    @Published var openEditor = true
    @Published var copied = false
    private var tokens: [NSObjectProtocol] = []

    init() {
        refresh()
        let nc = NotificationCenter.default
        for n in [Notification.Name.snapCapturesChanged, .snapRecordingChanged, .snapThemeChanged] {
            tokens.append(nc.addObserver(forName: n, object: nil, queue: .main) { [weak self] _ in self?.refresh() })
        }
    }
    deinit { tokens.forEach(NotificationCenter.default.removeObserver) }

    func refresh() {
        let app = AppController.shared
        recents = Array(CaptureItem.scan(app.settings.outputURL).prefix(6))
        recording = app.isRecording
        openEditor = app.settings.afterCapture != "none"
        copied = app.lastCopied
    }
}

/// Painel que abre ao clicar no ícone da barra de menus.
struct TrayPanelView: View {
    @StateObject private var m = TrayModel()
    @StateObject private var theme = ThemeObserver()
    private var app: AppController { AppController.shared }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Image(nsImage: AppIcon.image(size: 26))
                Text("SnapStudio").font(.system(size: 15, weight: .bold))
                Spacer()
                IconButton(symbol: Theme.isDark ? "sun.max" : "moon",
                           help: Theme.isDark ? S.t("Mudar para tema claro", "Switch to light theme") : S.t("Mudar para tema escuro", "Switch to dark theme")) {
                    app.toggleTheme()
                }
                IconButton(symbol: "gearshape", help: S.t("Definições", "Settings")) { app.showSettings() }
            }

            Button { app.startCapture(.region) } label: {
                HStack {
                    Image(systemName: "viewfinder").font(.system(size: 17, weight: .semibold))
                    Text(S.t("Capturar janela ou região", "Capture window or region")).font(.system(size: 14, weight: .semibold))
                    Spacer()
                    Text(app.settings.hotkeyRegion.display)
                        .font(.system(size: 11, weight: .medium))
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(Capsule().fill(Color.white.opacity(0.22)))
                }
                .foregroundStyle(.white)
                .padding(.horizontal, 14).frame(height: 44)
                .background(RoundedRectangle(cornerRadius: 10).fill(Theme.accentColor))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            Button { app.toggleRecording() } label: {
                HStack {
                    Image(systemName: m.recording ? "stop.circle.fill" : "record.circle").foregroundStyle(.red)
                    Text(m.recording ? S.t("Parar a gravação", "Stop recording") : S.t("Gravar vídeo do ecrã", "Record screen video"))
                    Spacer()
                    Text(app.settings.hotkeyRecord.display).font(.system(size: 11)).foregroundStyle(.secondary)
                }
                .padding(.horizontal, 14).frame(height: 36)
                .background(RoundedRectangle(cornerRadius: 9).fill(Color.primary.opacity(0.07)))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            HStack(spacing: 8) {
                tile("display", S.t("Ecrã inteiro", "Full screen"), S.t("Capturar todos os ecrãs", "Capture all screens")) { app.startCapture(.fullScreen) }
                tile("macwindow", S.t("Janela ativa", "Active window"), S.t("Capturar a janela que estava ativa", "Capture the window that was active")) { app.startCapture(.activeWindow) }
                tile("timer", S.t("Atraso 3 s", "3 s delay"), S.t("Modo captura daqui a 3 segundos", "Capture mode in 3 seconds")) { app.startCapture(.region, delay: 3) }
            }

            Toggle(S.t("Abrir no editor depois de capturar", "Open in the editor after capture"), isOn: Binding(
                get: { m.openEditor },
                set: { on in
                    app.settings.afterCapture = on ? "quick" : "none"
                    app.settings.save()
                    m.openEditor = on
                }))
                .toggleStyle(.switch)
                .controlSize(.small)

            Divider()

            HStack {
                Text(S.t("Recentes", "Recent")).font(.system(size: 13, weight: .bold))
                Spacer()
                Button(S.t("Ver histórico", "View history")) { app.showHistory() }.buttonStyle(.link)
            }

            if m.recents.isEmpty {
                Text(S.t("Ainda não há capturas.", "No captures yet."))
                    .foregroundStyle(.secondary).font(.callout)
                    .frame(maxWidth: .infinity, minHeight: 80)
            } else {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
                    ForEach(m.recents) { it in
                        Button { app.openEditor(it.url, image: nil, compact: !it.isVideo) } label: {
                            VStack(alignment: .leading, spacing: 3) {
                                ThumbView(url: it.url, size: 220)
                                    .frame(height: 72)
                                    .clipShape(RoundedRectangle(cornerRadius: 6))
                                    .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color.primary.opacity(0.1)))
                                Text(RelTime.short(it.date)).font(.system(size: 11)).foregroundStyle(.secondary)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .help(it.name)
                        .onDrag { NSItemProvider(contentsOf: it.url) ?? NSItemProvider() }
                    }
                }
            }

            Text(m.copied
                 ? S.t("A última captura está na área de transferência — cole com ⌘V", "Your last capture is on the clipboard — paste with ⌘V")
                 : S.t("Dica: \(app.settings.hotkeyRegion.display) captura · \(app.settings.hotkeyRecord.display) grava vídeo",
                       "Tip: \(app.settings.hotkeyRegion.display) captures · \(app.settings.hotkeyRecord.display) records video"))
                .font(.system(size: 11)).foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            Divider()

            HStack(spacing: 14) {
                Button(S.t("Abrir imagem…", "Open image…")) { app.openImageDialog() }
                Button(S.t("Pasta", "Folder")) { app.openFolder() }
                Spacer()
                Button(S.t("Sair", "Quit")) { NSApp.terminate(nil) }
            }
            .buttonStyle(.link)
            .font(.system(size: 12))
        }
        .padding(16)
        .frame(width: 380)
        .tint(Theme.accentColor)
        .id(theme.tick)
        .onAppear { m.refresh() }
    }

    private func tile(_ symbol: String, _ title: String, _ help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            VStack(spacing: 5) {
                Image(systemName: symbol).font(.system(size: 18))
                Text(title).font(.system(size: 11.5)).lineLimit(1)
            }
            .frame(maxWidth: .infinity, minHeight: 58)
            .background(RoundedRectangle(cornerRadius: 9).fill(Color.primary.opacity(0.07)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }
}
