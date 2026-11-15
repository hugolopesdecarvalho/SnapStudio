import AppKit
import SwiftUI

// ================= janela =================

final class EditorWindowController: NSWindowController, NSWindowDelegate {
    let model: EditorModel
    private var closing = false
    private var quietClose = false

    init(model: EditorModel) {
        self.model = model
        let compact = model.compact
        let win: NSWindow
        if compact {
            let qp = QuickPanel(contentRect: .zero, styleMask: [.titled, .fullSizeContentView, .closable, .resizable, .nonactivatingPanel], backing: .buffered, defer: false)
            qp.becomesKeyOnlyIfNeeded = false
            qp.hidesOnDeactivate = false
            win = qp
            win.titlebarAppearsTransparent = true
            win.titleVisibility = .hidden
            win.standardWindowButton(.miniaturizeButton)?.isHidden = true
            win.standardWindowButton(.zoomButton)?.isHidden = true
            win.standardWindowButton(.closeButton)?.isHidden = true
            win.isMovableByWindowBackground = false
            win.level = .floating
        } else {
            win = NSWindow(contentRect: .zero, styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
            win.tabbingMode = .disallowed
        }
        win.isReleasedWhenClosed = false
        super.init(window: win)
        win.delegate = self
        let root = EditorRoot(model: model, controller: self)
        win.contentView = NSHostingView(rootView: root)
        updateTitle()
        model.onClose = { [weak self] in self?.window?.close() }
        layout()
    }
    required init?(coder: NSCoder) { fatalError() }

    func updateTitle() {
        window?.title = model.title
        window?.representedURL = model.compact ? nil : model.fileURL
        window?.isDocumentEdited = model.modified
    }

    private func layout() {
        guard let win = window else { return }
        let scr = NSScreen.main ?? NSScreen.screens[0]
        let vis = scr.visibleFrame
        let img = model.pointSize
        if model.compact {
            // pop-up junto à barra de menus, do lado direito
            let w = max(560, min(720, img.width + 40)), canvasH = min(440, max(220, img.height * (w - 40) / max(1, img.width)))
            let h = canvasH + 190
            win.setContentSize(CGSize(width: w, height: h))
            win.minSize = CGSize(width: 520, height: 360)
            var anchor = CGPoint(x: vis.maxX - w - 12, y: vis.maxY - h - 8)
            if let b = AppController.shared.statusButtonFrame, b.minX < vis.maxX {
                anchor.x = max(vis.minX + 8, min(vis.maxX - w - 8, b.midX - w / 2))
            }
            win.setFrameOrigin(anchor)
        } else {
            let w = min(vis.width * 0.9, max(960, img.width + 360)), h = min(vis.height * 0.9, max(640, img.height + 180))
            win.setContentSize(CGSize(width: w, height: h))
            win.minSize = CGSize(width: 760, height: 480)
            win.center()
        }
    }

    func show() {
        AppController.shared.windowOpened(self)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
        if let c = model.canvas { window?.makeFirstResponder(c) }
        // o editor rápido é um painel não ativante (recebe o teclado sem ativar a app); o completo ativa a app
        if !model.compact { NSApp.activate() }
    }

    // ---------- fecho ----------

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        model.canvas?.commitText()
        if quietClose || model.compact || !model.modified { return true }
        let a = NSAlert()
        a.messageText = S.t("Guardar as alterações em “\(model.fileURL.lastPathComponent)”?", "Save changes to “\(model.fileURL.lastPathComponent)”?")
        a.informativeText = S.t("As anotações continuam editáveis ao reabrir a captura.", "Annotations stay editable when you reopen the capture.")
        a.addButton(withTitle: S.t("Guardar", "Save"))
        a.addButton(withTitle: S.t("Cancelar", "Cancel"))
        a.addButton(withTitle: S.t("Não guardar", "Don't Save"))
        model.dialogOpen = true
        a.beginSheetModal(for: sender) { [weak self] r in
            guard let self = self else { return }
            self.model.dialogOpen = false
            switch r {
            case .alertFirstButtonReturn: if self.model.save() { self.quietClose = true; sender.close() }
            case .alertThirdButtonReturn: self.quietClose = true; sender.close()
            default: break
            }
        }
        return false
    }

    func windowWillClose(_ notification: Notification) {
        closing = true
        model.rememberStyle()
        // o pop-up guarda sempre as alterações ao fechar
        if model.compact && model.modified && !quietClose { model.save() }
        AppController.shared.windowClosed(self)
    }

