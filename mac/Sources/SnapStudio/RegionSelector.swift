import AppKit
import SwiftUI

enum SelectionResult {
    case region(ScreenShot, CGRect)   // retângulo em pontos locais do ecrã (origem em baixo à esquerda)
    case allScreens
    case cancelled
}

/// Modo captura: mostra o ecrã congelado e escurecido em todos os monitores.
/// Clicar numa janela escolhe-a; arrastar escolhe uma região; Enter = ecrã inteiro; A = todos os ecrãs; Esc = cancelar.
final class RegionSelector {
    private var windows: [OverlayWindow] = []
    private var completion: ((SelectionResult) -> Void)?
    private static var current: RegionSelector?
    let shots: [ScreenShot]
    let allowAll: Bool

    private init(shots: [ScreenShot], allowAll: Bool) {
        self.shots = shots
        self.allowAll = allowAll && shots.count > 1
    }

    static func run(shots: [ScreenShot], hint: String? = nil, allowAll: Bool = true, completion: @escaping (SelectionResult) -> Void) {
        let sel = RegionSelector(shots: shots, allowAll: allowAll)
        current = sel
        sel.completion = completion
        let wins = Capture.visibleWindows()
        let hintText = hint ?? S.t("Clique numa janela ou arraste para selecionar uma região", "Click a window or drag to select a region")
        let mouse = NSEvent.mouseLocation
        for shot in shots {
            let w = OverlayWindow(shot: shot)
            let local = wins.compactMap { wi -> CGRect? in
                let r = wi.frame.intersection(shot.frame)
                if r.isNull || r.width < 8 || r.height < 8 { return nil }
                return r.offsetBy(dx: -shot.frame.minX, dy: -shot.frame.minY)
            }
            let v = OverlayView(shot: shot, windows: local, owner: sel)
            w.contentView = v
            let bar = NSHostingView(rootView: SelectorBar(hint: hintText, showAll: sel.allowAll,
                                                          onFull: { [weak sel, weak v] in if let v = v { sel?.finish(.region(shot, v.bounds)) } },
                                                          onAll: { [weak sel] in sel?.finish(.allScreens) },
                                                          onCancel: { [weak sel] in sel?.finish(.cancelled) }))
            let size = bar.fittingSize
            bar.frame = CGRect(x: (shot.frame.width - size.width) / 2, y: shot.frame.height - size.height - 40, width: size.width, height: size.height)
            v.addSubview(bar)
            v.bar = bar
            sel.windows.append(w)
            w.setFrame(shot.frame, display: true)
            w.initialFirstResponder = v
            w.orderFrontRegardless()
        }
        // o ecrã debaixo do rato fica com o teclado; a vista recebe as teclas diretamente
        let key = sel.windows.first(where: { $0.frame.contains(mouse) }) ?? sel.windows.first
        key?.makeKeyAndOrderFront(nil)
        if let k = key { k.makeFirstResponder(k.contentView) }
        NSCursor.crosshair.set()
    }

    func finish(_ r: SelectionResult) {
        guard let c = completion else { return }
        completion = nil
        for w in windows { w.orderOut(nil) }
        windows.removeAll()
        NSCursor.arrow.set()
        RegionSelector.current = nil
        // deixar o ecrã redesenhar antes de continuar
        DispatchQueue.main.async { c(r) }
    }

    /// Ecrã debaixo do rato.
    func shotUnderMouse() -> ScreenShot {
        let m = NSEvent.mouseLocation
        return shots.first(where: { $0.frame.contains(m) }) ?? shots[0]
    }

    func keyDown(_ e: NSEvent) {
        switch e.keyCode {
        case 53: finish(.cancelled)                                         // Esc
        case 36, 76: let s = shotUnderMouse(); finish(.region(s, CGRect(origin: .zero, size: s.frame.size)))  // Enter
        default:
            if e.charactersIgnoringModifiers?.lowercased() == "a" {
                if allowAll { finish(.allScreens) } else {
                    let s = shotUnderMouse(); finish(.region(s, CGRect(origin: .zero, size: s.frame.size)))
                }
            }
        }
    }
}

