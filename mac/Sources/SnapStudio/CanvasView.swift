import AppKit
import SwiftUI

/// Tela do editor. Coordenadas da vista em pontos (y para baixo); anotações em pixels da imagem.
final class CanvasView: NSView, NSTextViewDelegate, NSMenuItemValidation {
    let model: EditorModel

    private enum Drag { case none, create, move, handle1, handle2, crop }
    private var drag = Drag.none
    private var dragStart = CGPoint.zero
    private var lastPt = CGPoint.zero
    private var moved = false

    // edição de texto
    private var textView: AnnTextView?
    private var editingText: Ann?
    private var textPos = CGPoint.zero
    private var textSize: CGFloat = 26
    private var textColor: UInt32 = ArgbColor.red

    init(model: EditorModel) {
        self.model = model
        super.init(frame: CGRect(origin: .zero, size: model.pointSize))
        model.canvas = self
    }
    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    var scrollView: NSScrollView? { enclosingScrollView }
    var magnification: CGFloat { scrollView?.magnification ?? 1 }
    var scale: CGFloat { model.scale }

    /// Tolerância de clique em pixels da imagem (6 pt no ecrã).
    var tol: CGFloat { 6 / magnification * scale }

    func toImg(_ e: NSEvent) -> CGPoint {
        let p = convert(e.locationInWindow, from: nil)
        return CGPoint(x: p.x * scale, y: p.y * scale)
    }

    func imageSizeChanged() {
        setFrameSize(model.pointSize)
        zoomFit()
        needsDisplay = true
    }

    // ================= desenho =================

    override func draw(_ dirtyRect: NSRect) {
        guard let g = NSGraphicsContext.current?.cgContext else { return }
        g.saveGState()
        g.scaleBy(x: 1 / scale, y: 1 / scale)
        g.interpolationQuality = magnification * scale >= 2 ? .none : .high
        let full = CGRect(origin: .zero, size: model.imageSize)
        g.drawUpright(model.base, in: full)
        g.interpolationQuality = .high
        for a in model.anns where !(a === editingText) {
            model.renderer.draw(a, in: g, base: model.base)
        }
        if let l = model.live, !model.anns.contains(where: { $0 === l }) { model.renderer.draw(l, in: g, base: model.base) }

        // seleção
        if let s = model.selected, model.tool == .select, editingText == nil { drawSelection(g, s) }

        // recorte
        if let c = model.cropRect {
            g.setFillColor(NSColor.black.withAlphaComponent(0.5).cgColor)
            g.addRect(full); g.addRect(c)
            g.fillPath(using: .evenOdd)
            g.setStrokeColor(NSColor.white.cgColor)
            g.setLineWidth(1.5 / magnification * scale)
            g.setLineDash(phase: 0, lengths: [6 / magnification * scale, 4 / magnification * scale])
            g.stroke(c)
        }
        g.restoreGState()
    }

    private func drawSelection(_ g: CGContext, _ s: Ann) {
        let px = scale / magnification
        g.saveGState()
        g.setStrokeColor(Theme.accent.cgColor)
        g.setLineWidth(1.25 * px)
        if s.isShape {
            if s.isLinear {
                drawHandle(g, s.p1, px); drawHandle(g, s.p2, px)
            } else {
                g.setLineDash(phase: 0, lengths: [4 * px, 3 * px])
                g.stroke(Geo.norm(s.p1, s.p2).insetBy(dx: -3 * px, dy: -3 * px))
                g.setLineDash(phase: 0, lengths: [])
                drawHandle(g, s.p1, px); drawHandle(g, s.p2, px)
            }
        } else {
            g.setLineDash(phase: 0, lengths: [4 * px, 3 * px])
            g.stroke(s.bounds.insetBy(dx: -2 * px, dy: -2 * px))
        }
        g.restoreGState()
    }

