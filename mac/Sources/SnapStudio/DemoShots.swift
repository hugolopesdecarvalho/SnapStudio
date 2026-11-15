import AppKit
import ScreenCaptureKit
import SwiftUI

/// `SnapStudio --readme-shots <pasta>`: cria capturas fictícias, abre as janelas reais da app com esse conteúdo
/// (sem alterar as definições do utilizador) e fotografa cada janela para o README.
enum DemoShots {
    static func run(outDir: URL) {
        let app = NSApplication.shared
        app.setActivationPolicy(.accessory)
        let fm = FileManager.default
        try? fm.createDirectory(at: outDir, withIntermediateDirectories: true)
        let caps = fm.temporaryDirectory.appendingPathComponent("SnapStudioDemo-\(getpid())")
        try? fm.removeItem(at: caps)
        try? fm.createDirectory(at: caps, withIntermediateDirectories: true)

        // definições só em memória (nunca gravadas)
        let st = AppController.shared.settings
        st.outputFolder = caps.path
        st.afterCapture = "quick"
        st.copyToClipboard = true
        st.editorColor = 0; st.editorWidth = 4; st.editorFont = 26   // estilo por omissão (não o do utilizador)
        st.panelWidth = 0; st.panelHeight = 0

        let images = DemoArt.all()
        var urls: [URL] = []
        let now = Date()
        // datas espalhadas: hoje, ontem, esta semana (segundos antes de agora)
        let ages: [TimeInterval] = [180, 1500, 10800, 93600, 180000, 270000]
        for (i, (name, img)) in images.enumerated() {
            let u = caps.appendingPathComponent(name)
            try? ImageUtil.save(img, to: u)
            let d = now.addingTimeInterval(-ages[i % ages.count])
            try? fm.setAttributes([.creationDate: d, .modificationDate: d], ofItemAtPath: u.path)
            urls.append(u)
        }

        func pump(_ s: Double) { RunLoop.main.run(until: Date().addingTimeInterval(s)) }

        for theme in ["light", "dark"] {
            st.themeMode = theme
            Theme.apply(st)
            pump(0.4)

            // painel
            let panel = TrayPanelController()
            panel.window.setFrame(CGRect(x: 200, y: 120, width: 460, height: 640), display: true)
            panel.window.orderFrontRegardless()
            pump(1.5)
            shoot(panel.window, outDir.appendingPathComponent("panel-\(theme).png"))
            panel.window.orderOut(nil)

            // editor completo com anotações
            if let m = try? EditorModel(url: urls[0], initial: nil, compact: false) {
                annotateDashboard(m)
                let wc = EditorWindowController(model: m)
                wc.window?.setContentSize(CGSize(width: 1180, height: 760))
                wc.window?.center()
                wc.window?.orderFrontRegardless()
                pump(1.5)
                m.canvas?.zoomFit()
                pump(0.8)
                shoot(wc.window!, outDir.appendingPathComponent("editor-\(theme).png"))
                wc.window?.orderOut(nil)
            }

            // editor rápido
            if let m = try? EditorModel(url: urls[1], initial: nil, compact: true) {
                m.copied = true
                annotateLogin(m)
                let wc = EditorWindowController(model: m)
                wc.window?.center()
                wc.window?.orderFrontRegardless()
                pump(1.5)
                shoot(wc.window!, outDir.appendingPathComponent("quick-editor-\(theme).png"))
                wc.window?.orderOut(nil)
            }

            // histórico
            let hist = HostWindowController(title: S.t("Histórico de capturas", "Capture history"), size: CGSize(width: 1000, height: 640),
                                            root: AnyView(HistoryView()), resizable: true, autosave: nil)
            hist.window?.center()
            hist.window?.orderFrontRegardless()
            pump(2.5)
            shoot(hist.window!, outDir.appendingPathComponent("history-\(theme).png"))
            hist.window?.orderOut(nil)
        }
        try? fm.removeItem(at: caps)
        print("OK -> \(outDir.path)")
    }

