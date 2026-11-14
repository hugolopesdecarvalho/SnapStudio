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
    @Published var selection: URL?
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

    // ---------- ações ----------

    func open(_ it: CaptureItem) { AppController.shared.openEditor(it.url, image: nil, compact: false) }

    func copy(_ it: CaptureItem) {
        if it.isVideo { ImageUtil.copyFile(it.url) } else if let i = ImageUtil.load(it.url) { ImageUtil.copyImage(i) }
    }

    func reveal(_ it: CaptureItem) { NSWorkspace.shared.activateFileViewerSelecting([it.url]) }

    func trash(_ it: CaptureItem) {
        try? FileManager.default.trashItem(at: it.url, resultingItemURL: nil)
        Sidecar.delete(it.url)
        if selection == it.url { selection = nil }
        reload()
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
            selection = dst
            reload()
        } catch {
            AppController.shared.showError(error.localizedDescription)
        }
    }
}

struct HistoryView: View {
    @StateObject private var m = HistoryModel()
    @StateObject private var theme = ThemeObserver()
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
                Text(S.pt ? "\(m.filtered.count) itens" : "\(m.filtered.count) items").foregroundStyle(.secondary).font(.callout)
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
                .onDeleteCommand { if let s = m.selection, let it = m.items.first(where: { $0.url == s }) { m.trash(it) } }
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
        HistoryCard(item: it, selected: m.selection == it.url,
                    onSelect: { m.selection = it.url },
                    onOpen: { m.open(it) },
                    onCopy: { m.copy(it) },
                    onReveal: { m.reveal(it) },
                    onTrash: { m.trash(it) },
                    onRename: { startRename(it) })
    }
}

struct HistoryCard: View {
    let item: CaptureItem
    let selected: Bool
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
            Button(item.isVideo ? S.t("Reproduzir", "Play") : S.t("Abrir no editor", "Open in editor"), action: onOpen)
            if !item.isVideo { Button(S.t("Abrir no editor rápido", "Open in quick editor")) { AppController.shared.openEditor(item.url, image: nil, compact: true) } }
            Button(S.t("Copiar", "Copy"), action: onCopy)
            Button(S.t("Mostrar no Finder", "Show in Finder"), action: onReveal)
            Button(S.t("Mudar o nome…", "Rename…"), action: onRename)
            Divider()
            Button(S.t("Mover para o Lixo", "Move to Trash"), role: .destructive, action: onTrash)
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
