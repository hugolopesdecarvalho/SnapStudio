import AppKit
import CoreImage
import CoreImage.CIFilterBuiltins
import CoreText

enum Tool: String, CaseIterable {
    case select, arrow, line, rect, ellipse, highlight, blur, pixelate, text, step, pen, crop

    var name: String {
        switch self {
        case .select: return S.t("Selecionar", "Select")
        case .arrow: return S.t("Seta", "Arrow")
        case .line: return S.t("Linha", "Line")
        case .rect: return S.t("Caixa", "Box")
        case .ellipse: return S.t("Elipse", "Ellipse")
        case .highlight: return S.t("Marcador", "Highlighter")
        case .blur: return S.t("Esbater", "Blur")
        case .pixelate: return S.t("Pixelizar", "Pixelate")
        case .text: return S.t("Texto", "Text")
        case .step: return S.t("Passo numerado", "Step number")
        case .pen: return S.t("Desenho livre", "Freehand")
        case .crop: return S.t("Recortar", "Crop")
        }
    }

    var key: String {
        switch self {
        case .select: return "V"
        case .arrow: return "A"
        case .line: return "L"
        case .rect: return "R"
        case .ellipse: return "E"
        case .highlight: return "H"
        case .blur: return "B"
        case .pixelate: return "X"
        case .text: return "T"
        case .step: return "N"
        case .pen: return "P"
        case .crop: return "C"
        }
    }

    var symbol: String {
        switch self {
        case .select: return "cursorarrow"
        case .arrow: return "arrow.up.right"
        case .line: return "line.diagonal"
        case .rect: return "rectangle"
        case .ellipse: return "circle"
        case .highlight: return "highlighter"
        case .blur: return "drop.halffull"
        case .pixelate: return "square.grid.3x3"
        case .text: return "textformat"
        case .step: return "1.circle"
        case .pen: return "scribble"
        case .crop: return "crop"
        }
    }

    var shapeKind: AnnKind? {
        switch self {
        case .arrow: return .arrow
        case .line: return .line
        case .rect: return .rect
        case .ellipse: return .ellipse
        case .highlight: return .highlight
        case .blur: return .blur
        case .pixelate: return .pixelate
        default: return nil
        }
    }
}

enum AnnKind: String, Codable {
    case arrow, line, rect, ellipse, highlight, blur, pixelate, text, step, pen
}

/// Anotação editável (objeto, só pintado na imagem ao guardar). Coordenadas em pixels da imagem, origem no canto superior esquerdo.
final class Ann: Codable, Identifiable {
    var id = UUID()
    var kind: AnnKind
    var argb: UInt32
    var width: CGFloat = 4
    var x1: CGFloat = 0, y1: CGFloat = 0, x2: CGFloat = 0, y2: CGFloat = 0   // texto/passo usam x1,y1
    var text: String = ""
    var size: CGFloat = 26
    var number: Int = 0
    var pts: [CGFloat] = []

    init(kind: AnnKind, argb: UInt32) { self.kind = kind; self.argb = argb }

    var p1: CGPoint { get { CGPoint(x: x1, y: y1) } set { x1 = newValue.x; y1 = newValue.y } }
    var p2: CGPoint { get { CGPoint(x: x2, y: y2) } set { x2 = newValue.x; y2 = newValue.y } }

    var isShape: Bool { ![.text, .step, .pen].contains(kind) }
    var isLinear: Bool { kind == .arrow || kind == .line }
    var isObscure: Bool { kind == .blur || kind == .pixelate }
    var usesFont: Bool { kind == .text || kind == .step }
    var usesWidth: Bool {
        switch kind {
        case .text, .step, .highlight, .blur, .pixelate: return false
        default: return true
        }
    }
    var fontSize: CGFloat { get { size } set { size = newValue } }

    var color: NSColor {
        get { ArgbColor.ns(argb) }
        set { argb = ArgbColor.argb(newValue) }
    }

    var displayName: String {
        switch kind {
        case .arrow: return S.t("Seta", "Arrow")
        case .line: return S.t("Linha", "Line")
        case .rect: return S.t("Caixa", "Box")
        case .ellipse: return S.t("Elipse", "Ellipse")
        case .highlight: return S.t("Marcador", "Highlight")
        case .blur: return S.t("Esbater", "Blur")
        case .pixelate: return S.t("Pixelizar", "Pixelate")
        case .text:
            var s = text.replacingOccurrences(of: "\n", with: " ")
            if s.count > 22 { s = String(s.prefix(21)) + "…" }
            return S.t("Texto “", "Text “") + s + "”"
        case .step: return S.t("Passo ", "Step ") + "\(number)"
        case .pen: return S.t("Desenho", "Drawing")
        }
    }

