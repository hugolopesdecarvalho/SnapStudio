import AppKit
import SwiftUI

/// Teste sem interface do motor de desenho (`SnapStudio --render-test <pasta>`).
enum SelfTest {
    static func run(outDir: URL) -> Bool {
        try? FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)
        let w = 900, h = 560
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        let g = CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0, space: cs,
                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        // fundo: "janela" com texto e uma faixa colorida no topo (para ver a orientação)
        g.setFillColor(NSColor(white: 0.96, alpha: 1).cgColor); g.fill(CGRect(x: 0, y: 0, width: w, height: h))
        g.setFillColor(NSColor.systemBlue.cgColor); g.fill(CGRect(x: 0, y: h - 60, width: w, height: 60))
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: g, flipped: false)
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 28, weight: .semibold), .foregroundColor: NSColor.black]
        ("TOPO — barra azul" as NSString).draw(at: CGPoint(x: 20, y: h - 48), withAttributes: [.font: NSFont.boldSystemFont(ofSize: 26), .foregroundColor: NSColor.white])
        ("Palavra-passe: hunter2-SECRET" as NSString).draw(at: CGPoint(x: 40, y: h - 170), withAttributes: attrs)
        ("IBAN PT50 0000 1234 5678 9012 3" as NSString).draw(at: CGPoint(x: 40, y: h - 250), withAttributes: attrs)
        ("Texto normal sem ocultar" as NSString).draw(at: CGPoint(x: 40, y: h - 330), withAttributes: attrs)
        NSGraphicsContext.restoreGraphicsState()
        let base = g.makeImage()!

        func ann(_ k: AnnKind, _ c: UInt32 = ArgbColor.red, _ f: (Ann) -> Void) -> Ann { let a = Ann(kind: k, argb: c); f(a); return a }
        let anns: [Ann] = [
            ann(.blur) { $0.p1 = CGPoint(x: 30, y: 105); $0.p2 = CGPoint(x: 520, y: 165) },
            ann(.pixelate) { $0.p1 = CGPoint(x: 30, y: 185); $0.p2 = CGPoint(x: 560, y: 245) },
            ann(.highlight, ArgbColor.highlight) { $0.p1 = CGPoint(x: 30, y: 262); $0.p2 = CGPoint(x: 400, y: 322) },
            ann(.arrow) { $0.p1 = CGPoint(x: 760, y: 420); $0.p2 = CGPoint(x: 580, y: 300); $0.width = 5 },
            ann(.line, ArgbColor.rgb(46, 160, 67)) { $0.p1 = CGPoint(x: 600, y: 120); $0.p2 = CGPoint(x: 860, y: 120); $0.width = 3 },
            ann(.rect, ArgbColor.rgb(37, 99, 235)) { $0.p1 = CGPoint(x: 600, y: 150); $0.p2 = CGPoint(x: 860, y: 250); $0.width = 4 },
            ann(.ellipse, ArgbColor.rgb(124, 58, 237)) { $0.p1 = CGPoint(x: 40, y: 380); $0.p2 = CGPoint(x: 260, y: 520); $0.width = 4 },
            ann(.text) { $0.p1 = CGPoint(x: 300, y: 400); $0.text = "Olá, SnapStudio!\nSegunda linha"; $0.size = 34 },
            ann(.step) { $0.p1 = CGPoint(x: 820, y: 470); $0.number = 1; $0.size = 26 },
            ann(.step, ArgbColor.rgb(255, 214, 0)) { $0.p1 = CGPoint(x: 870, y: 520); $0.number = 2; $0.size = 20 },
            ann(.pen, ArgbColor.rgb(245, 124, 0)) { a in
                a.width = 4
                for i in 0...40 { let t = CGFloat(i) / 40; a.pts += [600 + t * 160, 300 + sin(t * 12) * 20] }
            },
        ]

        // JSON: ida e volta
        let rt = Ann.deserialize(Ann.serialize(anns))
        guard rt.count == anns.count, rt[7].text == anns[7].text, rt[10].pts.count == anns[10].pts.count else {
            print("FALHA: serialização"); return false
        }

        // testes de geometria
        let arrow = anns[3]
        guard arrow.hit(CGPoint(x: 670, y: 360), tol: 4), !arrow.hit(CGPoint(x: 700, y: 300), tol: 4) else { print("FALHA: hit seta"); return false }
        guard anns[8].hit(CGPoint(x: 822, y: 471), tol: 1) else { print("FALHA: hit passo"); return false }
        let tb = anns[7].bounds
        guard tb.width > 150, tb.height > 60, tb.minY >= 390, tb.minY < 410 else { print("FALHA: limites do texto \(tb)"); return false }
        let c45 = Geo.constrain(.zero, CGPoint(x: 100, y: 90), linear: true)
        guard abs(c45.x - c45.y) < 0.5 else { print("FALHA: constrain"); return false }

        let r = AnnRenderer()
        guard let out = r.render(base: base, anns: rt) else { print("FALHA: render"); return false }
        do {
            try ImageUtil.save(base, to: outDir.appendingPathComponent("base.png"))
            try ImageUtil.save(out, to: outDir.appendingPathComponent("render.png"))
            try ImageUtil.save(out, to: outDir.appendingPathComponent("render.jpg"))
            if let c = ImageUtil.crop(out, CGRect(x: 560, y: 90, width: 320, height: 200)) {
                try ImageUtil.save(c, to: outDir.appendingPathComponent("crop.png"))
                guard c.width == 320, c.height == 200 else { print("FALHA: recorte"); return false }
            }
        } catch { print("FALHA: gravar \(error)"); return false }

        // a zona esbatida tem de ser diferente da original (o texto secreto deixa de ser legível)
        guard let a = pixels(base, CGRect(x: 60, y: 120, width: 400, height: 30)),
              let b = pixels(out, CGRect(x: 60, y: 120, width: 400, height: 30)) else { return false }
        let diff = zip(a, b).reduce(0) { $0 + abs(Int($1.0) - Int($1.1)) } / a.count
        guard diff > 3 else { print("FALHA: esbater não alterou a imagem (\(diff))"); return false }

        // Hotkey e definições: ida e volta
        let st = Settings()
        st.hotkeyRegion = Hotkey(keyCode: 0x15, modifiers: 0x1200)
        let data = try! JSONEncoder().encode(st)
        let back = try! JSONDecoder().decode(Settings.self, from: data)
        guard back.hotkeyRegion == st.hotkeyRegion, back.hotkeyRegion.display == "⌃⇧4" else { print("FALHA: definições"); return false }
        let partial = try! JSONDecoder().decode(Settings.self, from: Data("{\"format\":\"jpg\"}".utf8))
        guard partial.format == "jpg", partial.videoFps == 30 else { print("FALHA: definições parciais"); return false }

        print("OK — render \(out.width)x\(out.height), diferença média no esbatido = \(diff), atalho = \(back.hotkeyRegion.display)")
        return true
    }

    /// Desenha as vistas principais fora do ecrã, com uma pasta de capturas de teste.
    static func uiSnapshots(outDir: URL, theme: String) {
        let app = NSApplication.shared
        app.setActivationPolicy(.prohibited)
        let st = AppController.shared.settings
        st.outputFolder = outDir.appendingPathComponent("captures").path
        st.themeMode = theme
        st.afterCapture = "quick"
        Theme.apply(st)
        let caps = st.outputURL
        try? FileManager.default.createDirectory(at: caps, withIntermediateDirectories: true)
        if CaptureItem.scan(caps).isEmpty { _ = run(outDir: caps) }

        func pump(_ s: Double) { RunLoop.main.run(until: Date().addingTimeInterval(s)) }
        func snap(_ view: NSView, _ size: CGSize, _ name: String) {
            let w = NSWindow(contentRect: CGRect(origin: CGPoint(x: -10000, y: -10000), size: size), styleMask: [.titled, .resizable], backing: .buffered, defer: false)
            w.contentView = view
            w.orderFrontRegardless()
            pump(1.2)
            view.layoutSubtreeIfNeeded()
            pump(0.6)
            guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return }
            view.cacheDisplay(in: view.bounds, to: rep)
            try? rep.representation(using: .png, properties: [:])?.write(to: outDir.appendingPathComponent("ui-\(theme)-\(name).png"))
            w.orderOut(nil)
        }

        snap(NSHostingView(rootView: TrayPanelView()), CGSize(width: 380, height: 640), "panel")
        snap(NSHostingView(rootView: SettingsView()), CGSize(width: 560, height: 430), "settings")
        snap(NSHostingView(rootView: HistoryView().tint(Theme.accentColor)), CGSize(width: 980, height: 640), "history")

        let img = caps.appendingPathComponent("render.png")
        for compact in [false, true] {
            guard let m = try? EditorModel(url: img, initial: nil, compact: compact) else { continue }
            let wc = EditorWindowController(model: m)
            let size = wc.window!.contentView!.frame.size
            let host = wc.window!.contentView!
            wc.window!.contentView = NSView()
            // seleciona a seta para mostrar as pegas
            if !compact {
                let a = Ann(kind: .arrow, argb: ArgbColor.red)
                a.p1 = CGPoint(x: 100, y: 500); a.p2 = CGPoint(x: 300, y: 420); a.width = 6
                m.add(a)
                m.tool = .select
            }
            snap(host, size, compact ? "quick-editor" : "full-editor")
        }
    }

    private static func pixels(_ img: CGImage, _ r: CGRect) -> [UInt8]? {
        guard let c = img.cropping(to: r) else { return nil }
        let cs = CGColorSpace(name: CGColorSpace.sRGB)!
        var buf = [UInt8](repeating: 0, count: c.width * c.height * 4)
        guard let g = CGContext(data: &buf, width: c.width, height: c.height, bitsPerComponent: 8, bytesPerRow: c.width * 4, space: cs,
                                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        g.draw(c, in: CGRect(x: 0, y: 0, width: c.width, height: c.height))
        return buf
    }
}