    /// `--history-test <pasta>`: testa a seleção múltipla do histórico (pasta temporária, definições só em memória).
    static func historyTest(outDir: URL) -> Bool {
        NSApplication.shared.setActivationPolicy(.accessory)
        let fm = FileManager.default
        try? fm.createDirectory(at: outDir, withIntermediateDirectories: true)
        let dir = fm.temporaryDirectory.appendingPathComponent("SnapStudioHist-\(getpid())")
        try? fm.removeItem(at: dir); try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? fm.removeItem(at: dir) }
        AppController.shared.settings.outputFolder = dir.path
        let arts = DemoArt.all()
        for (i, (name, img)) in arts.enumerated() {
            let u = dir.appendingPathComponent(name)
            try? ImageUtil.save(img, to: u)
            let d = Date().addingTimeInterval(TimeInterval(-600 * i))
            try? fm.setAttributes([.creationDate: d, .modificationDate: d], ofItemAtPath: u.path)
        }
        let m = HistoryModel()
        var ok = true
        func check(_ c: Bool, _ msg: String) { if !c { print("FALHA: \(msg)"); ok = false } }
        let f = m.filtered
        check(f.count == 6, "6 capturas (\(f.count))")
        m.click(f[0], modifiers: []); check(m.selection == [f[0].url], "clique simples")
        m.click(f[2], modifiers: .command); check(m.selection.count == 2, "⌘-clique junta")
        m.click(f[2], modifiers: .command); check(m.selection == [f[0].url], "⌘-clique retira")
        m.click(f[1], modifiers: []); m.click(f[4], modifiers: .shift)
        check(m.selection == Set(f[1...4].map { $0.url }), "⇧-clique intervalo (\(m.selection.count))")
        check(m.targets(for: f[2]).count == 4 && m.targets(for: f[5]).count == 1, "ação aplica-se à seleção")
        m.selectAll(); check(m.selection.count == 6, "⌘A")
        // janela com 3 selecionadas, para ver o aspeto
        m.clearSelection(); m.click(f[0], modifiers: []); m.click(f[2], modifiers: .command); m.click(f[3], modifiers: .command)
        func pump(_ s: Double) { RunLoop.main.run(until: Date().addingTimeInterval(s)) }
        let wc = HostWindowController(title: "History", size: CGSize(width: 1000, height: 640), root: AnyView(HistoryView(model: m)), resizable: true, autosave: nil)
        wc.window?.center(); wc.window?.orderFrontRegardless(); pump(2)
        shoot(wc.window!, outDir.appendingPathComponent("history-multi.png"))
        wc.window?.orderOut(nil)
        // apagar uma (sem diálogo)
        m.clearSelection(); m.click(f[5], modifiers: [])
        m.trash(m.selectedItems)
        check(m.items.count == 5 && m.selection.isEmpty, "apagar uma (\(m.items.count))")
        print(ok ? "OK — seleção múltipla do histórico" : "FALHAS")
        return ok
    }

    /// `--nav-test <pasta>`: testa imagem anterior/seguinte no editor completo, com 3 capturas fictícias.
    static func navTest(outDir: URL) -> Bool {
        NSApplication.shared.setActivationPolicy(.accessory)
        let fm = FileManager.default
        try? fm.createDirectory(at: outDir, withIntermediateDirectories: true)
        let dir = fm.temporaryDirectory.appendingPathComponent("SnapStudioNav-\(getpid())")
        try? fm.removeItem(at: dir); try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { for u in (try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? [] { Sidecar.delete(u) }; try? fm.removeItem(at: dir) }
        let arts = [DemoArt.login(), DemoArt.dashboard(), DemoArt.code()]
        var urls: [URL] = []
        for (i, a) in arts.enumerated() {
            let u = dir.appendingPathComponent("capture-\(i + 1).png")
            try? ImageUtil.save(a, to: u)
            let d = Date().addingTimeInterval(TimeInterval(-3600 * (3 - i)))   // 1 é a mais antiga
            try? fm.setAttributes([.creationDate: d, .modificationDate: d], ofItemAtPath: u.path)
            urls.append(u)
        }
        func pump(_ s: Double) { RunLoop.main.run(until: Date().addingTimeInterval(s)) }
        guard let m = try? EditorModel(url: urls[1], initial: nil, compact: false) else { return false }
        let wc = EditorWindowController(model: m)
        wc.window?.setContentSize(CGSize(width: 1180, height: 760)); wc.window?.center(); wc.window?.orderFrontRegardless()
        pump(1.2)
        var ok = true
        func check(_ c: Bool, _ msg: String) { if !c { print("FALHA: \(msg)"); ok = false } }
        check(wc.position?.index == 2 && wc.position?.count == 3, "posição inicial \(String(describing: wc.position))")
        check(wc.canNavigate(-1) && wc.canNavigate(1), "pode navegar nos dois sentidos")
        shoot(wc.window!, outDir.appendingPathComponent("nav-2of3.png"))
        wc.navigate(1); pump(0.8)
        check(wc.model.fileURL.lastPathComponent == "capture-3.png", "seguinte → \(wc.model.fileURL.lastPathComponent)")
        check(wc.position?.index == 3 && !wc.canNavigate(1), "última sem seguinte")
        // anotar e recuar: a alteração é guardada antes de mudar
        let a = Ann(kind: .rect, argb: ArgbColor.red); a.p1 = CGPoint(x: 100, y: 100); a.p2 = CGPoint(x: 400, y: 300)
        wc.model.add(a)
        wc.navigate(-1); pump(0.8)
        check(wc.model.fileURL.lastPathComponent == "capture-2.png", "anterior → \(wc.model.fileURL.lastPathComponent)")
        check(Sidecar.hasAnnotations(urls[2]), "anotação da 3.ª guardada ao mudar")
        wc.navigate(-1); pump(0.8)
        check(wc.position?.index == 1 && !wc.canNavigate(-1), "primeira sem anterior")
        shoot(wc.window!, outDir.appendingPathComponent("nav-1of3.png"))
        wc.window?.orderOut(nil)
        print(ok ? "OK — anterior/seguinte" : "FALHAS")
        return ok
    }

    /// `--props-shots <pasta>`: fotografa o painel de propriedades do editor com várias ferramentas/seleções.
    static func propsShots(outDir: URL) {
        NSApplication.shared.setActivationPolicy(.accessory)
        let fm = FileManager.default
        try? fm.createDirectory(at: outDir, withIntermediateDirectories: true)
        let img = fm.temporaryDirectory.appendingPathComponent("SnapStudioProps-\(getpid()).png")
        try? ImageUtil.save(DemoArt.dashboard(), to: img)
        defer { try? fm.removeItem(at: img); Sidecar.delete(img) }
        func pump(_ s: Double) { RunLoop.main.run(until: Date().addingTimeInterval(s)) }
        guard let m = try? EditorModel(url: img, initial: nil, compact: false) else { return }
        let hl = Ann(kind: .highlight, argb: ArgbColor.highlight); hl.p1 = CGPoint(x: 72, y: 336); hl.p2 = CGPoint(x: 360, y: 384); hl.radius = 10
        m.add(hl)
        let wc = EditorWindowController(model: m)
        wc.window?.setContentSize(CGSize(width: 1180, height: 760))
        wc.window?.center()
        wc.window?.orderFrontRegardless()
        pump(1.5)
        let cases: [(String, Tool, Bool)] = [("select-none", .select, false), ("select-highlight", .select, true), ("text", .text, false),
                                             ("rect", .rect, false), ("arrow", .arrow, false), ("blur", .blur, false), ("step", .step, false), ("crop", .crop, false)]
        for (name, t, sel) in cases {
            m.setTool(t)
            m.selectedID = sel ? hl.id : nil
            if sel { m.syncPropsFromSelection() }
            m.rounded = (t == .rect || sel)
            m.fillOn = (t == .rect)
            m.changed()
            pump(0.8)
            shoot(wc.window!, outDir.appendingPathComponent("props-\(name).png"))
        }
        wc.window?.orderOut(nil)
    }

    /// `--settings-shots <pasta> [light|dark]`: fotografa a janela das Definições em cada secção.
    static func settingsShots(outDir: URL, theme: String) {
        NSApplication.shared.setActivationPolicy(.accessory)
        try? FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)
        let st = AppController.shared.settings
        st.themeMode = theme
        Theme.apply(st)
        func pump(_ s: Double) { RunLoop.main.run(until: Date().addingTimeInterval(s)) }
        let wc = HostWindowController(title: S.t("Definições da SnapStudio", "SnapStudio Settings"), size: SettingsView.size,
                                      root: AnyView(SettingsView()), resizable: false, autosave: nil)
        wc.window?.center()
        wc.window?.orderFrontRegardless()
        pump(1.2)
        for tab in ["general", "appearance", "after", "shortcuts", "files", "video", "about"] {
            NotificationCenter.default.post(name: .snapShowSettingsTab, object: tab)
            pump(0.8)
            shoot(wc.window!, outDir.appendingPathComponent("settings-\(theme)-\(tab).png"))
        }
        wc.window?.orderOut(nil)
    }

    private static func annotateDashboard(_ m: EditorModel) {
        func a(_ k: AnnKind, _ c: UInt32, _ f: (Ann) -> Void) { let x = Ann(kind: k, argb: c); f(x); m.add(x) }
        let red = ArgbColor.red, blue = ArgbColor.rgb(37, 99, 235)
        m.rounded = true; m.radius = 16      // mostra os cantos arredondados no painel de propriedades
        a(.blur, red) { $0.p1 = CGPoint(x: 1120, y: 22); $0.p2 = CGPoint(x: 1400, y: 70); $0.radius = 12 }
        a(.rect, red) { $0.p1 = CGPoint(x: 52, y: 120); $0.p2 = CGPoint(x: 388, y: 290); $0.width = 6; $0.radius = 16 }
        a(.step, red) { $0.p1 = CGPoint(x: 52, y: 120); $0.number = 1; $0.size = 30 }
        a(.arrow, red) { $0.p1 = CGPoint(x: 700, y: 412); $0.p2 = CGPoint(x: 612, y: 452); $0.width = 7; $0.radius = 16 }
        a(.step, red) { $0.p1 = CGPoint(x: 676, y: 372); $0.number = 2; $0.size = 30 }
        a(.text, red) { $0.p1 = CGPoint(x: 704, y: 350); $0.text = "Peak in August"; $0.size = 36 }
        a(.highlight, ArgbColor.highlight) { $0.p1 = CGPoint(x: 72, y: 336); $0.p2 = CGPoint(x: 360, y: 384); $0.radius = 10 }
        a(.ellipse, blue) { $0.p1 = CGPoint(x: 1010, y: 160); $0.p2 = CGPoint(x: 1380, y: 270); $0.width = 6 }
        a(.step, blue) { $0.p1 = CGPoint(x: 1010, y: 160); $0.number = 3; $0.size = 30 }
        m.selectedID = nil
        m.tool = .arrow
    }

    private static func annotateLogin(_ m: EditorModel) {
        func a(_ k: AnnKind, _ c: UInt32, _ f: (Ann) -> Void) { let x = Ann(kind: k, argb: c); f(x); m.add(x) }
        let red = ArgbColor.red
        a(.pixelate, red) { $0.p1 = CGPoint(x: 482, y: 474); $0.p2 = CGPoint(x: 958, y: 526) }
        a(.rect, red) { $0.p1 = CGPoint(x: 470, y: 560); $0.p2 = CGPoint(x: 970, y: 630); $0.width = 6 }
        a(.arrow, red) { $0.p1 = CGPoint(x: 1220, y: 740); $0.p2 = CGPoint(x: 990, y: 610); $0.width = 7 }
        a(.text, red) { $0.p1 = CGPoint(x: 1080, y: 750); $0.text = "Click here"; $0.size = 46 }
        m.selectedID = nil
    }

    /// Fotografa uma janela (com sombra, fundo transparente).
    static func shoot(_ w: NSWindow, _ url: URL) {
        let sem = DispatchSemaphore(value: 0)
        let wid = CGWindowID(w.windowNumber)
        let scale = w.screen?.backingScaleFactor ?? 2
        var result: CGImage?
        Task.detached {
            do {
                let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
                if let scw = content.windows.first(where: { $0.windowID == wid }) {
                    let cfg = SCStreamConfiguration()
                    cfg.width = Int((scw.frame.width + 120) * scale)
                    cfg.height = Int((scw.frame.height + 120) * scale)
                    cfg.showsCursor = false
                    cfg.ignoreShadowsSingleWindow = false
                    cfg.shouldBeOpaque = false
                    cfg.captureResolution = .best
                    cfg.colorSpaceName = CGColorSpace.sRGB
                    result = try await SCScreenshotManager.captureImage(contentFilter: SCContentFilter(desktopIndependentWindow: scw), configuration: cfg)
                }
            } catch { print("erro \(url.lastPathComponent): \(error)") }
            sem.signal()
        }
        while sem.wait(timeout: .now()) == .timedOut { RunLoop.main.run(until: Date().addingTimeInterval(0.05)) }
        if let img = result { try? ImageUtil.save(trimTransparent(img), to: url) }
    }

    /// Corta as margens totalmente transparentes.
    static func trimTransparent(_ img: CGImage) -> CGImage {
        let w = img.width, h = img.height
        var buf = [UInt8](repeating: 0, count: w * h * 4)
        guard let g = CGContext(data: &buf, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return img }
        g.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
        var minX = w, minY = h, maxX = -1, maxY = -1
        for y in 0..<h { for x in 0..<w where buf[(y * w + x) * 4 + 3] > 2 {
            minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
        } }
        if maxX < minX { return img }
        return img.cropping(to: CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)) ?? img
    }
}