    var symbol: String {
        switch kind {
        case .arrow: return Tool.arrow.symbol
        case .line: return Tool.line.symbol
        case .rect: return Tool.rect.symbol
        case .ellipse: return Tool.ellipse.symbol
        case .highlight: return Tool.highlight.symbol
        case .blur: return Tool.blur.symbol
        case .pixelate: return Tool.pixelate.symbol
        case .text: return Tool.text.symbol
        case .step: return Tool.step.symbol
        case .pen: return Tool.pen.symbol
        }
    }

    // ---------- geometria ----------

    var stepRadius: CGFloat { size * 0.8 + 4 }

    var bounds: CGRect {
        switch kind {
        case .text:
            var b = Geo.textPath(text, size: size, at: p1).boundingBoxOfPath
            if b.isNull || b.width <= 0 { b = CGRect(x: x1, y: y1, width: size, height: size) }
            return b.insetBy(dx: -size / 8, dy: -size / 8)
        case .step:
            let r = stepRadius
            return CGRect(x: x1 - r, y: y1 - r, width: 2 * r, height: 2 * r)
        case .pen:
            guard pts.count >= 2 else { return .null }
            var minX = CGFloat.greatestFiniteMagnitude, minY = minX, maxX = -minX, maxY = -minX
            var i = 0
            while i + 1 < pts.count {
                minX = min(minX, pts[i]); maxX = max(maxX, pts[i])
                minY = min(minY, pts[i + 1]); maxY = max(maxY, pts[i + 1])
                i += 2
            }
            return CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY).insetBy(dx: -width / 2, dy: -width / 2)
        default:
            let r = Geo.norm(p1, p2)
            return isLinear ? r.insetBy(dx: -width * 2, dy: -width * 2) : r
        }
    }

    func hit(_ p: CGPoint, tol: CGFloat) -> Bool {
        switch kind {
        case .arrow, .line:
            return Geo.distToSegment(p, p1, p2) <= tol + width / 2
        case .step:
            return Geo.dist(p, p1) <= stepRadius + tol
        case .pen:
            let pp = points
            if pp.count == 1 { return Geo.dist(p, pp[0]) <= tol + width }
            for i in 0..<max(0, pp.count - 1) where Geo.distToSegment(p, pp[i], pp[i + 1]) <= tol + width / 2 { return true }
            return false
        default:
            return bounds.insetBy(dx: -tol, dy: -tol).contains(p)
        }
    }

    func offset(_ dx: CGFloat, _ dy: CGFloat) {
        x1 += dx; y1 += dy; x2 += dx; y2 += dy
        var i = 0
        while i + 1 < pts.count { pts[i] += dx; pts[i + 1] += dy; i += 2 }
    }

    var points: [CGPoint] {
        stride(from: 0, to: pts.count - 1, by: 2).map { CGPoint(x: pts[$0], y: pts[$0 + 1]) }
    }

    // ---------- serialização ----------

    static func serialize(_ list: [Ann]) -> Data { (try? JSONEncoder().encode(list)) ?? Data("[]".utf8) }
    static func deserialize(_ d: Data) -> [Ann] { (try? JSONDecoder().decode([Ann].self, from: d)) ?? [] }
}

enum ArgbColor {
    static func ns(_ v: UInt32) -> NSColor {
        NSColor(srgbRed: CGFloat((v >> 16) & 0xFF) / 255, green: CGFloat((v >> 8) & 0xFF) / 255,
                blue: CGFloat(v & 0xFF) / 255, alpha: CGFloat((v >> 24) & 0xFF) / 255)
    }
    static func argb(_ c: NSColor) -> UInt32 {
        let s = c.usingColorSpace(.sRGB) ?? c
        func b(_ x: CGFloat) -> UInt32 { UInt32(max(0, min(255, (x * 255).rounded()))) }
        return (b(s.alphaComponent) << 24) | (b(s.redComponent) << 16) | (b(s.greenComponent) << 8) | b(s.blueComponent)
    }
    static func rgb(_ r: Int, _ g: Int, _ b: Int) -> UInt32 { 0xFF00_0000 | UInt32(r << 16 | g << 8 | b) }

