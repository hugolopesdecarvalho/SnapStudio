import AppKit
import SwiftUI

/// Estado do editor: imagem base, anotações, ferramenta, estilo, anular/refazer, ficheiro.
final class EditorModel: ObservableObject {
    @Published var tool: Tool = .arrow
    @Published var color: UInt32 = ArgbColor.red
    @Published var width: CGFloat = 4
    @Published var fontSize: CGFloat = 26
    @Published var rounded = false            // cantos arredondados nas novas caixas/setas/marcador/esbater
    @Published var radius: CGFloat = 12       // raio em pixels da imagem

    /// Raio a usar numa anotação nova (0 = cantos vivos).
    var newRadius: CGFloat? { rounded ? radius : nil }
    @Published private(set) var anns: [Ann] = []
    @Published var selectedID: UUID?
    @Published private(set) var modified = false
    @Published var statusOverride: String?
    @Published private(set) var canUndo = false
    @Published private(set) var canRedo = false
    @Published var zoomPercent: Int = 100
    @Published private(set) var revision = 0
    @Published private(set) var fileURL: URL

    private(set) var base: CGImage
    let scale: CGFloat           // pixels da imagem por ponto do ecrã
    let compact: Bool
    var copied = false
    let renderer = AnnRenderer()
    weak var canvas: CanvasView?
    var onClose: (() -> Void)?
    var dialogOpen = false

    // desenho em curso
    var live: Ann?
    var cropRect: CGRect?

    private struct Snap { let base: CGImage; let json: Data }
    private var undoStack: [Snap] = []
    private var redoStack: [Snap] = []
    private var pendingUndo: Snap?

    init(url: URL, initial: CGImage?, compact: Bool) throws {
        fileURL = url
        self.compact = compact
        scale = NSScreen.main?.backingScaleFactor ?? 2
        // reabrir: imagem original + anotações editáveis, se existirem
        let sb = Sidecar.base(url), sj = Sidecar.json(url)
        if FileManager.default.fileExists(atPath: sj.path), let b = ImageUtil.load(sb), let j = try? Data(contentsOf: sj) {
            base = b
            anns = Ann.deserialize(j)
        } else if let i = initial ?? ImageUtil.load(url) {
            base = i
        } else {
            throw NSError(domain: "SnapStudio", code: 3, userInfo: [NSLocalizedDescriptionKey: S.t("Formato de imagem não suportado.", "Unsupported image format.")])
        }
        loadLastStyle()
        tool = compact ? .arrow : .select
    }

    var imageSize: CGSize { CGSize(width: base.width, height: base.height) }
    var pointSize: CGSize { CGSize(width: CGFloat(base.width) / scale, height: CGFloat(base.height) / scale) }

    var selected: Ann? {
        get { anns.first(where: { $0.id == selectedID }) }
        set { selectedID = newValue?.id }
    }

    var title: String { fileURL.lastPathComponent + (modified ? " •" : "") }

    var status: String {
        if let s = statusOverride { return s }
        let dims = "\(base.width) × \(base.height)"
        if compact { return dims + (copied ? S.t(" · copiada para a área de transferência", " · copied to the clipboard") : "") }
        return (modified ? S.t("Alterações por guardar", "Unsaved changes") : S.t("Guardado", "Saved")) + " · " + dims
    }

    func changed() {
        revision &+= 1
        canvas?.needsDisplay = true
    }

    // ---------- estilo ----------

    private func loadLastStyle() {
        let st = AppController.shared.settings
        if st.editorColor != 0 { color = st.editorColor }
        if (1...16).contains(st.editorWidth) { width = CGFloat(st.editorWidth) }
        if (12...80).contains(st.editorFont) { fontSize = CGFloat(st.editorFont) }
        rounded = st.editorRounded
        if (2...40).contains(st.editorRadius) { radius = CGFloat(st.editorRadius) }
    }