    private func drawHandle(_ g: CGContext, _ p: CGPoint, _ px: CGFloat) {
        let r = 5 * px
        let rect = CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r)
        g.setFillColor(NSColor.white.cgColor)
        g.fillEllipse(in: rect)
        g.setLineDash(phase: 0, lengths: [])
        g.setStrokeColor(Theme.accent.cgColor)
        g.setLineWidth(1.5 * px)
        g.strokeEllipse(in: rect)
    }

    // ================= cursor =================

    override func resetCursorRects() {
        let c: NSCursor
        switch model.tool {
        case .select: c = .arrow
        case .text: c = .iBeam
        default: c = .crosshair
        }
        addCursorRect(visibleRect, cursor: c)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: .zero, options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect], owner: self))
    }

    override func mouseMoved(with event: NSEvent) {
        guard model.tool == .select, drag == .none else { return }
        let p = toImg(event)
        if let s = model.selected, s.isShape, Geo.dist(p, s.p1) <= tol * 1.5 || Geo.dist(p, s.p2) <= tol * 1.5 {
            NSCursor.crosshair.set()
        } else if model.hitTest(p, tol: tol) != nil {
            NSCursor.openHand.set()
        } else {
            NSCursor.arrow.set()
        }
    }

    // ================= rato =================

    override func mouseDown(with event: NSEvent) {
        if textView != nil { commitText(); return }
        window?.makeFirstResponder(self)
        let p = toImg(event)
        dragStart = p; lastPt = p; moved = false

        if event.clickCount == 2, model.tool == .select, let t = model.hitTest(p, tol: tol), t.kind == .text {
            beginTextEdit(t, at: t.p1)
            return
        }

        switch model.tool {
        case .select:
            if let s = model.selected, s.isShape {
                if Geo.dist(p, s.p1) <= tol * 1.5 { model.beginEdit(); model.live = s; drag = .handle1; needsDisplay = true; return }
                if Geo.dist(p, s.p2) <= tol * 1.5 { model.beginEdit(); model.live = s; drag = .handle2; needsDisplay = true; return }
            }
            let hit = model.hitTest(p, tol: tol)
            model.selectedID = hit?.id
            if hit != nil { model.beginEdit(); drag = .move; NSCursor.closedHand.set() }
            model.syncPropsFromSelection()
            model.changed()
        case .text:
            let t = model.hitTest(p, tol: tol)
            beginTextEdit(t?.kind == .text ? t : nil, at: p)
        case .step:
            let st = Ann(kind: .step, argb: model.color)
            st.p1 = p
            st.number = model.nextStep()
            st.size = model.fontSize
            model.add(st)
        case .pen:
            let pa = Ann(kind: .pen, argb: model.color)
            pa.width = model.width
            pa.pts = [p.x, p.y]
            model.live = pa
            drag = .create
            needsDisplay = true
        case .crop:
            drag = .crop
            model.cropRect = CGRect(origin: p, size: .zero)
        default:
            guard let k = model.tool.shapeKind else { return }
            let sa = Ann(kind: k, argb: model.tool == .highlight ? ArgbColor.highlight : model.color)
            sa.p1 = p; sa.p2 = p
            sa.width = model.width
            if sa.isRoundable { sa.radius = model.newRadius }
            model.live = sa
            drag = .create
            needsDisplay = true
        }
    }

    private func clampToImage(_ p: CGPoint) -> CGPoint {
        CGPoint(x: max(0, min(model.imageSize.width, p.x)), y: max(0, min(model.imageSize.height, p.y)))
    }

    override func mouseDragged(with event: NSEvent) {
        autoscroll(with: event)
        let p = toImg(event)
        let shift = event.modifierFlags.contains(.shift)
        switch drag {
        case .none: return
        case .create:
            guard let l = model.live else { return }
            if l.kind == .pen {
                let n = l.pts.count
                if Geo.dist(p, CGPoint(x: l.pts[n - 2], y: l.pts[n - 1])) > 1.2 * scale / magnification { l.pts += [p.x, p.y] }
            } else {
                l.p2 = shift ? Geo.constrain(l.p1, p, linear: l.isLinear) : p
            }
        case .move:
            guard let s = model.selected else { return }
            if !moved && Geo.dist(p, dragStart) / scale * magnification < 2 { return }
            if !moved { moved = true; model.commitPendingUndo() }
            s.offset(p.x - lastPt.x, p.y - lastPt.y)
            lastPt = p
        case .handle1, .handle2:
            guard let s = model.selected else { return }
            if !moved { moved = true; model.commitPendingUndo() }
            let other = drag == .handle1 ? s.p2 : s.p1
            let q = shift ? Geo.constrain(other, p, linear: s.isLinear) : p
            if drag == .handle1 { s.p1 = q } else { s.p2 = q }
        case .crop:
            model.cropRect = Geo.norm(clampToImage(dragStart), clampToImage(p))
        }
        model.changed()
    }

    override func mouseUp(with event: NSEvent) {
        switch drag {
        case .none: break
        case .create:
            if let a = model.live {
                model.live = nil
                var valid = true
                if a.isShape {
                    let w = abs(a.x2 - a.x1) / scale * magnification, h = abs(a.y2 - a.y1) / scale * magnification
                    valid = a.isLinear ? (w + h) >= 6 : (w >= 4 && h >= 4)
                }
                if valid {
                    model.add(a)
                    // depois de colocar um elemento, volta ao ponteiro (com o elemento selecionado);
                    // o desenho livre mantém-se para traçar vários seguidos
                    if a.kind != .pen { model.setTool(.select) }
                }
            }
        case .move, .handle1, .handle2:
            model.live = nil
            if moved { model.markModified() }
            if model.tool == .select { NSCursor.arrow.set() }
        case .crop:
            drag = .none
            if let c = model.cropRect, c.width >= 4, c.height >= 4 { model.applyCrop(c) } else { model.cropRect = nil }
        }
        drag = .none
        model.clearPendingUndo()
        model.changed()
    }

    override func scrollWheel(with event: NSEvent) {
        if event.modifierFlags.contains(.command) || event.modifierFlags.contains(.control) {
            let f: CGFloat = event.scrollingDeltaY > 0 ? 1.1 : event.scrollingDeltaY < 0 ? 1 / 1.1 : 1
            zoom(by: f, at: convert(event.locationInWindow, from: nil))
        } else {
            super.scrollWheel(with: event)
        }
    }

    override func magnify(with event: NSEvent) {
        zoom(by: 1 + event.magnification, at: convert(event.locationInWindow, from: nil))
    }

    // ================= teclado =================

    override func keyDown(with event: NSEvent) {
        let mods = event.modifierFlags.intersection([.command, .control, .option])
        let step: CGFloat = (event.modifierFlags.contains(.shift) ? 10 : 1) * scale
        switch event.keyCode {
        case 51, 117: model.deleteSelected(); return                     // Delete
        case 53:                                                         // Esc
            if model.cropRect != nil || model.live != nil { model.cropRect = nil; model.live = nil; drag = .none; model.changed(); return }
            if model.compact { (window?.windowController as? EditorWindowController)?.saveAndClose(); return }
            if model.selectedID != nil { model.selectedID = nil; model.changed(); return }
            return
        case 123: if model.selected != nil { model.nudge(-step, 0); return }
        case 124: if model.selected != nil { model.nudge(step, 0); return }
        case 125: if model.selected != nil { model.nudge(0, step); return }
        case 126: if model.selected != nil { model.nudge(0, -step); return }
        default: break
        }
        if mods.isEmpty, let ch = event.charactersIgnoringModifiers?.uppercased(),
           let t = Tool.allCases.first(where: { $0.key == ch }) {
            model.setTool(t)
            return
        }
        super.keyDown(with: event)
    }

    // ================= comandos do menu =================

    @objc func undo(_ sender: Any?) { model.undo() }
    @objc func redo(_ sender: Any?) { model.redo() }
    @objc func copy(_ sender: Any?) { model.copyToClipboard(notify: true) }
    @objc func delete(_ sender: Any?) { model.deleteSelected() }
    @objc func saveDocument(_ sender: Any?) { model.save() }
    @objc func saveDocumentAs(_ sender: Any?) { model.saveAs(in: window) }
    @objc func zoomIn(_ sender: Any?) { zoom(by: 1.25, at: nil) }
    @objc func zoomOut(_ sender: Any?) { zoom(by: 1 / 1.25, at: nil) }
    @objc func zoomToFit(_ sender: Any?) { zoomFit() }
    @objc func zoomActualSize(_ sender: Any?) { setZoom(1, at: nil) }

    func validateMenuItem(_ item: NSMenuItem) -> Bool {
        switch item.action {
        case #selector(undo(_:)): return model.canUndo
        case #selector(redo(_:)): return model.canRedo
        case #selector(delete(_:)): return model.selected != nil
        default: return true
        }
    }

    // ================= zoom =================

    /// Enquanto true, a imagem acompanha o tamanho da janela (até o utilizador mudar o zoom).
    var fitMode = true

    func containerResized() {
        if fitMode { zoomFit() }
    }

    func setZoom(_ z: CGFloat, at anchor: CGPoint?) {
        fitMode = false
        guard let sv = scrollView else { return }
        let m = max(sv.minMagnification, min(sv.maxMagnification, z))
        if let a = anchor { sv.setMagnification(m, centeredAt: a) } else {
            let c = CGPoint(x: visibleRect.midX, y: visibleRect.midY)
            sv.setMagnification(m, centeredAt: c)
        }
        zoomChanged()
    }

    func zoom(by f: CGFloat, at anchor: CGPoint?) { setZoom(magnification * f, at: anchor) }

    func zoomFit() {
        fitMode = true
        guard let sv = scrollView else { return }
        let avail = sv.contentSize
        let size = model.pointSize
        guard size.width > 0, size.height > 0, avail.width > 40, avail.height > 40 else { return }
        let m = min(1, min((avail.width - 24) / size.width, (avail.height - 24) / size.height))
        sv.magnification = max(sv.minMagnification, m)
        zoomChanged()
    }

    func zoomChanged() {
        model.zoomPercent = Int((magnification * 100).rounded())
        textView.map { positionTextView($0) }
        needsDisplay = true
    }

    // ================= texto =================

    func beginTextEdit(_ existing: Ann?, at p: CGPoint) {
        commitText()
        editingText = existing
        textSize = existing?.size ?? model.fontSize
        textColor = existing?.argb ?? model.color
        textPos = existing?.p1 ?? p

        let tv = AnnTextView(frame: .zero)
        tv.delegate = self
        tv.isRichText = false
        tv.allowsUndo = true
        tv.drawsBackground = true
        tv.isHorizontallyResizable = true
        tv.isVerticallyResizable = true
        tv.textContainer?.widthTracksTextView = false
        tv.textContainer?.containerSize = CGSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        tv.textContainer?.lineFragmentPadding = 0
        tv.textContainerInset = CGSize(width: 3, height: 2)
        tv.wantsLayer = true
        tv.layer?.borderWidth = 1
        tv.layer?.borderColor = Theme.accent.cgColor
        tv.onCommit = { [weak self] in self?.commitText(); self?.window?.makeFirstResponder(self) }
        tv.onCancel = { [weak self] in self?.cancelText() }
        tv.string = existing?.text ?? ""
        textView = tv
        applyTextStyle()
        addSubview(tv)
        window?.makeFirstResponder(tv)
        tv.selectAll(nil)
        model.changed()
    }

    private func applyTextStyle() {
        guard let tv = textView else { return }
        tv.font = NSFont.systemFont(ofSize: max(4, textSize / scale), weight: .bold)
        tv.textColor = ArgbColor.ns(textColor)
        tv.insertionPointColor = ArgbColor.ns(textColor)
        tv.backgroundColor = ArgbColor.contrast(textColor) == ArgbColor.rgb(255, 255, 255)
            ? NSColor.white.withAlphaComponent(0.92) : NSColor(white: 0.24, alpha: 0.92)
        positionTextView(tv)
    }

    private func positionTextView(_ tv: AnnTextView) {
        guard let lm = tv.layoutManager, let tc = tv.textContainer else { return }
        lm.ensureLayout(for: tc)
        let used = lm.usedRect(for: tc)
        let wTail = ("W" as NSString).size(withAttributes: [.font: tv.font as Any]).width
        let size = CGSize(width: ceil(used.width + wTail + 8), height: ceil(max(used.height, tv.font?.boundingRectForFont.height ?? 20) + 6))
        // o texto desenha a partir de (x,y) em pixels; o NSTextView tem margem interior de 3×2 pt
        tv.frame = CGRect(x: textPos.x / scale - 3, y: textPos.y / scale - 2, width: size.width, height: size.height)
    }

    func textDidChange(_ notification: Notification) {
        if let tv = textView { positionTextView(tv) }
    }

    func textDidEndEditing(_ notification: Notification) { commitText() }

    /// Muda a cor do texto em edição. Devolve true se havia texto em edição.
    func setTextColor(_ c: UInt32) -> Bool {
        guard textView != nil else { return false }
        textColor = c
        applyTextStyle()
        return true
    }

    func setTextSize(_ s: CGFloat) -> Bool {
        guard textView != nil else { return false }
        textSize = s
        applyTextStyle()
        return true
    }

    func cancelText() {
        guard let tv = textView else { return }
        tv.string = editingText?.text ?? ""
        commitText()
        window?.makeFirstResponder(self)
    }

    func commitText() {
        guard let tv = textView else { return }
        textView = nil
        let txt = tv.string.replacingOccurrences(of: "\r\n", with: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
        tv.delegate = nil
        tv.removeFromSuperview()
        if let e = editingText {
            if txt != e.text || e.size != textSize || e.argb != textColor {
                model.pushUndo()
                if txt.isEmpty { model.remove(e) } else { e.text = txt; e.size = textSize; e.argb = textColor }
                model.markModified()
            }
        } else if !txt.isEmpty {
            let t = Ann(kind: .text, argb: textColor)
            t.p1 = textPos
            t.text = txt
            t.size = textSize
            model.add(t)
        }
        editingText = nil
        // texto terminado: volta ao ponteiro (se commitText vier de setTool(x), o setTool exterior define x a seguir)
        if model.tool == .text { model.setTool(.select) }
        model.changed()
    }
}