    /// Cor de contraste (texto/halo) para uma cor de fundo.
    static func contrast(_ v: UInt32) -> UInt32 {
        let r = Double((v >> 16) & 0xFF), g = Double((v >> 8) & 0xFF), b = Double(v & 0xFF)
        return (r * 0.299 + g * 0.587 + b * 0.114) > 170 ? rgb(40, 40, 40) : rgb(255, 255, 255)
    }

    static let red = rgb(229, 32, 41)
    static let highlight = rgb(255, 230, 0)
    static let fullPalette: [UInt32] = [rgb(229, 32, 41), rgb(245, 124, 0), rgb(255, 214, 0), rgb(46, 160, 67),
                                        rgb(37, 99, 235), rgb(124, 58, 237), rgb(17, 17, 17), rgb(255, 255, 255)]
    static let quickPalette: [UInt32] = [rgb(229, 32, 41), rgb(255, 214, 0), rgb(37, 99, 235), rgb(17, 17, 17)]

    static func name(_ v: UInt32) -> String {
        switch v {
        case rgb(229, 32, 41): return S.t("Vermelho", "Red")
        case rgb(245, 124, 0): return S.t("Laranja", "Orange")
        case rgb(255, 214, 0): return S.t("Amarelo", "Yellow")
        case rgb(46, 160, 67): return S.t("Verde", "Green")
        case rgb(37, 99, 235): return S.t("Azul", "Blue")
        case rgb(124, 58, 237): return S.t("Roxo", "Purple")
        case rgb(17, 17, 17): return S.t("Preto", "Black")
        case rgb(255, 255, 255): return S.t("Branco", "White")
        default: return S.t("Cor", "Color")
        }
    }
}

enum Geo {
    static func dist(_ a: CGPoint, _ b: CGPoint) -> CGFloat { hypot(a.x - b.x, a.y - b.y) }

    static func distToSegment(_ p: CGPoint, _ a: CGPoint, _ b: CGPoint) -> CGFloat {
        let dx = b.x - a.x, dy = b.y - a.y, len2 = dx * dx + dy * dy
        if len2 < 0.0001 { return dist(p, a) }
        let t = max(0, min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2))
        return dist(p, CGPoint(x: a.x + t * dx, y: a.y + t * dy))
    }

    static func norm(_ a: CGPoint, _ b: CGPoint) -> CGRect {
        CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
    }

    /// Shift ao desenhar: ângulos de 45° (linhas) ou quadrados (caixas).
    static func constrain(_ a: CGPoint, _ p: CGPoint, linear: Bool) -> CGPoint {
        let dx = p.x - a.x, dy = p.y - a.y
        if linear {
            let step = Double.pi / 4
            let ang = (atan2(Double(dy), Double(dx)) / step).rounded() * step
            let len = Double(hypot(dx, dy))
            return CGPoint(x: a.x + CGFloat(cos(ang) * len), y: a.y + CGFloat(sin(ang) * len))
        }
        let d = max(abs(dx), abs(dy))
        return CGPoint(x: a.x + (dx < 0 ? -d : d), y: a.y + (dy < 0 ? -d : d))
    }

    static func font(_ size: CGFloat) -> CTFont {
        NSFont.systemFont(ofSize: max(1, size), weight: .bold) as CTFont
    }

    /// Contorno do texto (como GraphicsPath.AddString): `origin` é o canto superior esquerdo, y para baixo.
    static func textPath(_ text: String, size: CGFloat, at origin: CGPoint) -> CGPath {
        let path = CGMutablePath()
        let font = font(size)
        let ascent = CTFontGetAscent(font), descent = CTFontGetDescent(font), leading = CTFontGetLeading(font)
        let lineH = ceil(ascent + descent + leading)
        let lines = (text.isEmpty ? " " : text).components(separatedBy: "\n")
        for (i, line) in lines.enumerated() {
            let attr = NSAttributedString(string: line, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
            let ctLine = CTLineCreateWithAttributedString(attr)
            let baseline = origin.y + ascent + CGFloat(i) * lineH
            appendGlyphs(of: ctLine, to: path, x: origin.x, baseline: baseline)
        }
        return path
    }

    static func appendGlyphs(of ctLine: CTLine, to path: CGMutablePath, x: CGFloat, baseline: CGFloat) {
        guard let runs = CTLineGetGlyphRuns(ctLine) as? [CTRun] else { return }
        for run in runs {
            let attrs = CTRunGetAttributes(run) as NSDictionary
            let runFont = attrs[kCTFontAttributeName as String] as! CTFont
            let n = CTRunGetGlyphCount(run)
            var glyphs = [CGGlyph](repeating: 0, count: n)
            var pos = [CGPoint](repeating: .zero, count: n)
            CTRunGetGlyphs(run, CFRange(location: 0, length: n), &glyphs)
            CTRunGetPositions(run, CFRange(location: 0, length: n), &pos)
            for k in 0..<n {
                guard let gp = CTFontCreatePathForGlyph(runFont, glyphs[k], nil) else { continue }
                let t = CGAffineTransform(translationX: x + pos[k].x, y: baseline - pos[k].y).scaledBy(x: 1, y: -1)
                path.addPath(gp, transform: t)
            }
        }
    }

    /// Número centrado num círculo (passo).
    static func centeredText(_ s: String, size: CGFloat, center: CGPoint) -> CGPath {
        let font = font(size)
        let attr = NSAttributedString(string: s, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font])
        let line = CTLineCreateWithAttributedString(attr)
        let raw = CGMutablePath()
        appendGlyphs(of: line, to: raw, x: 0, baseline: 0)
        let b = raw.boundingBoxOfPath
        let path = CGMutablePath()
        path.addPath(raw, transform: CGAffineTransform(translationX: center.x - b.midX, y: center.y - b.midY))
        return path
    }
}

