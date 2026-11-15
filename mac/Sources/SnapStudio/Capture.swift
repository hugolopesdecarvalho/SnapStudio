import AppKit
import ScreenCaptureKit

enum CaptureMode { case region, activeWindow, fullScreen }

/// Imagem congelada de um ecrã.
struct ScreenShot {
    let screen: NSScreen
    let image: CGImage
    var frame: CGRect { screen.frame }          // coordenadas globais Cocoa (origem em baixo à esquerda)
    var scale: CGFloat { CGFloat(image.width) / screen.frame.width }

    /// Recorta um retângulo em pontos locais do ecrã (origem em baixo à esquerda).
    func crop(_ local: CGRect) -> CGImage? {
        let s = scale
        let px = CGRect(x: local.minX * s, y: (frame.height - local.maxY) * s, width: local.width * s, height: local.height * s)
        return ImageUtil.crop(image, px)
    }
}

/// Janela visível (para o modo captura destacar a janela debaixo do rato).
struct WindowInfo {
    let id: CGWindowID
    let pid: pid_t
    let frame: CGRect   // coordenadas globais Cocoa
}

enum CaptureError: LocalizedError {
    case noPermission, noDisplay
    var errorDescription: String? {
        switch self {
        case .noPermission:
            return S.t("A SnapStudio precisa de autorização para gravar o ecrã.", "SnapStudio needs permission to record the screen.")
        case .noDisplay:
            return S.t("Nenhum ecrã encontrado.", "No display found.")
        }
    }
}