    func rememberStyle() {
        let st = AppController.shared.settings
        let w = Int(width), f = Int(fontSize), r = Int(radius)
        if st.editorColor == color && st.editorWidth == w && st.editorFont == f && st.editorRounded == rounded && st.editorRadius == r { return }
        st.editorColor = color; st.editorWidth = w; st.editorFont = f
        st.editorRounded = rounded; st.editorRadius = r
        st.save()
    }

    /// Liga/desliga os cantos arredondados (e aplica à anotação selecionada).
    func setRounded(_ on: Bool) {
        rounded = on
        if let s = selected, s.isRoundable, (s.cornerRadius > 0) != on {
            pushUndo()
            s.radius = on ? radius : nil
            markModified()
        }
    }

    func setRadius(_ r: CGFloat) {
        radius = r
        if let s = selected, s.isRoundable, rounded, s.radius != r {
            pushUndo()
            s.radius = r
            markModified()
        }
    }

    func setTool(_ t: Tool) {
        canvas?.commitText()
        tool = t
        if t != .select { selectedID = nil }
        cropRect = nil
        canvas?.window?.invalidateCursorRects(for: canvas!)
        changed()
    }

    func setColor(_ c: UInt32) {
        color = c
        if canvas?.setTextColor(c) == true { return }
        if let s = selected, s.argb != c {
            pushUndo()
            s.argb = c
            markModified()
        }
    }

    func setWidth(_ w: CGFloat) {
        width = w
        if let s = selected, s.usesWidth, s.width != w {
            pushUndo()
            s.width = w
            markModified()
        }
    }

    func setFontSize(_ f: CGFloat) {
        fontSize = f
        if canvas?.setTextSize(f) == true { return }
        if let s = selected, s.usesFont, s.size != f {
            pushUndo()
            s.size = f
            markModified()
        }
    }

    func syncPropsFromSelection() {
        guard let s = selected else { return }
        color = s.argb
        if s.usesWidth { width = s.width }
        if s.usesFont { fontSize = s.size }
        if s.isRoundable {
            rounded = s.cornerRadius > 0
            if s.cornerRadius > 0 { radius = s.cornerRadius }
        }
    }

    // ---------- anotações ----------

    func hitTest(_ p: CGPoint, tol: CGFloat) -> Ann? {
        anns.last(where: { $0.hit(p, tol: tol) })
    }

    func nextStep() -> Int { (anns.filter { $0.kind == .step }.map { $0.number }.max() ?? 0) + 1 }

    func add(_ a: Ann) {
        pushUndo()
        anns.append(a)
        selectedID = a.id
        markModified()
    }

    func remove(_ a: Ann) {
        anns.removeAll { $0 === a }
        if selectedID == a.id { selectedID = nil }
    }

    func deleteSelected() {
        guard let s = selected else { return }
        pushUndo()
        remove(s)
        markModified()
    }

    func nudge(_ dx: CGFloat, _ dy: CGFloat) {
        guard let s = selected else { return }
        pushUndo()
        s.offset(dx, dy)
        markModified()
    }

    func markModified() {
        modified = true
        statusOverride = nil
        changed()
    }

    // ---------- anular / refazer ----------

    private func makeSnap() -> Snap { Snap(base: base, json: Ann.serialize(anns)) }

    func pushUndo() { pushUndo(makeSnap()) }

    private func pushUndo(_ s: Snap) {
        undoStack.append(s)
        redoStack.removeAll()
        if undoStack.count > 200 { undoStack.removeFirst(undoStack.count - 200) }
        updateUndo()
    }

    /// Início de uma edição por arrasto: o estado só entra no histórico se houver mudança.
    func beginEdit() { pendingUndo = makeSnap() }
    func commitPendingUndo() { if let p = pendingUndo { pushUndo(p); pendingUndo = nil } }
    func clearPendingUndo() { pendingUndo = nil }

