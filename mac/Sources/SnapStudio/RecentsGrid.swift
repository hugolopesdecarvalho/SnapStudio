import AppKit
import SwiftUI

// Grelha "Recentes" do painel da barra de menus: todas as capturas da pasta (imagens e vídeos), com deslocamento
// vertical, seleção (clique, ⌘-clique, ⇧-clique, retângulo a partir de um espaço vazio), duplo clique para abrir,
// menu de contexto e apagar (botão no cabeçalho, ⌫). Substitui a antiga janela "Histórico de capturas".

/// Geometria da grelha: colunas pela largura (mín. 120 pt), cartões com proporção fixa.
/// As posições são calculadas (não medidas), por isso o retângulo de seleção apanha também cartões fora do ecrã.
struct GridLayout {
    var cols = 1
    var tileW: CGFloat = 0
    var tileH: CGFloat = 0
    var gap: CGFloat = 8
    var pad: CGFloat = 0
    var top: CGFloat = 0

    var rowStride: CGFloat { tileH + gap }

    static func make(width: CGFloat, height: CGFloat, pad: CGFloat, gap: CGFloat, top: CGFloat) -> GridLayout {
        let availW = max(0, width - 2 * pad)
        let cols = max(2, Int((availW + gap) / (120 + gap)))
        let tw = max(10, (availW - gap * CGFloat(cols - 1)) / CGFloat(cols))
        let th0 = (tw * 0.58).rounded() + 24
        let th = max(48, min(th0, height))      // painel baixo: os cartões encolhem em vez de tapar o resto
        return GridLayout(cols: cols, tileW: tw, tileH: th, gap: gap, pad: pad, top: top)
    }

    /// Retângulo do cartão `i` no conteúdo da grelha.
    func frame(_ i: Int) -> CGRect {
        let c = i % max(1, cols), r = i / max(1, cols)
        return CGRect(x: pad + CGFloat(c) * (tileW + gap), y: top + CGFloat(r) * rowStride, width: tileW, height: tileH)
    }
}

// ================= seleção e ações =================

extension TrayModel {
    func click(_ it: CaptureItem, modifiers: NSEvent.ModifierFlags) {
        if modifiers.contains(.command) {
            if selection.contains(it.url) { selection.remove(it.url) } else { selection.insert(it.url) }
            anchor = it.url
        } else if modifiers.contains(.shift), let a = anchor,
                  let i = recents.firstIndex(where: { $0.url == a }), let j = recents.firstIndex(where: { $0.url == it.url }) {
            for k in min(i, j)...max(i, j) { selection.insert(recents[k].url) }
        } else {
            selection = [it.url]
            anchor = it.url
        }
    }

    func selectAll() { selection = Set(recents.map { $0.url }) }

    func clearSelection() {
        if !selection.isEmpty { selection = [] }
        anchor = nil
    }

    var selectedItems: [CaptureItem] { recents.filter { selection.contains($0.url) } }

    /// Itens a que uma ação sobre `it` se aplica: toda a seleção, se `it` fizer parte dela; senão só `it`.
    func targets(for it: CaptureItem) -> [CaptureItem] {
        selection.contains(it.url) && selection.count > 1 ? selectedItems : [it]
    }

    // ---------- retângulo de seleção ----------

    /// `a`/`b` em coordenadas do conteúdo da grelha.
    func marqueeDragged(from a: CGPoint, to b: CGPoint, layout lay: GridLayout, viewportHeight vh: CGFloat) {
        layout = lay
        viewportHeight = vh
        if marqueeStart == nil {
            let mods = NSEvent.modifierFlags
            marqueeBase = mods.contains(.command) || mods.contains(.shift) ? selection : []
            marqueeStart = a
        }
        marqueePointer = CGPoint(x: b.x, y: b.y + scrollOffset)
        updateMarquee(to: b)
        // perto (ou para lá) do topo / fundo: deslocar automaticamente
        let edge: CGFloat = 24
        autoScroll(marqueePointer.y < edge ? -1 : (marqueePointer.y > vh - edge ? 1 : 0))
    }

    func marqueeEnded() {
        autoScroll(0)
        marqueeStart = nil
        if marquee != nil { marquee = nil }
        if let first = recents.first(where: { selection.contains($0.url) }) { anchor = first.url }
    }

