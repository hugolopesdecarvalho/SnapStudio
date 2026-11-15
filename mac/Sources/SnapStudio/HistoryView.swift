import AppKit
import SwiftUI

enum HistoryFilter: String, CaseIterable, Identifiable {
    case all, today, week, annotated, videos
    var id: String { rawValue }
    var title: String {
        switch self {
        case .all: return S.t("Todas", "All")
        case .today: return S.t("Hoje", "Today")
        case .week: return S.t("Esta semana", "This week")
        case .annotated: return S.t("Com anotações", "Annotated")
        case .videos: return S.t("Vídeos", "Videos")
        }
    }
}

final class HistoryModel: ObservableObject {
    @Published var items: [CaptureItem] = []
    @Published var query = ""
    @Published var filter: HistoryFilter = .all
    /// Capturas selecionadas (clique, ⌘-clique para juntar/retirar, ⇧-clique para um intervalo, ⌘A para todas).
    @Published var selection: Set<URL> = []
    private var anchor: URL?
    @Published var renaming: CaptureItem?
    @Published var newName = ""
    private var token: NSObjectProtocol?

    init() {
        reload()
        token = NotificationCenter.default.addObserver(forName: .snapCapturesChanged, object: nil, queue: .main) { [weak self] _ in self?.reload() }
    }
    deinit { if let t = token { NotificationCenter.default.removeObserver(t) } }

    func reload() { items = CaptureItem.scan(AppController.shared.settings.outputURL) }

    var filtered: [CaptureItem] {
        let cal = Calendar.current
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        return items.filter { it in
            if !q.isEmpty && !it.name.lowercased().contains(q) { return false }
            switch filter {
            case .all: return true
            case .today: return cal.isDateInToday(it.date)
            case .week: return cal.isDate(it.date, equalTo: Date(), toGranularity: .weekOfYear)
            case .annotated: return it.annotated
            case .videos: return it.isVideo
            }
        }
    }

    /// Grupos por dia: Hoje, Ontem, dia da semana, data.
    var groups: [(title: String, items: [CaptureItem])] {
        let cal = Calendar.current
        var out: [(String, [CaptureItem])] = []
        let loc = Locale(identifier: S.pt ? "pt_PT" : "en_US")
        let wd = DateFormatter(); wd.locale = loc; wd.dateFormat = "EEEE"
        let full = DateFormatter(); full.locale = loc; full.dateStyle = .long
        for it in filtered {
            let t: String
            if cal.isDateInToday(it.date) { t = S.t("Hoje", "Today") }
            else if cal.isDateInYesterday(it.date) { t = S.t("Ontem", "Yesterday") }
            else if cal.isDate(it.date, equalTo: Date(), toGranularity: .weekOfYear) { t = wd.string(from: it.date).capitalized }
            else { t = full.string(from: it.date) }
            if out.last?.0 == t { out[out.count - 1].1.append(it) } else { out.append((t, [it])) }
        }
        return out
    }

    // ---------- seleção ----------

    func click(_ it: CaptureItem, modifiers: NSEvent.ModifierFlags) {
        if modifiers.contains(.command) {
            if selection.contains(it.url) { selection.remove(it.url) } else { selection.insert(it.url) }
            anchor = it.url
        } else if modifiers.contains(.shift), let a = anchor,
                  let i = filtered.firstIndex(where: { $0.url == a }), let j = filtered.firstIndex(where: { $0.url == it.url }) {
            for k in min(i, j)...max(i, j) { selection.insert(filtered[k].url) }
        } else {
            selection = [it.url]
            anchor = it.url
        }
    }

    func selectAll() { selection = Set(filtered.map { $0.url }) }
    func clearSelection() { selection = []; anchor = nil }

    var selectedItems: [CaptureItem] { filtered.filter { selection.contains($0.url) } }