    /// Clicar fora do pop-up guarda e fecha.
    func windowDidResignKey(_ notification: Notification) {
        guard model.compact, !closing, !model.dialogOpen else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
            guard let self = self, !self.closing, !self.model.dialogOpen, let w = self.window else { return }
            if NSApp.keyWindow === w || w.attachedSheet != nil { return }
            if NSApp.modalWindow != nil { return }
            self.saveAndClose()
        }
    }

    // ---------- ações ----------

    func saveAndClose() {
        model.canvas?.commitText()
        if model.modified { model.save() }
        quietClose = true
        window?.close()
    }

    func copyAndClose() {
        model.copyToClipboard(notify: false)
        if model.modified { model.save() }
        quietClose = true
        window?.close()
    }

    func discard() {
        model.discard()
        quietClose = true
        window?.close()
    }

    func openFullEditor() {
        model.canvas?.commitText()
        if model.modified { model.save() }
        let url = model.fileURL
        quietClose = true
        window?.close()
        AppController.shared.openEditor(url, image: nil, compact: false)
    }

    func saveAs() { model.saveAs(in: window) { [weak self] in self?.updateTitle() } }

    func toggleTheme() { AppController.shared.toggleTheme() }
}

/// Editor rápido: painel "não ativante", para receber o teclado (Esc, texto, atalhos das ferramentas)
/// logo depois de uma captura feita com um atalho global, sem ter de clicar nele primeiro.
final class QuickPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
}

// ================= vistas =================

struct EditorRoot: View {
    @ObservedObject var model: EditorModel
    let controller: EditorWindowController
    @StateObject private var theme = ThemeObserver()

    var body: some View {
        Group {
            if model.compact { QuickEditorView(model: model, controller: controller) } else { FullEditorView(model: model, controller: controller) }
        }
        .tint(Theme.accentColor)
        .id(theme.tick)
        .onChange(of: model.revision) { _, _ in controller.updateTitle() }
        .onChange(of: model.fileURL) { _, _ in controller.updateTitle() }
    }
}

struct IconButton: View {
    let symbol: String
    let help: String
    var active = false
    var size: CGFloat = 30
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 15, weight: .medium))
                .frame(width: size, height: size)
                .foregroundStyle(active ? Color.white : Color.primary)
                .background(RoundedRectangle(cornerRadius: 7).fill(active ? Theme.accentColor : Color.clear))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }
}

struct Swatch: View {
    let argb: UInt32
    let selected: Bool
    var size: CGFloat = 24
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                RoundedRectangle(cornerRadius: 6).fill(Color(nsColor: ArgbColor.ns(argb)))
                RoundedRectangle(cornerRadius: 6).strokeBorder(Color.primary.opacity(0.25), lineWidth: 1)
                if selected {
                    RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.accentColor, lineWidth: 2).padding(-4)
                }
            }
            .frame(width: size, height: size)
        }
        .buttonStyle(.plain)
        .help(ArgbColor.name(argb))
    }
}

/// Botão "Arrastar": arrasta o ficheiro guardado para o Finder, Mail, Teams…
struct DragHandle: View {
    @ObservedObject var model: EditorModel

    var body: some View {
        Label(S.t("Arrastar", "Drag"), systemImage: "hand.draw")
            .padding(.horizontal, 10).padding(.vertical, 5)
            .background(RoundedRectangle(cornerRadius: 6).fill(Color.primary.opacity(0.08)))
            .help(S.t("Arraste para o Finder, Mail, Teams ou outra app", "Drag into Finder, Mail, Teams or another app"))
            .onDrag {
                if model.modified { model.save() }
                return NSItemProvider(contentsOf: model.fileURL) ?? NSItemProvider()
            }
    }
}

// ---------- editor completo ----------

struct FullEditorView: View {
    @ObservedObject var model: EditorModel
    let controller: EditorWindowController