extension CGContext {
    /// Desenha uma imagem direita num contexto com y para baixo.
    func drawUpright(_ img: CGImage, in r: CGRect) {
        saveGState()
        translateBy(x: r.minX, y: r.maxY)
        scaleBy(x: 1, y: -1)
        draw(img, in: CGRect(origin: .zero, size: r.size))
        restoreGState()
    }
}

/// Desenha anotações sobre uma imagem base. Guarda em cache as zonas esbatidas/pixelizadas.
final class AnnRenderer {
    private let ci = CIContext(options: [.cacheIntermediates: false])
    private var cache: [String: CGImage] = [:]
    private var cachedBaseID: ObjectIdentifier?

    func draw(_ a: Ann, in g: CGContext, base: CGImage) {
        let col = ArgbColor.ns(a.argb).cgColor
        switch a.kind {
        case .arrow:
            if Geo.dist(a.p1, a.p2) < 1 { return }
            let w = a.width
            let head = max(3.2, 14 / w)
            let len = head * w, half = head * 0.85 * w / 2
            let dx = a.x2 - a.x1, dy = a.y2 - a.y1, d = hypot(dx, dy)
            let ux = dx / d, uy = dy / d
            let effLen = min(len, d)
            let baseP = CGPoint(x: a.x2 - ux * effLen, y: a.y2 - uy * effLen)
            g.saveGState()
            g.setStrokeColor(col); g.setFillColor(col)
            g.setLineWidth(w); g.setLineCap(.round)
            let lineEnd = CGPoint(x: a.x2 - ux * effLen * 0.7, y: a.y2 - uy * effLen * 0.7)
            g.move(to: a.p1); g.addLine(to: lineEnd); g.strokePath()
            g.move(to: a.p2)
            g.addLine(to: CGPoint(x: baseP.x - uy * half, y: baseP.y + ux * half))
            g.addLine(to: CGPoint(x: baseP.x + uy * half, y: baseP.y - ux * half))
            g.closePath(); g.fillPath()
            g.restoreGState()
        case .line:
            g.saveGState()
            g.setStrokeColor(col); g.setLineWidth(a.width); g.setLineCap(.round)
            g.move(to: a.p1); g.addLine(to: a.p2); g.strokePath()
            g.restoreGState()
        case .rect:
            g.saveGState()
            g.setStrokeColor(col); g.setLineWidth(a.width); g.setLineJoin(.miter)
            g.stroke(Geo.norm(a.p1, a.p2))
            g.restoreGState()
        case .ellipse:
            g.saveGState()
            g.setStrokeColor(col); g.setLineWidth(a.width)
            g.strokeEllipse(in: Geo.norm(a.p1, a.p2))
            g.restoreGState()
        case .highlight:
            g.saveGState()
            g.setFillColor(ArgbColor.ns(a.argb).withAlphaComponent(105 / 255).cgColor)
            g.fill(Geo.norm(a.p1, a.p2))
            g.restoreGState()
        case .blur, .pixelate:
            obscure(g, base: base, rect: Geo.norm(a.p1, a.p2), pixelate: a.kind == .pixelate)
        case .text:
            let path = Geo.textPath(a.text, size: a.size, at: a.p1)
            g.saveGState()
            g.addPath(path)
            g.setLineJoin(.round)
            g.setLineWidth(max(2, a.size / 5))
            g.setStrokeColor(ArgbColor.ns(ArgbColor.contrast(a.argb)).cgColor)
            g.strokePath()
            g.addPath(path)
            g.setFillColor(col)
            g.fillPath()
            g.restoreGState()
        case .step:
            let r = a.bounds
            let fg = ArgbColor.contrast(a.argb)
            g.saveGState()
            g.setFillColor(col)
            g.fillEllipse(in: r)
            g.setStrokeColor(ArgbColor.ns(fg).cgColor)
            g.setLineWidth(max(1.5, a.size / 12))
            g.strokeEllipse(in: r)
            g.addPath(Geo.centeredText("\(a.number)", size: a.size * 0.95, center: CGPoint(x: r.midX, y: r.midY)))
            g.setFillColor(ArgbColor.ns(fg).cgColor)
            g.fillPath()
            g.restoreGState()
        case .pen:
            let pp = a.points
            guard !pp.isEmpty else { return }
            g.saveGState()
            g.setFillColor(col); g.setStrokeColor(col)
            if pp.count == 1 {
                g.fillEllipse(in: CGRect(x: pp[0].x - a.width / 2, y: pp[0].y - a.width / 2, width: a.width, height: a.width))
            } else {
                g.setLineWidth(a.width); g.setLineCap(.round); g.setLineJoin(.round)
                g.addLines(between: pp)
                g.strokePath()
            }
            g.restoreGState()
        }
    }