enum Capture {
    static func displayID(_ s: NSScreen) -> CGDirectDisplayID {
        (s.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? CGMainDisplayID()
    }

    static var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    /// Verificação fiável: pede a lista de ecrãs ao ScreenCaptureKit (só funciona com autorização).
    static func verifyPermission() async -> Bool {
        if CGPreflightScreenCaptureAccess() { return true }
        do { _ = try await shareable(); return true } catch { return false }
    }

    /// Erro de autorização do ScreenCaptureKit (o utilizador não deu acesso à Gravação do ecrã).
    static func isPermissionError(_ error: Error) -> Bool {
        let e = error as NSError
        if e.domain == SCStreamErrorDomain && e.code == SCStreamError.Code.userDeclined.rawValue { return true }
        return !CGPreflightScreenCaptureAccess() && e.domain == SCStreamErrorDomain
    }

    /// Explica como dar a autorização de Gravação do ecrã. Só é chamado quando o ScreenCaptureKit a recusa
    /// (o CGPreflightScreenCaptureAccess pode dizer "não" mesmo com a app autorizada, por isso não serve de critério).
    static func showPermissionAlert() {
        let a = NSAlert()
        a.messageText = S.t("Autorizar a gravação do ecrã", "Allow screen recording")
        a.informativeText = S.t(
            "Para capturar o ecrã, ative a SnapStudio em Definições do Sistema › Privacidade e segurança › Gravação do ecrã e áudio do sistema. Depois feche e volte a abrir a SnapStudio.\n\nSe a SnapStudio já aparece ativada, é de uma versão anterior: selecione-a, remova-a com “−” e volte a abrir a SnapStudio para a autorizar de novo.",
            "To capture the screen, turn on SnapStudio in System Settings › Privacy & Security › Screen & System Audio Recording. Then quit and reopen SnapStudio.\n\nIf SnapStudio is already turned on, that entry belongs to an earlier version: select it, remove it with “−” and reopen SnapStudio to allow it again.")
        a.addButton(withTitle: S.t("Abrir Definições", "Open Settings"))
        a.addButton(withTitle: S.t("Cancelar", "Cancel"))
        NSApp.activate(ignoringOtherApps: true)
        if a.runModal() == .alertFirstButtonReturn,
           let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    static func shareable() async throws -> SCShareableContent {
        try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
    }

    /// Janelas temporárias da própria SnapStudio que nunca devem aparecer numa captura ou vídeo:
    /// painel, ecrã de seleção, moldura/barra/contagem da gravação (todas acima do nível normal).
    /// As janelas normais da app (editor, histórico, definições) aparecem, como as de qualquer outra app.
    static func ownTransientWindows(_ content: SCShareableContent) -> [SCWindow] {
        content.windows.filter { $0.owningApplication?.processID == getpid() && $0.windowLayer != 0 }
    }

    /// Fotografa todos os ecrãs (sem as janelas da própria SnapStudio).
    static func snapshotScreens() async throws -> [ScreenShot] {
        let content = try await shareable()
        let hidden = ownTransientWindows(content)
        var out: [ScreenShot] = []
        for screen in NSScreen.screens {
            let did = displayID(screen)
            guard let d = content.displays.first(where: { $0.displayID == did }) else { continue }
            let filter = SCContentFilter(display: d, excludingWindows: hidden)
            let cfg = SCStreamConfiguration()
            let scale = screen.backingScaleFactor
            cfg.width = Int(screen.frame.width * scale)
            cfg.height = Int(screen.frame.height * scale)
            cfg.showsCursor = false
            cfg.captureResolution = .best
            cfg.colorSpaceName = CGColorSpace.sRGB
            let img = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg)
            out.append(ScreenShot(screen: screen, image: img))
        }
        if out.isEmpty { throw CaptureError.noDisplay }
        return out
    }

    /// Junta vários ecrãs numa só imagem.
    static func compose(_ shots: [ScreenShot]) -> CGImage? {
        guard let first = shots.first else { return nil }
        if shots.count == 1 { return first.image }
        let union = shots.reduce(CGRect.null) { $0.union($1.frame) }
        let scale = shots.map { $0.scale }.max() ?? 2
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        guard let g = CGContext(data: nil, width: Int(union.width * scale), height: Int(union.height * scale), bitsPerComponent: 8,
                                bytesPerRow: 0, space: cs, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        g.interpolationQuality = .high
        for s in shots {
            let r = CGRect(x: (s.frame.minX - union.minX) * scale, y: (s.frame.minY - union.minY) * scale,
                           width: s.frame.width * scale, height: s.frame.height * scale)
            g.draw(s.image, in: r)
        }
        return g.makeImage()
    }

    /// Janelas normais visíveis (nível 0, incluindo as da própria SnapStudio), da frente para trás.
    static func visibleWindows() -> [WindowInfo] {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { return [] }
        let h0 = NSScreen.screens.first?.frame.height ?? 0
        var out: [WindowInfo] = []
        for w in list {
            guard (w[kCGWindowLayer as String] as? Int) == 0,
                  let pid = w[kCGWindowOwnerPID as String] as? pid_t,
                  let num = w[kCGWindowNumber as String] as? CGWindowID,
                  let bd = w[kCGWindowBounds as String] as? NSDictionary,
                  let r = CGRect(dictionaryRepresentation: bd) else { continue }
            if let a = w[kCGWindowAlpha as String] as? Double, a <= 0.01 { continue }
            if r.width < 40 || r.height < 40 { continue }
            out.append(WindowInfo(id: num, pid: pid, frame: CGRect(x: r.minX, y: h0 - r.maxY, width: r.width, height: r.height)))
        }
        return out
    }

    /// Janela da frente da app ativa (a que estava em primeiro plano antes da SnapStudio).
    static func captureActiveWindow(pid: pid_t?) async throws -> CGImage? {
        let content = try await shareable()
        let wins = visibleWindows()
        guard let target = wins.first(where: { pid == nil || $0.pid == pid }) ?? wins.first,
              let scw = content.windows.first(where: { $0.windowID == target.id }) else {
            return compose(try await snapshotScreens())
        }
        let filter = SCContentFilter(desktopIndependentWindow: scw)
        let cfg = SCStreamConfiguration()
        let screen = NSScreen.screens.first(where: { $0.frame.intersects(target.frame) }) ?? NSScreen.main
        let scale = screen?.backingScaleFactor ?? 2
        cfg.width = Int(scw.frame.width * scale)
        cfg.height = Int(scw.frame.height * scale)
        cfg.showsCursor = false
        cfg.ignoreShadowsSingleWindow = true
        cfg.captureResolution = .best
        cfg.colorSpaceName = CGColorSpace.sRGB
        return try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg)
    }
}