    private let tools: [Tool] = [.select, .arrow, .rect, .text, .blur, .pixelate, .highlight, .step, .ellipse, .line, .pen, .crop]

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            HStack(spacing: 0) {
                rail
                Divider()
                CanvasRepresentable(model: model)
                Divider()
                props.frame(width: 250)
            }
            Divider()
            footer
        }
    }

    private var header: some View {
        HStack(spacing: 6) {
            IconButton(symbol: "arrow.uturn.backward", help: S.t("Anular (⌘Z)", "Undo (⌘Z)")) { model.undo() }.disabled(!model.canUndo)
            IconButton(symbol: "arrow.uturn.forward", help: S.t("Refazer (⇧⌘Z)", "Redo (⇧⌘Z)")) { model.redo() }.disabled(!model.canRedo)
            Spacer()
            DragHandle(model: model)
            Button(S.t("Guardar como…", "Save as…")) { controller.saveAs() }
            Button { model.save() } label: { Label(S.t("Guardar", "Save"), systemImage: "square.and.arrow.down") }
                .help(S.t("Guardar (⌘S)", "Save (⌘S)"))
            Button { model.copyToClipboard(notify: true) } label: { Label(S.t("Copiar", "Copy"), systemImage: "doc.on.doc") }
                .buttonStyle(.borderedProminent)
                .help(S.t("Copiar imagem (⌘C)", "Copy image (⌘C)"))
            IconButton(symbol: Theme.isDark ? "sun.max" : "moon", help: S.t("Mudar tema", "Toggle theme")) { controller.toggleTheme() }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var rail: some View {
        VStack(spacing: 4) {
            ForEach(tools, id: \.self) { t in
                IconButton(symbol: t.symbol, help: "\(t.name) (\(t.key))", active: model.tool == t, size: 34) { model.setTool(t) }
            }
            Spacer()
            IconButton(symbol: "trash", help: S.t("Apagar anotação (⌫)", "Delete annotation (⌫)"), size: 34) { model.deleteSelected() }
                .disabled(model.selectedID == nil)
        }
        .padding(8)
        .frame(width: 52)
    }

    private var sectionTitle: String {
        if let s = model.selected { return s.displayName + S.t(" selecionada", " selected") }
        return S.t("Ferramenta: ", "Tool: ") + model.tool.name
    }

    private var props: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(sectionTitle).font(.headline).lineLimit(1)
            VStack(alignment: .leading, spacing: 8) {
                Text(S.t("Cor", "Color")).font(.subheadline).foregroundStyle(.secondary)
                LazyVGrid(columns: Array(repeating: GridItem(.fixed(30), spacing: 10), count: 5), alignment: .leading, spacing: 10) {
                    ForEach(ArgbColor.fullPalette, id: \.self) { c in
                        Swatch(argb: c, selected: model.color == c, size: 26) { model.setColor(c) }
                    }
                    ColorPicker("", selection: Binding(
                        get: { Color(nsColor: ArgbColor.ns(model.color)) },
                        set: { model.setColor(ArgbColor.argb(NSColor($0))) }), supportsOpacity: false)
                        .labelsHidden()
                        .frame(width: 30)
                        .help(S.t("Outra cor…", "More colors…"))
                }
            }
            VStack(alignment: .leading, spacing: 4) {
                HStack { Text(S.t("Espessura", "Width")).font(.subheadline).foregroundStyle(.secondary); Spacer(); Text("\(Int(model.width)) px").monospacedDigit() }
                Slider(value: Binding(get: { model.width }, set: { model.setWidth($0.rounded()) }), in: 1...16)
            }
            VStack(alignment: .leading, spacing: 4) {
                HStack { Text(S.t("Tamanho do texto", "Text size")).font(.subheadline).foregroundStyle(.secondary); Spacer(); Text("\(Int(model.fontSize)) px").monospacedDigit() }
                Slider(value: Binding(get: { model.fontSize }, set: { model.setFontSize($0.rounded()) }), in: 12...80)
            }
            VStack(alignment: .leading, spacing: 4) {
                Toggle(S.t("Cantos arredondados", "Rounded corners"), isOn: Binding(get: { model.rounded }, set: { model.setRounded($0) }))
                    .toggleStyle(.switch).controlSize(.small)
                    .help(S.t("Caixas, setas, marcador, esbater e pixelizar", "Boxes, arrows, highlighter, blur and pixelate"))
                HStack { Text(S.t("Raio", "Radius")).font(.subheadline).foregroundStyle(.secondary); Spacer(); Text("\(Int(model.radius)) px").monospacedDigit() }
                    .opacity(model.rounded ? 1 : 0.45)
                Slider(value: Binding(get: { model.radius }, set: { model.setRadius($0.rounded()) }), in: 2...40)
                    .disabled(!model.rounded)
            }
            Divider()
            Text(S.t("ANOTAÇÕES", "ANNOTATIONS")).font(.caption.bold()).foregroundStyle(.secondary)
            List(selection: Binding(get: { model.selectedID }, set: { id in
                model.selectedID = id
                if id != nil && model.tool != .select { model.tool = .select }
                model.syncPropsFromSelection()
                model.changed()
            })) {
                ForEach(model.anns.reversed()) { a in
                    Label {
                        Text(a.displayName).lineLimit(1)
                    } icon: {
                        Image(systemName: a.symbol).foregroundStyle(Color(nsColor: ArgbColor.ns(a.argb)))
                    }
                    .tag(a.id as UUID?)
                }
            }
            .listStyle(.inset)
            .scrollContentBackground(.hidden)
            .overlay {
                if model.anns.isEmpty {
                    Text(S.t("Ainda sem anotações", "No annotations yet")).foregroundStyle(.secondary).font(.callout)
                }
            }
            .id(model.revision)
        }
        .padding(14)
    }

    private var footer: some View {
        HStack(spacing: 6) {
            Text(model.status).foregroundStyle(.secondary).lineLimit(1)
            Spacer()
            Button("−") { model.canvas?.zoom(by: 1 / 1.25, at: nil) }.help(S.t("Reduzir (⌘−)", "Zoom out (⌘−)"))
            Text("\(model.zoomPercent)%").monospacedDigit().frame(width: 48)
            Button("+") { model.canvas?.zoom(by: 1.25, at: nil) }.help(S.t("Ampliar (⌘+)", "Zoom in (⌘+)"))
            Button(S.t("Ajustar", "Fit")) { model.canvas?.zoomFit() }.help(S.t("Ajustar à janela (⌘9)", "Fit to window (⌘9)"))
            Button("1:1") { model.canvas?.setZoom(1, at: nil) }.help(S.t("Tamanho real (⌘0)", "Actual size (⌘0)"))
        }
        .controlSize(.small)
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
    }
}