    private func updateUndo() {
        canUndo = !undoStack.isEmpty
        canRedo = !redoStack.isEmpty
    }

    func undo() {
        canvas?.commitText()
        guard let s = undoStack.popLast() else { return }
        redoStack.append(makeSnap())
        restore(s)
    }

    func redo() {
        canvas?.commitText()
        guard let s = redoStack.popLast() else { return }
        undoStack.append(makeSnap())
        restore(s)
    }

    private func restore(_ s: Snap) {
        let baseChanged = s.base !== base
        base = s.base
        anns = Ann.deserialize(s.json)
        selectedID = nil
        live = nil
        updateUndo()
        markModified()
        if baseChanged { canvas?.imageSizeChanged() }
    }

    // ---------- recortar ----------

    func applyCrop(_ r: CGRect) {
        let rr = r.integral.intersection(CGRect(origin: .zero, size: imageSize))
        guard !rr.isNull, rr.width >= 2, rr.height >= 2, let nb = ImageUtil.crop(base, rr) else { return }
        pushUndo()
        for a in anns { a.offset(-rr.minX, -rr.minY) }
        base = nb
        selectedID = nil
        cropRect = nil
        markModified()
        setTool(.select)
        canvas?.imageSizeChanged()
    }

    // ---------- ficheiros ----------

    func render() -> CGImage? { renderer.render(base: base, anns: anns) }

    @discardableResult
    func save() -> Bool {
        canvas?.commitText()
        do {
            guard let img = render() else { throw NSError(domain: "SnapStudio", code: 4) }
            try ImageUtil.save(img, to: fileURL)
            saveSidecar()
            modified = false
            statusOverride = nil
            changed()
            NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
            return true
        } catch {
            alert(S.t("Não foi possível guardar:\n", "Could not save:\n") + error.localizedDescription)
            return false
        }
    }

    private func saveSidecar() {
        if anns.isEmpty { Sidecar.delete(fileURL); return }
        Sidecar.ensureDir()
        try? ImageUtil.pngData(base)?.write(to: Sidecar.base(fileURL), options: .atomic)
        try? Ann.serialize(anns).write(to: Sidecar.json(fileURL), options: .atomic)
    }

    func saveAs(in window: NSWindow?, then: (() -> Void)? = nil) {
        canvas?.commitText()
        let p = NSSavePanel()
        p.allowedContentTypes = [.png, .jpeg, .bmp, .tiff]
        p.nameFieldStringValue = fileURL.lastPathComponent
        p.directoryURL = fileURL.deletingLastPathComponent()
        p.isExtensionHidden = false
        dialogOpen = true
        NSApp.activate()      // o diálogo precisa do teclado (o editor rápido não ativa a app)
        let done: (NSApplication.ModalResponse) -> Void = { [weak self] r in
            guard let self = self else { return }
            self.dialogOpen = false
            if r == .OK, let u = p.url {
                self.fileURL = u
                if self.save() { then?() }
            }
        }
        if let w = window { p.beginSheetModal(for: w, completionHandler: done) } else { done(p.runModal()) }
    }

    func copyToClipboard(notify: Bool) {
        canvas?.commitText()
        guard let img = render() else { return }
        ImageUtil.copyImage(img)
        copied = true
        if notify { statusOverride = S.t("Imagem copiada para a área de transferência", "Image copied to the clipboard") }
        changed()
    }

    /// Envia a captura para o Lixo.
    func discard() {
        dialogOpen = true
        try? FileManager.default.trashItem(at: fileURL, resultingItemURL: nil)
        Sidecar.delete(fileURL)
        modified = false
        NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
    }

    func alert(_ text: String) {
        dialogOpen = true
        let a = NSAlert()
        a.messageText = "SnapStudio"
        a.informativeText = text
        a.alertStyle = .warning
        NSApp.activate()
        a.runModal()
        dialogOpen = false
    }
}
