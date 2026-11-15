import AppKit
import Carbon.HIToolbox
import ServiceManagement

/// Atalho global: código de tecla virtual + modificadores Carbon.
struct Hotkey: Codable, Equatable {
    var keyCode: UInt32
    var modifiers: UInt32   // cmdKey | shiftKey | optionKey | controlKey

    static let none = Hotkey(keyCode: UInt32.max, modifiers: 0)
    var isNone: Bool { keyCode == UInt32.max }

    static func ctrlShift(_ key: Int) -> Hotkey { Hotkey(keyCode: UInt32(key), modifiers: UInt32(controlKey | shiftKey)) }

    var display: String {
        if isNone { return S.t("(nenhum)", "(none)") }
        var s = ""
        if modifiers & UInt32(controlKey) != 0 { s += "⌃" }
        if modifiers & UInt32(optionKey) != 0 { s += "⌥" }
        if modifiers & UInt32(shiftKey) != 0 { s += "⇧" }
        if modifiers & UInt32(cmdKey) != 0 { s += "⌘" }
        return s + KeyNames.name(Int(keyCode))
    }

    /// Converte modificadores do AppKit para Carbon.
    static func carbon(_ f: NSEvent.ModifierFlags) -> UInt32 {
        var m: UInt32 = 0
        if f.contains(.command) { m |= UInt32(cmdKey) }
        if f.contains(.shift) { m |= UInt32(shiftKey) }
        if f.contains(.option) { m |= UInt32(optionKey) }
        if f.contains(.control) { m |= UInt32(controlKey) }
        return m
    }
}

enum KeyNames {
    static let table: [Int: String] = [
        kVK_ANSI_A: "A", kVK_ANSI_B: "B", kVK_ANSI_C: "C", kVK_ANSI_D: "D", kVK_ANSI_E: "E", kVK_ANSI_F: "F",
        kVK_ANSI_G: "G", kVK_ANSI_H: "H", kVK_ANSI_I: "I", kVK_ANSI_J: "J", kVK_ANSI_K: "K", kVK_ANSI_L: "L",
        kVK_ANSI_M: "M", kVK_ANSI_N: "N", kVK_ANSI_O: "O", kVK_ANSI_P: "P", kVK_ANSI_Q: "Q", kVK_ANSI_R: "R",
        kVK_ANSI_S: "S", kVK_ANSI_T: "T", kVK_ANSI_U: "U", kVK_ANSI_V: "V", kVK_ANSI_W: "W", kVK_ANSI_X: "X",
        kVK_ANSI_Y: "Y", kVK_ANSI_Z: "Z",
        kVK_ANSI_0: "0", kVK_ANSI_1: "1", kVK_ANSI_2: "2", kVK_ANSI_3: "3", kVK_ANSI_4: "4",
        kVK_ANSI_5: "5", kVK_ANSI_6: "6", kVK_ANSI_7: "7", kVK_ANSI_8: "8", kVK_ANSI_9: "9",
        kVK_F1: "F1", kVK_F2: "F2", kVK_F3: "F3", kVK_F4: "F4", kVK_F5: "F5", kVK_F6: "F6",
        kVK_F7: "F7", kVK_F8: "F8", kVK_F9: "F9", kVK_F10: "F10", kVK_F11: "F11", kVK_F12: "F12",
        kVK_F13: "F13", kVK_F14: "F14", kVK_F15: "F15", kVK_F16: "F16", kVK_F17: "F17", kVK_F18: "F18", kVK_F19: "F19",
        kVK_Space: "Space", kVK_Return: "↩", kVK_Tab: "⇥", kVK_Escape: "⎋", kVK_Delete: "⌫",
        kVK_LeftArrow: "←", kVK_RightArrow: "→", kVK_UpArrow: "↑", kVK_DownArrow: "↓",
        kVK_ANSI_Minus: "-", kVK_ANSI_Equal: "=", kVK_ANSI_LeftBracket: "[", kVK_ANSI_RightBracket: "]",
        kVK_ANSI_Semicolon: ";", kVK_ANSI_Quote: "'", kVK_ANSI_Comma: ",", kVK_ANSI_Period: ".",
        kVK_ANSI_Slash: "/", kVK_ANSI_Backslash: "\\", kVK_ANSI_Grave: "`",
    ]
    static func name(_ code: Int) -> String { table[code] ?? "#\(code)" }
}

final class Settings: Codable {
    var outputFolder: String = Settings.defaultFolder
    var format: String = "png"                // png | jpg
    var copyToClipboard = true
    var showNotification = true
    var afterCapture: String = "quick"        // quick | full | none
    var themeMode: String = "system"          // system | light | dark
    var accent: String = "indigo"             // indigo | blue | teal | orange | system
    var hotkeyRegion = Hotkey.ctrlShift(kVK_ANSI_4)
    var hotkeyWindow = Hotkey.ctrlShift(kVK_ANSI_6)
    var hotkeyFull = Hotkey.ctrlShift(kVK_ANSI_3)
    var hotkeyRecord = Hotkey.ctrlShift(kVK_ANSI_5)
    var videoFps = 30
    var videoCursor = true
    var editorColor: UInt32 = 0               // última cor no editor (ARGB; 0 = omissão)
    var editorWidth: Int = 0
    var editorFont: Int = 0
    var panelWidth: Int = 0                   // tamanho do painel da barra de menus (pontos; 0 = omissão)
    var panelHeight: Int = 0
    var autoUpdateCheck = true                // procurar versões novas no GitHub (uma vez por dia)
    var lastUpdateCheck: Double = 0           // segundos desde 1970
    var skippedVersion: String = ""           // "Ignorar esta versão"

