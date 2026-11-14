import AppKit

/// Ícone da app desenhado em código (gera o .icns no build.sh).
enum AppIcon {
    /// `windows: true` reproduz o Icons.cs da versão Windows (margem menor nos tamanhos pequenos,
    /// traço mínimo de 1,5 px, sombra só a partir de 48 px) — usado para gerar o assets/app.ico.
    static func draw(in rect: CGRect, _ g: CGContext, windows: Bool = false) {
        let s = rect.width
        // fundo: quadrado arredondado com gradiente índigo → violeta
        let inset = s * ((windows && s <= 32) ? 0.04 : 0.09)
        let body = rect.insetBy(dx: inset, dy: inset)
        let radius = body.width * 0.225
        let path = CGPath(roundedRect: body, cornerWidth: radius, cornerHeight: radius, transform: nil)
        g.saveGState()
        if !windows || s >= 48 {
            g.setShadow(offset: CGSize(width: 0, height: -s * 0.012), blur: s * 0.03, color: NSColor.black.withAlphaComponent(0.3).cgColor)
        }
        g.addPath(path)
        g.setFillColor(NSColor(srgbRed: 0.31, green: 0.27, blue: 0.9, alpha: 1).cgColor)
        g.fillPath()
        g.restoreGState()
        g.saveGState()
        g.addPath(path)
        g.clip()
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        let grad = CGGradient(colorsSpace: cs, colors: [
            NSColor(srgbRed: 0.42, green: 0.36, blue: 0.98, alpha: 1).cgColor,
            NSColor(srgbRed: 0.26, green: 0.22, blue: 0.82, alpha: 1).cgColor,
        ] as CFArray, locations: [0, 1])!
        g.drawLinearGradient(grad, start: CGPoint(x: body.minX, y: body.maxY), end: CGPoint(x: body.maxX, y: body.minY), options: [])
        g.restoreGState()

        // cantos do visor
        let c = body.insetBy(dx: body.width * 0.2, dy: body.width * 0.2)
        let arm = c.width * 0.28
        g.setStrokeColor(NSColor.white.cgColor)
        g.setLineWidth(windows ? max(1.5, s * 0.055) : s * 0.055)
        g.setLineCap(.round)
        g.setLineJoin(.round)
        for (p, dx, dy) in [(CGPoint(x: c.minX, y: c.maxY), 1.0, -1.0), (CGPoint(x: c.maxX, y: c.maxY), -1.0, -1.0),
                            (CGPoint(x: c.minX, y: c.minY), 1.0, 1.0), (CGPoint(x: c.maxX, y: c.minY), -1.0, 1.0)] {
            g.move(to: CGPoint(x: p.x, y: p.y + dy * arm))
            g.addLine(to: p)
            g.addLine(to: CGPoint(x: p.x + dx * arm, y: p.y))
        }
        g.strokePath()

        // obturador ao centro
        let r = c.width * 0.2
        g.setFillColor(NSColor.white.cgColor)
        g.fillEllipse(in: CGRect(x: c.midX - r, y: c.midY - r, width: 2 * r, height: 2 * r))
        g.setFillColor(NSColor(srgbRed: 0.98, green: 0.32, blue: 0.36, alpha: 1).cgColor)
        let r2 = r * 0.45
        g.fillEllipse(in: CGRect(x: c.midX - r2, y: c.midY - r2, width: 2 * r2, height: 2 * r2))
    }

    static func image(size: CGFloat) -> NSImage {
        NSImage(size: CGSize(width: size, height: size), flipped: false) { r in
            if let g = NSGraphicsContext.current?.cgContext { draw(in: r, g) }
            return true
        }
    }

    static func png(pixels: Int) -> Data? {
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        guard let g = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0, space: cs,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        draw(in: CGRect(x: 0, y: 0, width: pixels, height: pixels), g)
        guard let img = g.makeImage() else { return nil }
        return ImageUtil.pngData(img)
    }

    /// .ico multi-tamanho com entradas PNG (mesmo formato do AppIcon.SaveIco da versão Windows).
    static func writeIco(to url: URL) {
        let sizes = [16, 20, 24, 32, 40, 48, 64, 256]
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        let pngs: [Data] = sizes.map { n in
            let g = CGContext(data: nil, width: n, height: n, bitsPerComponent: 8, bytesPerRow: 0, space: cs,
                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
            draw(in: CGRect(x: 0, y: 0, width: n, height: n), g, windows: true)
            return ImageUtil.pngData(g.makeImage()!)!
        }
        var d = Data()
        func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
        func u32(_ v: Int) { var x = UInt32(v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
        u16(0); u16(1); u16(sizes.count)
        var offset = 6 + 16 * sizes.count
        for (i, n) in sizes.enumerated() {
            d.append(UInt8(n >= 256 ? 0 : n)); d.append(UInt8(n >= 256 ? 0 : n)); d.append(0); d.append(0)
            u16(1); u16(32); u32(pngs[i].count); u32(offset)
            offset += pngs[i].count
        }
        for p in pngs { d.append(p) }
        try? d.write(to: url)
    }

    static func writeIconset(to dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        for base in [16, 32, 128, 256, 512] {
            for mult in [1, 2] {
                let name = mult == 1 ? "icon_\(base)x\(base).png" : "icon_\(base)x\(base)@2x.png"
                try? png(pixels: base * mult)?.write(to: dir.appendingPathComponent(name))
            }
        }
    }
}
