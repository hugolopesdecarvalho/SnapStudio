import AppKit
import ImageIO
import UniformTypeIdentifiers

enum ImageUtil {
    static let imageExts: Set<String> = ["png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "heic"]
    static let videoExts: Set<String> = ["mp4", "mov", "m4v"]

    static func isVideo(_ url: URL) -> Bool { videoExts.contains(url.pathExtension.lowercased()) }
    static func isMedia(_ url: URL) -> Bool {
        let e = url.pathExtension.lowercased()
        return imageExts.contains(e) || videoExts.contains(e)
    }

    static func load(_ url: URL) -> CGImage? {
        guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        return CGImageSourceCreateImageAtIndex(src, 0, [kCGImageSourceShouldCache: true] as CFDictionary)
    }

    /// Guarda em PNG/JPG/BMP/TIFF consoante a extensão.
    static func save(_ img: CGImage, to url: URL) throws {
        let ext = url.pathExtension.lowercased()
        let type: UTType = (ext == "jpg" || ext == "jpeg") ? .jpeg : ext == "bmp" ? .bmp : (ext == "tif" || ext == "tiff") ? .tiff : .png
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let tmp = url.deletingLastPathComponent().appendingPathComponent(".\(UUID().uuidString).\(ext)")
        guard let dst = CGImageDestinationCreateWithURL(tmp as CFURL, type.identifier as CFString, 1, nil) else {
            throw NSError(domain: "SnapStudio", code: 1, userInfo: [NSLocalizedDescriptionKey: S.t("Formato não suportado", "Unsupported format")])
        }
        var props: [CFString: Any] = [:]
        if type == .jpeg { props[kCGImageDestinationLossyCompressionQuality] = 0.92 }
        // capturas Retina: 144 dpi para aparecerem no tamanho certo noutras apps
        CGImageDestinationAddImage(dst, type == .jpeg ? flattened(img) : img, props as CFDictionary)
        guard CGImageDestinationFinalize(dst) else {
            throw NSError(domain: "SnapStudio", code: 2, userInfo: [NSLocalizedDescriptionKey: S.t("Falha ao escrever o ficheiro", "Failed to write the file")])
        }
        if FileManager.default.fileExists(atPath: url.path) {
            _ = try FileManager.default.replaceItemAt(url, withItemAt: tmp)
        } else {
            try FileManager.default.moveItem(at: tmp, to: url)
        }
    }

    /// JPEG não tem transparência: fundo branco.
    static func flattened(_ img: CGImage) -> CGImage {
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        guard let g = CGContext(data: nil, width: img.width, height: img.height, bitsPerComponent: 8, bytesPerRow: 0, space: cs,
                                bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return img }
        g.setFillColor(.white)
        g.fill(CGRect(x: 0, y: 0, width: img.width, height: img.height))
        g.draw(img, in: CGRect(x: 0, y: 0, width: img.width, height: img.height))
        return g.makeImage() ?? img
    }

    /// Recorte em coordenadas de pixels (origem no canto superior esquerdo).
    static func crop(_ img: CGImage, _ r: CGRect) -> CGImage? {
        let rr = r.integral.intersection(CGRect(x: 0, y: 0, width: img.width, height: img.height))
        if rr.isNull || rr.width < 1 || rr.height < 1 { return nil }
        return img.cropping(to: rr)
    }

    static func pngData(_ img: CGImage) -> Data? {
        let rep = NSBitmapImageRep(cgImage: img)
        return rep.representation(using: .png, properties: [:])
    }

    /// Copia a imagem para a área de transferência (PNG + TIFF, para colar em qualquer app).
    static func copyImage(_ img: CGImage) {
        let pb = NSPasteboard.general
        pb.clearContents()
        let rep = NSBitmapImageRep(cgImage: img)
        let item = NSPasteboardItem()
        if let png = rep.representation(using: .png, properties: [:]) { item.setData(png, forType: .png) }
        if let tiff = rep.tiffRepresentation { item.setData(tiff, forType: .tiff) }
        pb.writeObjects([item])
    }

    /// Copia um ficheiro (cola-se no Finder, Mail, Teams, Slack…).
    static func copyFile(_ url: URL) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects([url as NSURL])
    }

    /// Tamanho em pontos para mostrar uma imagem capturada em ecrã Retina no tamanho real.
    static func pointSize(_ img: CGImage, scale: CGFloat) -> NSSize {
        NSSize(width: CGFloat(img.width) / scale, height: CGFloat(img.height) / scale)
    }
}