/// Painel "não ativante": recebe o teclado (Esc, Enter, A) logo ao aparecer, sem ter de ativar a app.
/// (No macOS 14+, uma app em segundo plano não pode passar para a frente sozinha ao premir um atalho global.)
final class OverlayWindow: NSPanel {
    init(shot: ScreenShot) {
        super.init(contentRect: shot.frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        becomesKeyOnlyIfNeeded = false
        hidesOnDeactivate = false
        isFloatingPanel = true
        level = .screenSaver
        isOpaque = true
        backgroundColor = .black
        hasShadow = false
        acceptsMouseMovedEvents = true
        isReleasedWhenClosed = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        animationBehavior = .none
    }
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

final class OverlayView: NSView {
    let shot: ScreenShot
    let windowRects: [CGRect]
    weak var owner: RegionSelector?
    weak var bar: NSView?

    private var start: CGPoint?
    private var dragging = false
    private var sel: CGRect?
    private var hover: CGRect?

    init(shot: ScreenShot, windows: [CGRect], owner: RegionSelector) {
        self.shot = shot
        self.windowRects = windows
        self.owner = owner
        super.init(frame: CGRect(origin: .zero, size: shot.frame.size))
    }
    required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .activeAlways, .inVisibleRect, .cursorUpdate, .mouseEnteredAndExited], owner: self))
    }

    override func cursorUpdate(with event: NSEvent) { NSCursor.crosshair.set() }

    override func mouseEntered(with event: NSEvent) {
        // vários ecrãs: o teclado acompanha o rato
        window?.makeKey()
        window?.makeFirstResponder(self)
        NSCursor.crosshair.set()
    }

    override func mouseExited(with event: NSEvent) {
        if hover != nil { hover = nil; needsDisplay = true }
    }

    override func keyDown(with event: NSEvent) { owner?.keyDown(event) }

    override func mouseMoved(with event: NSEvent) {
        let p = convert(event.locationInWindow, from: nil)
        if let b = bar, b.frame.contains(p) { NSCursor.arrow.set() } else { NSCursor.crosshair.set() }
        let h = windowRects.first(where: { $0.contains(p) })
        if h != hover { hover = h; needsDisplay = true }
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeKey()
        start = convert(event.locationInWindow, from: nil)
        dragging = false
        sel = nil
    }

    override func mouseDragged(with event: NSEvent) {
        guard let s = start else { return }
        let p = convert(event.locationInWindow, from: nil)
        if !dragging && Geo.dist(s, p) < 4 { return }
        dragging = true
        sel = Geo.norm(s, clamp(p))
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        defer { start = nil; dragging = false }
        if dragging, let r = sel {
            if r.width >= 4 && r.height >= 4 { owner?.finish(.region(shot, r.integral)); return }
            sel = nil; needsDisplay = true
            return
        }
        // clique: a janela debaixo do rato, ou o ecrã inteiro
        let p = convert(event.locationInWindow, from: nil)
        let r = windowRects.first(where: { $0.contains(p) }) ?? bounds
        owner?.finish(.region(shot, r.intersection(bounds).integral))
    }

    private func clamp(_ p: CGPoint) -> CGPoint {
        CGPoint(x: max(0, min(bounds.width, p.x)), y: max(0, min(bounds.height, p.y)))
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let g = NSGraphicsContext.current?.cgContext else { return }
        g.interpolationQuality = .high
        g.draw(shot.image, in: bounds)
        g.setFillColor(NSColor.black.withAlphaComponent(0.47).cgColor)
        g.fill(bounds)

        guard let r = sel ?? hover else { return }
        // zona escolhida: original sem escurecer + moldura de destaque
        g.saveGState()
        g.clip(to: r)
        g.draw(shot.image, in: bounds)
        g.restoreGState()
        let accent = Theme.accent
        g.setStrokeColor(accent.cgColor)
        g.setLineWidth(2)
        g.stroke(r.insetBy(dx: -1, dy: -1))

        // tamanho em pixels
        let s = shot.scale
        let label = "\(Int((r.width * s).rounded())) × \(Int((r.height * s).rounded()))"
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .semibold), .foregroundColor: NSColor.white]
        let size = (label as NSString).size(withAttributes: attrs)
        var lp = CGPoint(x: r.minX, y: r.maxY + 6)
        if lp.y + size.height + 6 > bounds.height { lp.y = r.maxY - size.height - 12 }
        let pill = CGRect(x: lp.x, y: lp.y, width: size.width + 12, height: size.height + 6)
        accent.setFill()
        NSBezierPath(roundedRect: pill, xRadius: 5, yRadius: 5).fill()
        (label as NSString).draw(at: CGPoint(x: pill.minX + 6, y: pill.minY + 3), withAttributes: attrs)
    }
}

struct SelectorBar: View {
    let hint: String
    let showAll: Bool
    let onFull: () -> Void
    let onAll: () -> Void
    let onCancel: () -> Void

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "viewfinder").foregroundStyle(Theme.accentColor)
            Text(hint).font(.system(size: 13)).padding(.trailing, 6)
            Button(S.t("Ecrã inteiro  (↩)", "Full screen  (↩)"), action: onFull)
            if showAll { Button(S.t("Todos os ecrãs  (A)", "All screens  (A)"), action: onAll) }
            Button(S.t("Cancelar  (Esc)", "Cancel  (Esc)"), action: onCancel)
        }
        .controlSize(.regular)
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.primary.opacity(0.1)))
        .environment(\.colorScheme, .dark)
    }
}