    /// Chamado quando a grelha desloca (minY do conteúdo no retângulo visível).
    func scrolled(to offset: CGFloat) {
        scrollOffset = offset
        // o rato não mexeu, mas o conteúdo sim: o retângulo acompanha
        if marqueeStart != nil { updateMarquee(to: CGPoint(x: marqueePointer.x, y: marqueePointer.y - offset)) }
    }

    private func updateMarquee(to b: CGPoint) {
        guard let a = marqueeStart else { return }
        let r = CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(b.x - a.x), height: abs(b.y - a.y))
        marquee = r
        var sel = marqueeBase
        for (i, it) in recents.enumerated() where layout.frame(i).intersects(r) { sel.insert(it.url) }
        if sel != selection { selection = sel }
    }

    private func autoScroll(_ dir: Int) {
        autoScrollDir = dir
        if dir == 0 {
            autoScrollTimer?.invalidate()
            autoScrollTimer = nil
            return
        }
        guard autoScrollTimer == nil else { return }
        let t = Timer(timeInterval: 0.12, repeats: true) { [weak self] _ in self?.autoScrollStep() }
        RunLoop.main.add(t, forMode: .common)
        autoScrollTimer = t
        autoScrollStep()
    }

    private func autoScrollStep() {
        guard marqueeStart != nil, autoScrollDir != 0, layout.cols > 0, layout.rowStride > 0 else { return }
        let rows = (recents.count + layout.cols - 1) / layout.cols
        guard rows > 0 else { return }
        let top = -scrollOffset - layout.top          // topo visível, relativo à primeira linha
        if autoScrollDir < 0 {
            guard top > 0.5 else { return }
            let target = Int((top / layout.rowStride - 0.001).rounded(.up)) - 1
            scrollToRow?(max(0, target), .top)
        } else {
            let fb = (top + viewportHeight + layout.gap) / layout.rowStride
            let target = Int((fb + 0.001).rounded(.down))   // primeira linha que não se vê inteira
            if target < rows { scrollToRow?(target, .bottom) }
        }
    }

    // ---------- ações ----------

    func open(_ it: CaptureItem) { AppController.shared.openEditor(it.url, image: nil, compact: false) }

    func copy(_ items: [CaptureItem]) {
        if items.count == 1, let it = items.first {
            if it.isVideo { ImageUtil.copyFile(it.url) } else if let i = ImageUtil.load(it.url) { ImageUtil.copyImage(i) }
            return
        }
        // várias: como ficheiros (cola-se no Finder, Mail, Teams…)
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects(items.map { $0.url as NSURL })
    }

    func reveal(_ items: [CaptureItem]) {
        AppController.shared.closePanel()
        NSWorkspace.shared.activateFileViewerSelecting(items.map { $0.url })
    }

    /// Envia para o Lixo (com as anotações editáveis), depois de pedir confirmação.
    func trash(_ items: [CaptureItem], confirm: Bool = true) {
        guard !items.isEmpty else { return }
        if confirm {
            let a = NSAlert()
            a.messageText = items.count == 1
                ? S.t("Mover “\(items[0].name)” para o Lixo?", "Move “\(items[0].name)” to the Trash?")
                : S.t("Mover \(items.count) capturas para o Lixo?", "Move \(items.count) captures to the Trash?")
            a.informativeText = S.t("Pode recuperá-las no Lixo do Finder.", "You can restore them from the Trash in Finder.")
            a.addButton(withTitle: S.t("Mover para o Lixo", "Move to Trash"))
            a.addButton(withTitle: S.t("Cancelar", "Cancel"))
            a.buttons.first?.hasDestructiveAction = true
            if TrayPanelController.runAlert(a) != .alertFirstButtonReturn { return }
        }
        for it in items {
            try? FileManager.default.trashItem(at: it.url, resultingItemURL: nil)
            Sidecar.delete(it.url)
            selection.remove(it.url)
        }
        refresh()
        // outros painéis/editores abertos
        NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
    }

    func promptRename(_ it: CaptureItem) {
        let a = NSAlert()
        a.messageText = S.t("Mudar o nome", "Rename")
        a.informativeText = it.name
        let field = NSTextField(frame: CGRect(x: 0, y: 0, width: 260, height: 24))
        field.stringValue = it.url.deletingPathExtension().lastPathComponent
        a.accessoryView = field
        a.addButton(withTitle: S.t("Mudar o nome", "Rename"))
        a.addButton(withTitle: S.t("Cancelar", "Cancel"))
        a.window.initialFirstResponder = field
        if TrayPanelController.runAlert(a) == .alertFirstButtonReturn { rename(it, to: field.stringValue) }
    }

    func rename(_ it: CaptureItem, to newName: String) {
        var name = newName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, !name.contains("/") else { return }
        if (name as NSString).pathExtension.isEmpty { name += "." + it.url.pathExtension }
        let dst = it.url.deletingLastPathComponent().appendingPathComponent(name)
        if dst == it.url { return }
        if FileManager.default.fileExists(atPath: dst.path) {
            fail(S.t("Já existe um ficheiro com esse nome.", "A file with that name already exists."))
            return
        }
        do {
            try FileManager.default.moveItem(at: it.url, to: dst)
            Sidecar.move(from: it.url, to: dst)
            selection = [dst]
            anchor = dst
            refresh()
            NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
        } catch {
            fail(error.localizedDescription)
        }
    }

    private func fail(_ text: String) {
        let a = NSAlert()
        a.messageText = "SnapStudio"
        a.informativeText = text
        a.alertStyle = .warning
        TrayPanelController.runAlert(a)
    }
}