    /// Itens a que uma ação sobre `it` se aplica: toda a seleção, se `it` fizer parte dela; senão só `it`.
    func targets(for it: CaptureItem) -> [CaptureItem] {
        selection.contains(it.url) && selection.count > 1 ? selectedItems : [it]
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

    func reveal(_ items: [CaptureItem]) { NSWorkspace.shared.activateFileViewerSelecting(items.map { $0.url }) }

    /// Envia para o Lixo; com mais de uma captura pede confirmação.
    func trash(_ items: [CaptureItem]) {
        guard !items.isEmpty else { return }
        if items.count > 1 {
            let a = NSAlert()
            a.messageText = S.t("Mover \(items.count) capturas para o Lixo?", "Move \(items.count) captures to the Trash?")
            a.informativeText = S.t("Pode recuperá-las no Lixo do Finder.", "You can restore them from the Trash in Finder.")
            a.addButton(withTitle: S.t("Mover para o Lixo", "Move to Trash"))
            a.addButton(withTitle: S.t("Cancelar", "Cancel"))
            a.buttons.first?.hasDestructiveAction = true
            if a.runModal() != .alertFirstButtonReturn { return }
        }
        for it in items {
            try? FileManager.default.trashItem(at: it.url, resultingItemURL: nil)
            Sidecar.delete(it.url)
            selection.remove(it.url)
        }
        reload()
        NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
    }

    func rename(_ it: CaptureItem, to newName: String) {
        var name = newName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, !name.contains("/") else { return }
        if (name as NSString).pathExtension.isEmpty { name += "." + it.url.pathExtension }
        let dst = it.url.deletingLastPathComponent().appendingPathComponent(name)
        if dst == it.url { return }
        if FileManager.default.fileExists(atPath: dst.path) {
            AppController.shared.showError(S.t("Já existe um ficheiro com esse nome.", "A file with that name already exists."))
            return
        }
        do {
            try FileManager.default.moveItem(at: it.url, to: dst)
            Sidecar.move(from: it.url, to: dst)
            selection = [dst]
            reload()
        } catch {
            AppController.shared.showError(error.localizedDescription)
        }
    }
}

struct HistoryView: View {
    @StateObject private var m: HistoryModel
    @StateObject private var theme = ThemeObserver()

    init(model: HistoryModel? = nil) { _m = StateObject(wrappedValue: model ?? HistoryModel()) }
    @FocusState private var searchFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                HStack {
                    Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                    TextField(S.t("Pesquisar (⌘F)", "Search (⌘F)"), text: $m.query)
                        .textFieldStyle(.plain)
                        .focused($searchFocused)
                }
                .padding(.horizontal, 10).padding(.vertical, 6)
                .background(RoundedRectangle(cornerRadius: 8).fill(Color.primary.opacity(0.06)))
                .frame(maxWidth: 280)

                Picker("", selection: $m.filter) {
                    ForEach(HistoryFilter.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()

                Spacer()
                if m.selection.count > 1 {
                    Text(S.pt ? "\(m.selection.count) selecionadas" : "\(m.selection.count) selected").font(.callout)
                    Button(role: .destructive) { m.trash(m.selectedItems) } label: { Label(S.t("Mover para o Lixo", "Move to Trash"), systemImage: "trash") }
                        .help(S.t("Mover as capturas selecionadas para o Lixo (⌫)", "Move the selected captures to the Trash (⌫)"))
                    Button { m.clearSelection() } label: { Image(systemName: "xmark.circle") }
                        .buttonStyle(.plain).foregroundStyle(.secondary)
                        .help(S.t("Limpar a seleção (Esc)", "Clear the selection (Esc)"))
                } else {
                    Text(S.pt ? "\(m.filtered.count) itens" : "\(m.filtered.count) items").foregroundStyle(.secondary).font(.callout)
                }
                Button("") { m.selectAll() }.keyboardShortcut("a", modifiers: .command).frame(width: 0, height: 0).opacity(0).disabled(searchFocused)
                Button { AppController.shared.openFolder() } label: { Image(systemName: "folder") }.help(S.t("Abrir pasta das capturas", "Open captures folder"))
                Button { m.reload() } label: { Image(systemName: "arrow.clockwise") }.help(S.t("Atualizar", "Refresh"))
                Button("") { searchFocused = true }.keyboardShortcut("f", modifiers: .command).frame(width: 0, height: 0).opacity(0)
            }
            .padding(12)
            Divider()

            if m.filtered.isEmpty {
                VStack(spacing: 10) {
                    Image(systemName: "photo.on.rectangle.angled").font(.system(size: 40)).foregroundStyle(.secondary)
                    Text(m.items.isEmpty ? S.t("Ainda não há capturas.", "No captures yet.") : S.t("Nada encontrado.", "Nothing found."))
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 18, pinnedViews: [.sectionHeaders]) {
                        ForEach(m.groups, id: \.title) { g in
                            Section {
                                LazyVGrid(columns: [GridItem(.adaptive(minimum: 200, maximum: 280), spacing: 14)], spacing: 14) {
                                    ForEach(g.items) { it in card(it) }
                                }
                                .padding(.horizontal, 16)
                            } header: {
                                Text(g.title).font(.headline)
                                    .padding(.horizontal, 16).padding(.vertical, 6)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .background(.bar)
                            }
                        }
                    }
                    .padding(.vertical, 8)
                }
                .onDeleteCommand { m.trash(m.selectedItems) }
                .onExitCommand { m.clearSelection() }
            }
        }
        .id(theme.tick)
        .alert(S.t("Mudar o nome", "Rename"), isPresented: Binding(get: { m.renaming != nil }, set: { if !$0 { m.renaming = nil } })) {
            TextField(S.t("Nome", "Name"), text: $m.newName)
            Button(S.t("Mudar o nome", "Rename")) { if let r = m.renaming { m.rename(r, to: m.newName) }; m.renaming = nil }
            Button(S.t("Cancelar", "Cancel"), role: .cancel) { m.renaming = nil }
        }
    }

    private func startRename(_ it: CaptureItem) {
        m.newName = it.url.deletingPathExtension().lastPathComponent
        m.renaming = it
    }

    @ViewBuilder
    private func card(_ it: CaptureItem) -> some View {
        let many = m.selection.contains(it.url) && m.selection.count > 1
        HistoryCard(item: it, selected: m.selection.contains(it.url), multi: many ? m.selection.count : 0,
                    onSelect: { m.click(it, modifiers: NSEvent.modifierFlags) },
                    onOpen: { m.open(it) },
                    onCopy: { m.copy(m.targets(for: it)) },
                    onReveal: { m.reveal(m.targets(for: it)) },
                    onTrash: { m.trash(m.targets(for: it)) },
                    onRename: { startRename(it) })
    }
}