    var isNew = false

    enum CodingKeys: String, CodingKey {
        case outputFolder, format, copyToClipboard, showNotification, afterCapture, themeMode, accent
        case hotkeyRegion, hotkeyWindow, hotkeyFull, hotkeyRecord, videoFps, videoCursor
        case editorColor, editorWidth, editorFont, panelWidth, panelHeight
        case autoUpdateCheck, lastUpdateCheck, skippedVersion
    }

    init() {}

    /// Descodificação tolerante: campos em falta ficam com o valor por omissão.
    required init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func v<T: Decodable>(_ k: CodingKeys, _ d: T) -> T { ((try? c.decodeIfPresent(T.self, forKey: k)) ?? nil) ?? d }
        outputFolder = v(.outputFolder, outputFolder)
        format = v(.format, format)
        copyToClipboard = v(.copyToClipboard, copyToClipboard)
        showNotification = v(.showNotification, showNotification)
        afterCapture = v(.afterCapture, afterCapture)
        themeMode = v(.themeMode, themeMode)
        accent = v(.accent, accent)
        hotkeyRegion = v(.hotkeyRegion, hotkeyRegion)
        hotkeyWindow = v(.hotkeyWindow, hotkeyWindow)
        hotkeyFull = v(.hotkeyFull, hotkeyFull)
        hotkeyRecord = v(.hotkeyRecord, hotkeyRecord)
        videoFps = v(.videoFps, videoFps)
        videoCursor = v(.videoCursor, videoCursor)
        editorColor = v(.editorColor, editorColor)
        editorWidth = v(.editorWidth, editorWidth)
        editorFont = v(.editorFont, editorFont)
        panelWidth = v(.panelWidth, panelWidth)
        panelHeight = v(.panelHeight, panelHeight)
        autoUpdateCheck = v(.autoUpdateCheck, autoUpdateCheck)
        lastUpdateCheck = v(.lastUpdateCheck, lastUpdateCheck)
        skippedVersion = v(.skippedVersion, skippedVersion)
    }

    static var defaultFolder: String {
        let pics = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Pictures")
        return pics.appendingPathComponent("SnapStudio").path
    }

    static var appDir: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("SnapStudio")
    }

    static var filePath: URL { appDir.appendingPathComponent("settings.json") }

    static func load() -> Settings {
        if let data = try? Data(contentsOf: filePath), let s = try? JSONDecoder().decode(Settings.self, from: data) {
            if s.outputFolder.isEmpty { s.outputFolder = defaultFolder }
            if s.format != "jpg" { s.format = "png" }
            if !["quick", "full", "none"].contains(s.afterCapture) { s.afterCapture = "quick" }
            if !["light", "dark"].contains(s.themeMode) { s.themeMode = "system" }
            if s.accent.isEmpty { s.accent = "indigo" }
            if ![15, 30, 60].contains(s.videoFps) { s.videoFps = 30 }
            return s
        }
        let n = Settings()
        n.isNew = true
        return n
    }

    func save() {
        try? FileManager.default.createDirectory(at: Settings.appDir, withIntermediateDirectories: true)
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let d = try? enc.encode(self) { try? d.write(to: Settings.filePath, options: .atomic) }
    }

    var outputURL: URL { URL(fileURLWithPath: (outputFolder as NSString).expandingTildeInPath) }

    func newCapturePath() -> URL { newPath(format == "jpg" ? "jpg" : "png") }
    func newVideoPath() -> URL { newPath("mp4") }

    private func newPath(_ ext: String) -> URL {
        let dir = outputURL
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        let b = f.string(from: Date())
        var p = dir.appendingPathComponent("\(b).\(ext)")
        var i = 2
        while FileManager.default.fileExists(atPath: p.path) {
            p = dir.appendingPathComponent("\(b)_\(i).\(ext)")
            i += 1
        }
        return p
    }

    // ---------- iniciar com o macOS ----------

    static var startAtLogin: Bool {
        get { SMAppService.mainApp.status == .enabled }
        set {
            do {
                if newValue { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            } catch {
                NSLog("SnapStudio: login item: \(error)")
            }
        }
    }
}

/// Ficheiros laterais (imagem original + anotações) para reabrir uma captura e continuar a editar.
enum Sidecar {
    static var dir: URL { Settings.appDir.appendingPathComponent("edit") }

    static func key(_ url: URL) -> String {
        let full = url.standardizedFileURL.path.lowercased()
        var h: UInt32 = 2166136261
        for u in full.unicodeScalars { h ^= u.value; h = h &* 16777619 }
        return url.lastPathComponent + "_" + String(format: "%08x", h)
    }

    static func base(_ url: URL) -> URL { dir.appendingPathComponent(key(url) + ".base.png") }
    static func json(_ url: URL) -> URL { dir.appendingPathComponent(key(url) + ".json") }
    static func ensureDir() { try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true) }
    static func hasAnnotations(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: json(url).path) }

    static func delete(_ url: URL) {
        try? FileManager.default.removeItem(at: base(url))
        try? FileManager.default.removeItem(at: json(url))
    }

    static func move(from: URL, to: URL) {
        let fm = FileManager.default
        for (a, b) in [(base(from), base(to)), (json(from), json(to))] where fm.fileExists(atPath: a.path) {
            try? fm.removeItem(at: b)
            try? fm.moveItem(at: a, to: b)
        }
    }
}