// ================= vista =================

private struct RecentsOffsetKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = nextValue() }
}

/// Grelha de recentes com deslocamento vertical; as miniaturas carregam à medida que as linhas aparecem.
/// Com poucas capturas, os lugares vazios do espaço visível ficam tracejados (como antes).
struct RecentsGrid: View {
    @ObservedObject var model: TrayModel
    let pad: CGFloat
    private let gap: CGFloat = 8
    private let topPad: CGFloat = 2

    var body: some View {
        GeometryReader { geo in
            let lay = GridLayout.make(width: geo.size.width, height: geo.size.height, pad: pad, gap: gap, top: topPad)
            let items = model.recents
            let visibleRows = max(1, Int((geo.size.height - topPad + gap) / lay.rowStride))
            let slots = max(items.count, lay.cols * visibleRows)
            let totalRows = (slots + lay.cols - 1) / lay.cols
            ScrollViewReader { proxy in
                ScrollView(.vertical) {
                    LazyVStack(alignment: .leading, spacing: gap) {
                        ForEach(0..<totalRows, id: \.self) { r in
                            HStack(spacing: gap) {
                                ForEach(0..<lay.cols, id: \.self) { c in
                                    let i = r * lay.cols + c
                                    if i < items.count {
                                        tile(items[i]).frame(width: lay.tileW, height: lay.tileH).id(items[i].url)
                                    } else if i < slots {
                                        RoundedRectangle(cornerRadius: 8)
                                            .strokeBorder(Pal.border, style: StrokeStyle(lineWidth: 1, dash: [4, 3]))
                                            .frame(width: lay.tileW, height: lay.tileH)
                                            .allowsHitTesting(false)
                                    } else {
                                        Color.clear.frame(width: lay.tileW, height: lay.tileH).allowsHitTesting(false)
                                    }
                                }
                            }
                            .id(r)
                        }
                    }
                    .padding(.horizontal, pad)
                    .padding(.top, topPad)
                    .padding(.bottom, 4)
                    .frame(maxWidth: .infinity, minHeight: geo.size.height, alignment: .topLeading)
                    // espaço vazio: clique limpa a seleção; arrastar desenha um retângulo de seleção
                    .background(
                        ZStack {
                            GeometryReader { g in
                                Color.clear.preference(key: RecentsOffsetKey.self, value: g.frame(in: .named("recentsViewport")).minY)
                            }
                            .allowsHitTesting(false)
                            Color.primary.opacity(0.0001)
                                .gesture(DragGesture(minimumDistance: 4, coordinateSpace: .named("recents"))
                                    .onChanged { model.marqueeDragged(from: $0.startLocation, to: $0.location, layout: lay, viewportHeight: geo.size.height) }
                                    .onEnded { _ in model.marqueeEnded() })
                                .onTapGesture { model.clearSelection() }
                        }
                    )
                    .overlay(alignment: .topLeading) {
                        if let r = model.marquee {
                            Rectangle()
                                .fill(Theme.accentColor.opacity(0.15))
                                .overlay(Rectangle().strokeBorder(Theme.accentColor.opacity(0.8), lineWidth: 1))
                                .frame(width: r.width, height: r.height)
                                .offset(x: r.minX, y: r.minY)
                                .allowsHitTesting(false)
                        }
                    }
                    .coordinateSpace(name: "recents")
                }
                .coordinateSpace(name: "recentsViewport")
                .onPreferenceChange(RecentsOffsetKey.self) { model.scrolled(to: $0) }
                .onAppear {
                    model.scrollToRow = { row, anchor in
                        withAnimation(.linear(duration: 0.1)) { proxy.scrollTo(row, anchor: anchor) }
                    }
                }
            }
        }
        .frame(minHeight: 60)
    }

