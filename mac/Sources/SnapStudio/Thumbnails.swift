import AppKit
import QuickLookThumbnailing
import SwiftUI

/// Captura na pasta (imagem ou vídeo), mostrada na grelha de recentes do painel.
struct CaptureItem: Identifiable, Hashable {
    let url: URL
    let date: Date
    let bytes: Int64
    let annotated: Bool
    var id: URL { url }
    var isVideo: Bool { ImageUtil.isVideo(url) }
    var name: String { url.lastPathComponent }

    static func scan(_ folder: URL) -> [CaptureItem] {
        let fm = FileManager.default
        let keys: [URLResourceKey] = [.creationDateKey, .contentModificationDateKey, .fileSizeKey, .isRegularFileKey]
        guard let list = try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: keys, options: [.skipsHiddenFiles]) else { return [] }
        return list.compactMap { u -> CaptureItem? in
            guard ImageUtil.isMedia(u), let v = try? u.resourceValues(forKeys: Set(keys)), v.isRegularFile == true else { return nil }
            let d = v.creationDate ?? v.contentModificationDate ?? Date.distantPast
            return CaptureItem(url: u, date: d, bytes: Int64(v.fileSize ?? 0), annotated: Sidecar.hasAnnotations(u))
        }
        .sorted { $0.date > $1.date }
    }
}

/// Miniaturas (Quick Look: imagens e vídeos) com cache em memória.
final class ThumbnailCache {
    static let shared = ThumbnailCache()
    private let cache = NSCache<NSString, NSImage>()

    func key(_ url: URL, _ size: CGFloat) -> NSString {
        let mod = (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate?.timeIntervalSince1970) ?? 0
        return "\(url.path)|\(Int(size))|\(mod)" as NSString
    }

    func cached(_ url: URL, size: CGFloat) -> NSImage? { cache.object(forKey: key(url, size)) }

    func load(_ url: URL, size: CGFloat, completion: @escaping (NSImage?) -> Void) {
        let k = key(url, size)
        if let i = cache.object(forKey: k) { completion(i); return }
        let scale = NSScreen.main?.backingScaleFactor ?? 2
        let req = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: size, height: size), scale: scale, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: req) { rep, _ in
            let img = rep?.nsImage
            if let i = img { self.cache.setObject(i, forKey: k) }
            DispatchQueue.main.async { completion(img) }
        }
    }
}

/// Pequeno estado observável (evita @State, que no SDK do macOS 27 é uma macro só disponível com o Xcode).
final class Box<T>: ObservableObject {
    @Published var value: T
    init(_ v: T) { value = v }
}

struct ThumbView: View {
    let url: URL
    let size: CGFloat
    var contentMode: ContentMode = .fill
    @StateObject private var img = Box<NSImage?>(nil)

    var body: some View {
        ZStack {
            Rectangle().fill(Color.primary.opacity(0.06))
            if let i = img.value {
                Image(nsImage: i).resizable().aspectRatio(contentMode: contentMode)
            } else {
                Image(systemName: ImageUtil.isVideo(url) ? "film" : "photo").foregroundStyle(.secondary)
            }
            if ImageUtil.isVideo(url) {
                Image(systemName: "play.circle.fill")
                    .font(.system(size: min(36, size / 4)))
                    .foregroundStyle(.white, .black.opacity(0.45))
                    .shadow(radius: 2)
            }
        }
        .clipped()
        .onAppear(perform: load)
        .onChange(of: url) { _, _ in load() }
    }

    private func load() {
        let box = img
        box.value = ThumbnailCache.shared.cached(url, size: size)
        if box.value == nil { ThumbnailCache.shared.load(url, size: size) { box.value = $0 } }
    }
}

enum RelTime {
    static func short(_ d: Date) -> String {
        let s = -d.timeIntervalSinceNow
        if s < 60 { return S.t("Agora", "Just now") }
        if s < 3600 { return S.pt ? "Há \(Int(s / 60)) min" : "\(Int(s / 60)) min ago" }
        let f = DateFormatter(); f.dateFormat = "HH:mm"
        if Calendar.current.isDateInToday(d) { return f.string(from: d) }
        if Calendar.current.isDateInYesterday(d) { return S.t("Ontem ", "Yesterday ") + f.string(from: d) }
        let g = DateFormatter(); g.dateFormat = "d MMM"; g.locale = Locale(identifier: S.pt ? "pt_PT" : "en_US")
        return g.string(from: d)
    }
}