final class AnnTextView: NSTextView {
    var onCommit: (() -> Void)?
    var onCancel: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        if (event.keyCode == 36 || event.keyCode == 76) && !event.modifierFlags.contains(.shift) { onCommit?(); return }
        if event.keyCode == 53 { onCancel?(); return }
        super.keyDown(with: event)
    }
}

/// Mantém a imagem centrada quando é mais pequena do que a janela.
final class CenteringClipView: NSClipView {
    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        var r = super.constrainBoundsRect(proposedBounds)
        guard let doc = documentView else { return r }
        let d = doc.frame
        if r.width > d.width { r.origin.x = (d.width - r.width) / 2 }
        if r.height > d.height { r.origin.y = (d.height - r.height) / 2 }
        return r
    }
}

/// Envolve a tela num NSScrollView com zoom, para usar em SwiftUI.
struct CanvasRepresentable: NSViewRepresentable {
    let model: EditorModel

    func makeNSView(context: Context) -> NSScrollView {
        let sv = NSScrollView()
        sv.contentView = CenteringClipView()
        sv.hasVerticalScroller = true
        sv.hasHorizontalScroller = true
        sv.autohidesScrollers = true
        sv.allowsMagnification = true
        sv.minMagnification = 0.05
        sv.maxMagnification = 16
        sv.drawsBackground = true
        sv.backgroundColor = .underPageBackgroundColor
        sv.borderType = .noBorder
        let canvas = CanvasView(model: model)
        sv.documentView = canvas
        NotificationCenter.default.addObserver(forName: NSScrollView.didEndLiveMagnifyNotification, object: sv, queue: .main) { _ in
            canvas.fitMode = false
            canvas.zoomChanged()
        }
        sv.postsFrameChangedNotifications = true
        NotificationCenter.default.addObserver(forName: NSView.frameDidChangeNotification, object: sv, queue: .main) { [weak canvas] _ in
            canvas?.containerResized()
        }
        DispatchQueue.main.async {
            canvas.zoomFit()
            canvas.window?.makeFirstResponder(canvas)
        }
        return sv
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {}
}
