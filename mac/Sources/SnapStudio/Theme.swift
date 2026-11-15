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

/// Paleta igual à da versão Windows (Theme.cs), para o painel ficar idêntico.
enum Pal {
    private static func c(_ light: Int, _ dark: Int) -> Color { Color(nsColor: Theme.hex(Theme.isDark ? dark : light)) }
    static var panel: Color { c(0xFFFFFF, 0x202229) }
    static var surface: Color { c(0xFFFFFF, 0x1D1F25) }
    static var surface2: Color { c(0xF6F7F9, 0x272A33) }
    static var border: Color { c(0xE1E4EA, 0x2E3139) }
    static var borderStrong: Color { c(0xD3D7DF, 0x3A3E49) }
    static var text: Color { c(0x1B1D24, 0xECEDF1) }
    static var muted: Color { c(0x545A69, 0xA1A6B4) }
    static var hover: Color { c(0xEEF0F3, 0x2C2F38) }
    static var pressed: Color { c(0xE3E6EB, 0x353945) }
    static var footer: Color { Theme.isDark ? surface : surface2 }

    static func blend(_ a: NSColor, _ b: NSColor, _ t: CGFloat) -> NSColor {
        let x = a.usingColorSpace(.sRGB) ?? a, y = b.usingColorSpace(.sRGB) ?? b
        return NSColor(srgbRed: x.redComponent + (y.redComponent - x.redComponent) * t,
                       green: x.greenComponent + (y.greenComponent - x.greenComponent) * t,
                       blue: x.blueComponent + (y.blueComponent - x.blueComponent) * t, alpha: 1)
    }
    static var accentHover: Color { Color(nsColor: blend(Theme.accent, Theme.isDark ? .white : .black, 0.12)) }
    static var accentSoft: Color {
        Color(nsColor: Theme.isDark ? blend(Theme.hex(0x202229), Theme.accent, 0.30) : blend(.white, Theme.accent, 0.10))
    }
    static var accentSoftText: Color {
        Color(nsColor: Theme.isDark ? blend(Theme.accent, .white, 0.6) : blend(Theme.accent, .black, 0.35))
    }
}

extension Notification.Name {
    static let snapThemeChanged = Notification.Name("SnapStudio.themeChanged")
    static let snapCapturesChanged = Notification.Name("SnapStudio.capturesChanged")
    static let snapRecordingChanged = Notification.Name("SnapStudio.recordingChanged")
    static let snapUpdateChanged = Notification.Name("SnapStudio.updateChanged")
    static let snapShowSettingsTab = Notification.Name("SnapStudio.showSettingsTab")
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