// ---------- editor rápido (pop-up) ----------

struct QuickEditorView: View {
    @ObservedObject var model: EditorModel
    let controller: EditorWindowController
    private let tools: [Tool] = [.select, .arrow, .rect, .text, .blur, .step, .crop]

    private var captureTitle: String {
        let attrs = try? FileManager.default.attributesOfItem(atPath: model.fileURL.path)
        let d = (attrs?[.creationDate] as? Date) ?? Date()
        let f = DateFormatter(); f.dateFormat = "HH:mm"
        return S.t("Captura de ", "Capture at ") + f.string(from: d)
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: "camera.viewfinder").font(.system(size: 18)).foregroundStyle(Theme.accentColor)
                Text(captureTitle).font(.system(size: 14, weight: .bold))
                Spacer()
                IconButton(symbol: "xmark", help: S.t("Fechar (Esc) — guarda as alterações", "Close (Esc) — keeps your changes")) { controller.saveAndClose() }
            }
            .padding(.horizontal, 14).padding(.top, 12).padding(.bottom, 6)

            HStack(spacing: 4) {
                ForEach(tools, id: \.self) { t in
                    IconButton(symbol: t.symbol, help: "\(t.name) (\(t.key))", active: model.tool == t) { model.setTool(t) }
                }
                Divider().frame(height: 20).padding(.horizontal, 6)
                ForEach(ArgbColor.quickPalette, id: \.self) { c in
                    Swatch(argb: c, selected: model.color == c, size: 20) { model.setColor(c) }.padding(.horizontal, 3)
                }
                Spacer()
                IconButton(symbol: "arrow.uturn.backward", help: S.t("Anular (⌘Z)", "Undo (⌘Z)")) { model.undo() }.disabled(!model.canUndo)
            }
            .padding(.horizontal, 12).padding(.bottom, 8)

            CanvasRepresentable(model: model)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .padding(.horizontal, 12)

            HStack {
                Text(model.status).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                Spacer()
            }
            .padding(.horizontal, 14).padding(.top, 6)

            HStack(spacing: 8) {
                Button { controller.openFullEditor() } label: { Label(S.t("Editor completo", "Full editor"), systemImage: "arrow.up.left.and.arrow.down.right") }
                    .help(S.t("Abrir no editor completo", "Open in the full editor"))
                Button(role: .destructive) { controller.discard() } label: { Text(S.t("Descartar", "Discard")) }
                    .help(S.t("Enviar esta captura para o Lixo", "Move this capture to the Trash"))
                Spacer()
                Button(S.t("Guardar como…", "Save as…")) { controller.saveAs() }
                Button { controller.copyAndClose() } label: { Label(S.t("Copiar e fechar", "Copy and close"), systemImage: "doc.on.doc") }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.return, modifiers: .command)
                    .help(S.t("Copiar a imagem anotada e fechar (⌘↩)", "Copy the annotated image and close (⌘↩)"))
            }
            .controlSize(.large)
            .padding(12)
        }
        .background(.regularMaterial)
    }
}