/// Capturas fictícias (nada do ecrã do utilizador).
enum DemoArt {
    static let W = 1440, H = 900

    static func all() -> [(String, CGImage)] {
        [("2026-11-14_10-42-07.png", dashboard()), ("2026-11-14_10-18-55.png", login()), ("2026-11-14_09-31-20.png", code()),
         ("2026-11-13_17-05-12.png", settings()), ("2026-11-12_15-48-40.png", landing()), ("2026-11-11_11-12-03.png", kanban())]
    }

    private static func canvas(_ bg: NSColor, _ draw: (CGContext) -> Void) -> CGImage {
        let g = CGContext(data: nil, width: W, height: H, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        g.translateBy(x: 0, y: CGFloat(H)); g.scaleBy(x: 1, y: -1)   // y para baixo
        g.setFillColor(bg.cgColor); g.fill(CGRect(x: 0, y: 0, width: W, height: H))
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: g, flipped: true)
        draw(g)
        NSGraphicsContext.restoreGraphicsState()
        return g.makeImage()!
    }

    private static func c(_ hex: Int, _ a: CGFloat = 1) -> NSColor { Theme.hex(hex).withAlphaComponent(a) }
    private static func box(_ g: CGContext, _ r: CGRect, _ col: NSColor, radius: CGFloat = 14, stroke: NSColor? = nil) {
        g.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil))
        g.setFillColor(col.cgColor); g.fillPath()
        if let s = stroke { g.addPath(CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil)); g.setStrokeColor(s.cgColor); g.setLineWidth(2); g.strokePath() }
    }
    private static func text(_ s: String, _ p: CGPoint, _ size: CGFloat, _ col: NSColor, _ w: NSFont.Weight = .regular, mono: Bool = false) {
        let f = mono ? NSFont.monospacedSystemFont(ofSize: size, weight: w) : NSFont.systemFont(ofSize: size, weight: w)
        (s as NSString).draw(at: p, withAttributes: [.font: f, .foregroundColor: col])
    }

    static func dashboard() -> CGImage {
        canvas(c(0xF4F6FB)) { g in
            box(g, CGRect(x: 0, y: 0, width: W, height: 92), .white, radius: 0)
            text("Northwind Analytics", CGPoint(x: 52, y: 28), 30, c(0x1B1D24), .bold)
            text("Overview   Reports   Customers   Settings", CGPoint(x: 420, y: 36), 20, c(0x6B7280))
            text("maria.silva@northwind.example", CGPoint(x: 1130, y: 34), 19, c(0x374151))
            let kpis = [("Revenue", "$248,930", "+18.2%", 0x16A34A), ("Orders", "3,412", "+6.4%", 0x16A34A),
                        ("Customers", "1,087", "+2.1%", 0x16A34A), ("Refunds", "41", "-12%", 0xDC2626)]
            for (i, k) in kpis.enumerated() {
                let r = CGRect(x: 60 + CGFloat(i) * 338, y: 130, width: 318, height: 150)
                box(g, r, .white)
                text(k.0, CGPoint(x: r.minX + 26, y: r.minY + 24), 20, c(0x6B7280))
                text(k.1, CGPoint(x: r.minX + 26, y: r.minY + 56), 40, c(0x111827), .bold)
                text(k.2, CGPoint(x: r.minX + 26, y: r.minY + 110), 18, c(k.3), .semibold)
            }
            // gráfico de barras
            let chart = CGRect(x: 60, y: 320, width: 860, height: 520)
            box(g, chart, .white)
            text("Monthly revenue", CGPoint(x: 86, y: 344), 22, c(0x111827), .semibold)
            let vals: [CGFloat] = [0.42, 0.55, 0.48, 0.62, 0.70, 0.66, 0.78, 0.95, 0.81, 0.74, 0.69, 0.72]
            for (i, v) in vals.enumerated() {
                let bw: CGFloat = 46, x = chart.minX + 50 + CGFloat(i) * 66, hgt = v * 360
                box(g, CGRect(x: x, y: chart.maxY - 50 - hgt, width: bw, height: hgt), c(i == 7 ? 0x4F46E5 : 0xA5B4FC), radius: 8)
                text(["J","F","M","A","M","J","J","A","S","O","N","D"][i], CGPoint(x: x + 16, y: chart.maxY - 40), 16, c(0x6B7280))
            }
            // lista lateral
            let side = CGRect(x: 950, y: 320, width: 430, height: 520)
            box(g, side, .white)
            text("Top products", CGPoint(x: 976, y: 344), 22, c(0x111827), .semibold)
            let items = [("Aurora Desk Lamp", "$32,410"), ("Nimbus Chair", "$28,975"), ("Atlas Backpack", "$21,330"),
                         ("Orbit Headphones", "$18,040"), ("Lumen Notebook", "$9,880"), ("Zen Mug", "$4,120")]
            for (i, it) in items.enumerated() {
                let y = 400 + CGFloat(i) * 70
                box(g, CGRect(x: 976, y: y, width: 44, height: 44), c([0xFDE68A, 0xBFDBFE, 0xBBF7D0, 0xFBCFE8, 0xDDD6FE, 0xFED7AA][i]), radius: 10)
                text(it.0, CGPoint(x: 1036, y: y + 10), 19, c(0x1F2937))
                text(it.1, CGPoint(x: 1250, y: y + 10), 19, c(0x111827), .semibold)
            }
        }
    }

    static func login() -> CGImage {
        canvas(c(0xEEF2FF)) { g in
            let card = CGRect(x: 420, y: 150, width: 600, height: 600)
            box(g, card, .white, radius: 24)
            box(g, CGRect(x: 690, y: 190, width: 60, height: 60), c(0x4F46E5), radius: 16)
            text("Sign in to Northwind", CGPoint(x: 555, y: 272), 32, c(0x111827), .bold)
            text("Email", CGPoint(x: 480, y: 338), 18, c(0x374151), .medium)
            box(g, CGRect(x: 480, y: 366, width: 480, height: 56), c(0xF9FAFB), radius: 10, stroke: c(0xD1D5DB))
            text("maria.silva@northwind.example", CGPoint(x: 500, y: 382), 20, c(0x111827))
            text("Password", CGPoint(x: 480, y: 444), 18, c(0x374151), .medium)
            box(g, CGRect(x: 480, y: 472, width: 480, height: 56), c(0xF9FAFB), radius: 10, stroke: c(0xD1D5DB))
            text("••••••••••••", CGPoint(x: 500, y: 486), 22, c(0x111827))
            box(g, CGRect(x: 480, y: 566, width: 480, height: 58), c(0x4F46E5), radius: 12)
            text("Sign in", CGPoint(x: 683, y: 582), 21, .white, .semibold)
            text("Forgot your password?", CGPoint(x: 618, y: 660), 18, c(0x4F46E5), .medium)
        }
    }

    static func code() -> CGImage {
        canvas(c(0x1E1E2E)) { g in
            box(g, CGRect(x: 0, y: 0, width: W, height: 56), c(0x181825), radius: 0)
            for (i, col) in [0xFF5F57, 0xFEBC2E, 0x28C840].enumerated() { box(g, CGRect(x: 24 + CGFloat(i) * 26, y: 20, width: 16, height: 16), c(col), radius: 8) }
            text("CaptureService.swift", CGPoint(x: 140, y: 16), 18, c(0xCDD6F4))
            let lines: [(String, Int)] = [
                ("import ScreenCaptureKit", 0xCBA6F7), ("", 0), ("struct CaptureService {", 0xF9E2AF),
                ("    func capture(_ display: SCDisplay) async throws -> CGImage {", 0x89B4FA),
                ("        let filter = SCContentFilter(display: display, excludingWindows: [])", 0xCDD6F4),
                ("        let config = SCStreamConfiguration()", 0xCDD6F4), ("        config.showsCursor = false", 0xA6E3A1),
                ("        return try await SCScreenshotManager.captureImage(", 0xCDD6F4),
                ("            contentFilter: filter, configuration: config)", 0xCDD6F4), ("    }", 0xCDD6F4), ("}", 0xF9E2AF)]
            for (i, l) in lines.enumerated() {
                text(String(format: "%2d", i + 1), CGPoint(x: 30, y: 90 + CGFloat(i) * 40), 20, c(0x6C7086), mono: true)
                text(l.0, CGPoint(x: 90, y: 90 + CGFloat(i) * 40), 20, c(l.1), mono: true)
            }
        }
    }

    static func settings() -> CGImage {
        canvas(c(0xF9FAFB)) { g in
            box(g, CGRect(x: 0, y: 0, width: 300, height: H), c(0xF3F4F6), radius: 0)
            for (i, s) in ["General", "Notifications", "Billing", "Team", "Integrations", "Security"].enumerated() {
                if i == 1 { box(g, CGRect(x: 20, y: 110 + CGFloat(i) * 56, width: 260, height: 46), c(0xE0E7FF), radius: 10) }
                text(s, CGPoint(x: 44, y: 122 + CGFloat(i) * 56), 20, c(i == 1 ? 0x3730A3 : 0x374151), i == 1 ? .semibold : .regular)
            }
            text("Notifications", CGPoint(x: 360, y: 60), 34, c(0x111827), .bold)
            for (i, s) in ["Weekly summary email", "New order alerts", "Low stock warnings", "Product updates", "Security alerts"].enumerated() {
                let y = 150 + CGFloat(i) * 110
                box(g, CGRect(x: 360, y: y, width: 1000, height: 90), .white, radius: 14, stroke: c(0xE5E7EB))
                text(s, CGPoint(x: 390, y: y + 30), 21, c(0x1F2937), .medium)
                let on = i != 3
                box(g, CGRect(x: 1260, y: y + 28, width: 64, height: 34), c(on ? 0x4F46E5 : 0xD1D5DB), radius: 17)
                g.setFillColor(NSColor.white.cgColor); g.fillEllipse(in: CGRect(x: on ? 1292 : 1264, y: y + 31, width: 28, height: 28))
            }
        }
    }

    static func landing() -> CGImage {
        canvas(c(0x0F172A)) { g in
            let grad = CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: [c(0x4F46E5).cgColor, c(0xDB2777).cgColor] as CFArray, locations: [0, 1])!
            g.saveGState(); g.addRect(CGRect(x: 0, y: 0, width: W, height: 560)); g.clip()
            g.drawLinearGradient(grad, start: .zero, end: CGPoint(x: W, y: 560), options: []); g.restoreGState()
            text("Travel lighter.", CGPoint(x: 120, y: 190), 84, .white, .heavy)
            text("Smart luggage that weighs itself and never gets lost.", CGPoint(x: 124, y: 310), 28, c(0xFFFFFF, 0.85))
            box(g, CGRect(x: 124, y: 390, width: 220, height: 64), .white, radius: 32)
            text("Shop now", CGPoint(x: 182, y: 408), 24, c(0x4F46E5), .bold)
            for i in 0..<3 {
                let r = CGRect(x: 120 + CGFloat(i) * 410, y: 620, width: 380, height: 220)
                box(g, r, c(0x1E293B), radius: 18)
                text(["Built-in scale", "GPS tracking", "10-year warranty"][i], CGPoint(x: r.minX + 30, y: r.minY + 40), 26, .white, .semibold)
                text("Lorem ipsum dolor sit amet.", CGPoint(x: r.minX + 30, y: r.minY + 90), 19, c(0x94A3B8))
            }
        }
    }

    static func kanban() -> CGImage {
        canvas(c(0xF1F5F9)) { g in
            text("Sprint 24", CGPoint(x: 60, y: 40), 34, c(0x0F172A), .bold)
            let cols = [("To do", 0x64748B), ("In progress", 0x2563EB), ("Review", 0xD97706), ("Done", 0x16A34A)]
            for (i, col) in cols.enumerated() {
                let x = 60 + CGFloat(i) * 340
                box(g, CGRect(x: x, y: 120, width: 320, height: 740), c(0xE2E8F0), radius: 16)
                text(col.0, CGPoint(x: x + 22, y: 140), 21, c(col.1), .bold)
                for j in 0..<(4 - i % 2) {
                    let r = CGRect(x: x + 16, y: 190 + CGFloat(j) * 150, width: 288, height: 130)
                    box(g, r, .white, radius: 12)
                    box(g, CGRect(x: r.minX + 18, y: r.minY + 18, width: 70, height: 10), c(col.1, 0.6), radius: 5)
                    text(["Export to PDF", "Dark mode", "Fix login bug", "Onboarding", "Search filters"][(i + j) % 5], CGPoint(x: r.minX + 18, y: r.minY + 44), 20, c(0x1E293B), .semibold)
                    text("Lorem ipsum dolor sit amet", CGPoint(x: r.minX + 18, y: r.minY + 80), 16, c(0x64748B))
                }
            }
        }
    }
}