    /// Oculta uma zona: desfoque forte ou pixelização (Core Image), calculado sobre a imagem base.
    func obscure(_ g: CGContext, base: CGImage, rect: CGRect, pixelate: Bool) {
        let W = CGFloat(base.width), H = CGFloat(base.height)
        let r = rect.integral.intersection(CGRect(x: 0, y: 0, width: W, height: H))
        if r.isNull || r.width < 2 || r.height < 2 { return }
        if cachedBaseID != ObjectIdentifier(base) { cache.removeAll(); cachedBaseID = ObjectIdentifier(base) }
        let key = "\(pixelate ? "p" : "b"):\(Int(r.minX)),\(Int(r.minY)),\(Int(r.width)),\(Int(r.height))"
        var img = cache[key]
        if img == nil {
            let src = CIImage(cgImage: base)
            let ciRect = CGRect(x: r.minX, y: H - r.maxY, width: r.width, height: r.height)
            var out: CIImage
            if pixelate {
                let block = max(10, min(r.width, r.height) / 8)
                let f = CIFilter.pixellate()
                f.inputImage = src.clampedToExtent()
                f.center = ciRect.origin
                f.scale = Float(block)
                out = f.outputImage ?? src
            } else {
                let block = max(18, min(r.width, r.height) / 3)
                let f = CIFilter.gaussianBlur()
                f.inputImage = src.clampedToExtent()
                f.radius = Float(block)
                out = f.outputImage ?? src
            }
            out = out.cropped(to: ciRect)
            img = ci.createCGImage(out, from: ciRect, format: .RGBA8, colorSpace: CGColorSpace(name: CGColorSpace.sRGB))
            if cache.count > 64 { cache.removeAll() }
            cache[key] = img
        }
        if let i = img { g.drawUpright(i, in: r) }
    }

    /// Imagem final (base + anotações).
    func render(base: CGImage, anns: [Ann]) -> CGImage? {
        let w = base.width, h = base.height
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        guard let g = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: cs,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        g.translateBy(x: 0, y: CGFloat(h))
        g.scaleBy(x: 1, y: -1)
        g.interpolationQuality = .high
        g.setShouldAntialias(true)
        g.drawUpright(base, in: CGRect(x: 0, y: 0, width: w, height: h))
        for a in anns { draw(a, in: g, base: base) }
        return g.makeImage()
    }
}
