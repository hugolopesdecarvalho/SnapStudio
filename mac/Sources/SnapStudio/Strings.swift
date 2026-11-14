import Foundation

/// Textos da interface: português se o macOS estiver em PT, senão inglês.
enum S {
    static let pt: Bool = (Locale.preferredLanguages.first ?? "").lowercased().hasPrefix("pt")
    static func t(_ ptText: String, _ enText: String) -> String { pt ? ptText : enText }
}
