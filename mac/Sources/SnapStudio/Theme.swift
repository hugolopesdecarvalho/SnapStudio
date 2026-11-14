import AppKit
import SwiftUI

/// Tema (claro/escuro/sistema) e cor de destaque.
enum Theme {
    static func apply(_ s: Settings) {
        switch s.themeMode {
        case "light": NSApp.appearance = NSAppearance(named: .aqua)
        case "dark": NSApp.appearance = NSAppearance(named: .darkAqua)
        default: NSApp.appearance = nil
        }
        NotificationCenter.default.post(name: .snapThemeChanged, object: nil)
    }

    static var isDark: Bool {
        NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
    }

    static let accents: [(id: String, pt: String, en: String)] = [
        ("indigo", "Índigo", "Indigo"), ("blue", "Azul", "Blue"), ("teal", "Verde-azulado", "Teal"),
        ("orange", "Laranja", "Orange"), ("system", "Igual ao macOS", "Same as macOS"),
    ]

    static func accentNS(_ name: String) -> NSColor {
        let dark = isDark
        switch name {
        case "blue": return hex(dark ? 0x2B88D8 : 0x0F6CBD)
        case "teal": return hex(dark ? 0x14917F : 0x0E7A6B)
        case "orange": return hex(dark ? 0xD9541A : 0xC2410C)
        case "system": return .controlAccentColor
        default: return hex(dark ? 0x5B5FEF : 0x4F46E5)
        }
    }

    static var accent: NSColor { accentNS(AppController.shared.settings.accent) }
    static var accentColor: Color { Color(nsColor: accent) }

    static func hex(_ v: Int) -> NSColor {
        NSColor(srgbRed: CGFloat((v >> 16) & 0xFF) / 255, green: CGFloat((v >> 8) & 0xFF) / 255, blue: CGFloat(v & 0xFF) / 255, alpha: 1)
    }
}

extension Notification.Name {
    static let snapThemeChanged = Notification.Name("SnapStudio.themeChanged")
    static let snapCapturesChanged = Notification.Name("SnapStudio.capturesChanged")
    static let snapRecordingChanged = Notification.Name("SnapStudio.recordingChanged")
}

/// Observável para as vistas SwiftUI reagirem a mudanças de tema/destaque.
final class ThemeObserver: ObservableObject {
    @Published var tick = 0
    private var token: NSObjectProtocol?
    init() {
        token = NotificationCenter.default.addObserver(forName: .snapThemeChanged, object: nil, queue: .main) { [weak self] _ in
            self?.tick += 1
        }
    }
    deinit { if let t = token { NotificationCenter.default.removeObserver(t) } }
}