    private func tile(_ it: CaptureItem) -> some View {
        let n = model.selection.count
        let selected = model.selection.contains(it.url)
        return ThumbTile(item: it, selected: selected, multi: selected && n > 1 ? n : 0,
                         onSelect: { model.click(it, modifiers: NSEvent.modifierFlags) },
                         onOpen: { model.open(it) },
                         onCopy: { model.copy(model.targets(for: it)) },
                         onReveal: { model.reveal(model.targets(for: it)) },
                         onRename: { model.promptRename(it) },
                         onTrash: { model.trash(model.targets(for: it)) })
    }
}

struct ThumbTile: View {
    let item: CaptureItem
    let selected: Bool
    /// Número de capturas selecionadas, quando esta faz parte de uma seleção múltipla (0 = não).
    var multi: Int = 0
    let onSelect: () -> Void
    let onOpen: () -> Void
    let onCopy: () -> Void
    let onReveal: () -> Void
    let onRename: () -> Void
    let onTrash: () -> Void
    @StateObject private var hover = Box(false)

    var body: some View {
        VStack(spacing: 0) {
            // a miniatura não pode influenciar o tamanho do cartão (capturas muito altas ou largas)
            Pal.surface2
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .overlay(alignment: .top) { ThumbView(url: item.url, size: 240) }
                .overlay { if selected { Theme.accentColor.opacity(0.18) } }
                .overlay(alignment: .topLeading) {
                    if selected {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 16))
                            .foregroundStyle(.white, Theme.accentColor)
                            .padding(5)
                    }
                }
                .clipped()
            Rectangle().fill(selected ? Theme.accentColor : Pal.border).frame(height: 1)
            Text(RelTime.short(item.date))
                .font(.system(size: 11.5)).foregroundStyle(selected ? Pal.accentSoftText : Pal.muted).lineLimit(1)
                .padding(.horizontal, 7)
                .frame(maxWidth: .infinity, alignment: .leading)
                .frame(height: 23)
                .background(selected ? Pal.accentSoft : Color.clear)
        }
        .background(Pal.surface)
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(selected || hover.value ? Theme.accentColor : Pal.border,
                                                                lineWidth: selected ? 2 : (hover.value ? 1.5 : 1)))
        .contentShape(RoundedRectangle(cornerRadius: 8))
        .onHover { hover.value = $0 }
        // um clique seleciona, dois abrem (vídeos: no leitor do sistema)
        .onTapGesture(count: 2) { onOpen() }
        .simultaneousGesture(TapGesture().onEnded { onSelect() })
        .onDrag { NSItemProvider(contentsOf: item.url) ?? NSItemProvider() }
        .contextMenu {
            if multi > 1 {
                Button(S.t("Copiar \(multi) capturas", "Copy \(multi) captures"), action: onCopy)
                Button(S.t("Mostrar \(multi) no Finder", "Show \(multi) in Finder"), action: onReveal)
                Divider()
                Button(S.t("Mover \(multi) capturas para o Lixo", "Move \(multi) captures to Trash"), role: .destructive, action: onTrash)
            } else {
                Button(item.isVideo ? S.t("Reproduzir", "Play") : S.t("Abrir no editor", "Open in editor"), action: onOpen)
                Button(S.t("Copiar", "Copy"), action: onCopy)
                Button(S.t("Mostrar no Finder", "Show in Finder"), action: onReveal)
                Button(S.t("Mudar o nome…", "Rename…"), action: onRename)
                Divider()
                Button(S.t("Mover para o Lixo", "Move to Trash"), role: .destructive, action: onTrash)
            }
        }
        .help(item.name)
    }
}