struct HistoryCard: View {
    let item: CaptureItem
    let selected: Bool
    /// Número de capturas selecionadas, quando esta faz parte de uma seleção múltipla (0 = não).
    var multi: Int = 0
    let onSelect: () -> Void
    let onOpen: () -> Void
    let onCopy: () -> Void
    let onReveal: () -> Void
    let onTrash: () -> Void
    let onRename: () -> Void
    @StateObject private var hoverBox = Box(false)
    private var hover: Bool { hoverBox.value }

    private var subtitle: String {
        let f = DateFormatter(); f.dateFormat = "HH:mm"
        return f.string(from: item.date) + " · " + ByteCountFormatter.string(fromByteCount: item.bytes, countStyle: .file)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ZStack(alignment: .topTrailing) {
                ThumbView(url: item.url, size: 400, contentMode: .fit)
                    .frame(height: 150)
                    .frame(maxWidth: .infinity)
                    .background(Color.primary.opacity(0.04))
                    .clipShape(RoundedRectangle(cornerRadius: 8))
                if hover {
                    HStack(spacing: 4) {
                        action("doc.on.doc", S.t("Copiar", "Copy"), onCopy)
                        if !item.isVideo { action("pencil", S.t("Editar", "Edit"), onOpen) }
                        action("folder", S.t("Mostrar no Finder", "Show in Finder"), onReveal)
                        action("trash", S.t("Mover para o Lixo", "Move to Trash"), onTrash)
                    }
                    .padding(6)
                }
                if selected {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 20))
                        .foregroundStyle(.white, Theme.accentColor)
                        .padding(6)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                }
                if item.annotated {
                    Image(systemName: "pencil.tip.crop.circle.fill")
                        .foregroundStyle(.white, Theme.accentColor)
                        .font(.system(size: 18))
                        .padding(6)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomTrailing)
                        .help(S.t("Com anotações editáveis", "Has editable annotations"))
                }
            }
            Text(item.name).font(.system(size: 12, weight: .medium)).lineLimit(1).truncationMode(.middle)
            Text(subtitle).font(.system(size: 11)).foregroundStyle(.secondary)
        }
        .padding(8)
        .background(RoundedRectangle(cornerRadius: 10).fill(selected ? Theme.accentColor.opacity(0.15) : (hover ? Color.primary.opacity(0.05) : Color.clear)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(selected ? Theme.accentColor : Color.clear, lineWidth: 1.5))
        .contentShape(Rectangle())
        .onHover { hoverBox.value = $0 }
        .onTapGesture(count: 2) { onOpen() }
        .simultaneousGesture(TapGesture().onEnded { onSelect() })
        .onDrag { NSItemProvider(contentsOf: item.url) ?? NSItemProvider() }
        .contextMenu {
            if multi > 1 {
                Button(S.pt ? "Copiar \(multi) capturas" : "Copy \(multi) captures", action: onCopy)
                Button(S.pt ? "Mostrar \(multi) no Finder" : "Show \(multi) in Finder", action: onReveal)
                Divider()
                Button(S.pt ? "Mover \(multi) capturas para o Lixo" : "Move \(multi) captures to Trash", role: .destructive, action: onTrash)
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

    private func action(_ symbol: String, _ help: String, _ f: @escaping () -> Void) -> some View {
        Button(action: f) {
            Image(systemName: symbol)
                .font(.system(size: 12, weight: .semibold))
                .frame(width: 26, height: 26)
                .background(Circle().fill(.regularMaterial))
        }
        .buttonStyle(.plain)
        .help(help)
    }
}
