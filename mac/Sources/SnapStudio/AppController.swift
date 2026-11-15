import AppKit
import SwiftUI
import UserNotifications

/// Coração da app: ícone na barra de menus, atalhos globais, captura, gravação, janelas.
final class AppController: NSObject, NSApplicationDelegate, UNUserNotificationCenterDelegate {
    static let shared = AppController()

    var settings = Settings.load()
    private var statusItem: NSStatusItem!
    private var panel: TrayPanelController!
    private var windows: [NSWindowController] = []
    private var historyWC: HostWindowController?
    private var settingsWC: HostWindowController?
    private(set) var recording: ScreenRecording?
    private var capturing = false
    private(set) var lastExternalApp: NSRunningApplication?
    /// pid da app à frente no momento do pedido de captura, quando é a própria SnapStudio (editor, histórico…).
    private var activeAppPidAtRequest: pid_t?
    private(set) var lastCopied = false

    var isRecording: Bool { recording != nil }
    var statusButtonFrame: CGRect? { statusItem?.button?.window?.frame }
    private var canNotify: Bool { Bundle.main.bundleIdentifier != nil }

    // ================= arranque =================

    func applicationDidFinishLaunching(_ notification: Notification) {
        Theme.apply(settings)
        buildMainMenu()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let b = statusItem.button {
            b.image = statusImage(recording: false)
            b.imagePosition = .imageLeading
            b.toolTip = "SnapStudio"
            b.target = self
            b.action = #selector(statusClicked(_:))
            b.sendAction(on: [.leftMouseUp, .rightMouseUp])
        }

        panel = TrayPanelController()
        // teste: abrir o painel ao arrancar (--show-panel [segundos])
        let a = CommandLine.arguments
        if let i = a.firstIndex(of: "--show-panel") {
            let delay = i + 1 < a.count ? Double(a[i + 1]) ?? 0.8 : 0.8
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in self?.togglePanel() }
        }

        lastExternalApp = NSWorkspace.shared.frontmostApplication
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main) { [weak self] n in
            if let a = n.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication, a.processIdentifier != getpid() {
                self?.lastExternalApp = a
            }
        }

        if canNotify {
            UNUserNotificationCenter.current().delegate = self
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        }

        // teste: --test-overlay-focus <ficheiro> — abre o modo captura com a app em segundo plano e regista
        // se o ecrã de seleção recebe o teclado sem ativar a app (sem registar atalhos)
        if let i = CommandLine.arguments.firstIndex(of: "--test-overlay-focus"), i + 1 < CommandLine.arguments.count {
            runOverlayFocusTest(out: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
            return
        }
        registerHotkeys(notify: true)
        UpdateChecker.shared.start()

        if settings.isNew {
            settings.isNew = false
            settings.save()
            try? FileManager.default.createDirectory(at: settings.outputURL, withIntermediateDirectories: true)
            notify(S.t("A SnapStudio está pronta", "SnapStudio is ready"),
                   S.t("Prima \(settings.hotkeyRegion.display) para entrar em modo captura, ou clique no ícone na barra de menus.",
                       "Press \(settings.hotkeyRegion.display) to enter capture mode, or click the icon in the menu bar."), path: nil, force: true)
            // pedir já a autorização de gravação do ecrã
            if !Capture.hasPermission { _ = CGRequestScreenCaptureAccess() }
        }
    }

    private func runOverlayFocusTest(out: URL) {
        var log = "frontmost-before=\(NSWorkspace.shared.frontmostApplication?.localizedName ?? "?") active-before=\(NSApp.isActive)\n"
        startCapture(.region)
        DispatchQueue.main.asyncAfter(deadline: .now() + 3) {
            let key = NSApp.keyWindow
            log += "active-during=\(NSApp.isActive)\n"
            log += "key-window=\(key.map { String(describing: type(of: $0)) } ?? "nil")\n"
            log += "first-responder=\(key?.firstResponder.map { String(describing: type(of: $0)) } ?? "nil")\n"
            // Esc simulado através do mesmo caminho que o teclado usa (keyDown da vista com o foco)
            if let r = key?.firstResponder, let e = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0,
                                                                     windowNumber: key?.windowNumber ?? 0, context: nil, characters: "\u{1b}",
                                                                     charactersIgnoringModifiers: "\u{1b}", isARepeat: false, keyCode: 53) {
                r.keyDown(with: e)
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                log += "overlay-closed-after-esc=\(!NSApp.windows.contains { $0 is OverlayWindow && $0.isVisible })\n"
                log += "capturing-after=\(self.capturing)\n"
                try? log.write(to: out, atomically: true, encoding: .utf8)
                exit(0)
            }
        }
    }

    /// Abrir a app outra vez (Finder, Spotlight) mostra o histórico.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { showHistory() }
        return true
    }

    /// Ficheiros largados no ícone / "Abrir com".
    func application(_ application: NSApplication, open urls: [URL]) {
        for u in urls { openEditor(u, image: nil, compact: false) }
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if let r = recording {
            r.stop()
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { NSApp.reply(toApplicationShouldTerminate: true) }
            return .terminateLater
        }
        let editors = windows.compactMap { $0 as? EditorWindowController }
        for e in editors where e.model.compact { e.saveAndClose() }
        let dirty = editors.filter { !$0.model.compact && $0.model.modified }
        if !dirty.isEmpty {
            let a = NSAlert()
            a.messageText = S.t("Há capturas com alterações por guardar.", "Some captures have unsaved changes.")
            a.addButton(withTitle: S.t("Guardar e sair", "Save and Quit"))
            a.addButton(withTitle: S.t("Cancelar", "Cancel"))
            a.addButton(withTitle: S.t("Sair sem guardar", "Quit Without Saving"))
            NSApp.activate(ignoringOtherApps: true)
            switch a.runModal() {
            case .alertFirstButtonReturn: for e in dirty where !e.model.save() { return .terminateCancel }
            case .alertSecondButtonReturn: return .terminateCancel
            default: break
            }
        }
        return .terminateNow
    }

    // ================= barra de menus =================

    private func statusImage(recording: Bool) -> NSImage? {
        let name = recording ? "record.circle" : "camera.viewfinder"
        let img = NSImage(systemSymbolName: name, accessibilityDescription: "SnapStudio")?
            .withSymbolConfiguration(NSImage.SymbolConfiguration(pointSize: 15, weight: .medium))
        if recording {
            return img?.withSymbolConfiguration(NSImage.SymbolConfiguration(paletteColors: [.systemRed]))
        }
        img?.isTemplate = true
        return img
    }

    @objc private func statusClicked(_ sender: NSStatusBarButton) {
        let rightClick = NSApp.currentEvent?.type == .rightMouseUp || NSApp.currentEvent?.modifierFlags.contains(.control) == true
        if rightClick {
            closePanel()
            statusItem.menu = buildStatusMenu()
            statusItem.button?.performClick(nil)
            statusItem.menu = nil
            return
        }
        togglePanel()
    }

    func togglePanel() { panel.toggle(below: statusItem.button) }

    func closePanel() { panel?.hide() }

    private func buildStatusMenu() -> NSMenu {
        let m = NSMenu()
        func item(_ title: String, _ symbol: String?, _ key: Hotkey? = nil, _ action: @escaping () -> Void) -> NSMenuItem {
            let it = ClosureMenuItem(title: title, action: action)
            if let s = symbol { it.image = NSImage(systemSymbolName: s, accessibilityDescription: nil) }
            if let k = key, !k.isNone { it.setShortcutDisplay(k) }
            return it
        }
        let region = item(S.t("Modo captura (janela / região)", "Capture mode (window / region)"), "viewfinder", settings.hotkeyRegion) { [weak self] in self?.startCapture(.region) }
        region.attributedTitle = NSAttributedString(string: region.title, attributes: [.font: NSFont.menuFont(ofSize: 0).bold])
        m.addItem(region)
        m.addItem(item(S.t("Capturar todos os ecrãs", "Capture all screens"), "display", settings.hotkeyFull) { [weak self] in self?.startCapture(.fullScreen) })
        m.addItem(item(S.t("Capturar a janela ativa", "Capture active window"), "macwindow", settings.hotkeyWindow) { [weak self] in self?.startCapture(.activeWindow) })
        let delay = NSMenuItem(title: S.t("Com atraso de 3 segundos", "With a 3-second delay"), action: nil, keyEquivalent: "")
        delay.image = NSImage(systemSymbolName: "timer", accessibilityDescription: nil)
        let sub = NSMenu()
        sub.addItem(item(S.t("Modo captura", "Capture mode"), nil) { [weak self] in self?.startCapture(.region, delay: 3) })
        sub.addItem(item(S.t("Janela ativa", "Active window"), nil) { [weak self] in self?.startCapture(.activeWindow, delay: 3) })
        sub.addItem(item(S.t("Todos os ecrãs", "All screens"), nil) { [weak self] in self?.startCapture(.fullScreen, delay: 3) })
        delay.submenu = sub
        m.addItem(delay)
        m.addItem(item(isRecording ? S.t("Parar a gravação", "Stop recording") : S.t("Gravar vídeo do ecrã", "Record screen video"),
                       isRecording ? "stop.circle" : "record.circle", settings.hotkeyRecord) { [weak self] in self?.toggleRecording() })
        m.addItem(.separator())
        m.addItem(item(S.t("Histórico de capturas", "Capture history"), "clock.arrow.circlepath") { [weak self] in self?.showHistory() })
        m.addItem(item(S.t("Abrir imagem…", "Open image…"), "photo") { [weak self] in self?.openImageDialog() })
        m.addItem(item(S.t("Abrir pasta das capturas", "Open captures folder"), "folder") { [weak self] in self?.openFolder() })
        m.addItem(.separator())
        m.addItem(item(Theme.isDark ? S.t("Tema claro", "Light theme") : S.t("Tema escuro", "Dark theme"), Theme.isDark ? "sun.max" : "moon") { [weak self] in self?.toggleTheme() })
        let login = item(S.t("Iniciar ao arrancar o Mac", "Open at login"), nil) { Settings.startAtLogin.toggle() }
        login.state = Settings.startAtLogin ? .on : .off
        m.addItem(login)
        m.addItem(item(S.t("Definições…", "Settings…"), "gearshape") { [weak self] in self?.showSettings() })
        if let u = UpdateChecker.shared.pending {
            m.addItem(item(S.t("Atualizar para a versão \(u.version)…", "Update to version \(u.version)…"), "arrow.down.circle") {
                UpdateChecker.shared.showUpdateDialog(u)
            })
        } else {
            m.addItem(item(S.t("Procurar atualizações…", "Check for Updates…"), "arrow.triangle.2.circlepath") {
                UpdateChecker.shared.check(interactive: true)
            })
        }
        m.addItem(.separator())
        m.addItem(item(S.t("Sair da SnapStudio", "Quit SnapStudio"), nil) { NSApp.terminate(nil) })
        return m
    }

    func updateRecordingTitle(_ elapsed: TimeInterval?) {
        guard let b = statusItem?.button else { return }
        if let e = elapsed {
            b.title = " " + ScreenRecording.format(e)
            b.font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        } else {
            b.title = ""
        }
    }

    // ================= menu principal (atalhos de teclado nas janelas) =================

    private func buildMainMenu() {
        let main = NSMenu()
        func add(_ menu: NSMenu, _ title: String, _ sel: Selector?, _ key: String, _ mods: NSEvent.ModifierFlags = .command) {
            let it = NSMenuItem(title: title, action: sel, keyEquivalent: key)
            it.keyEquivalentModifierMask = mods
            menu.addItem(it)
        }
        func closure(_ menu: NSMenu, _ title: String, _ key: String = "", _ mods: NSEvent.ModifierFlags = .command, _ action: @escaping () -> Void) {
            let it = ClosureMenuItem(title: title, action: action)
            it.keyEquivalent = key
            it.keyEquivalentModifierMask = mods
            menu.addItem(it)
        }

        let appMenu = NSMenu()
        closure(appMenu, S.t("Acerca da SnapStudio", "About SnapStudio")) { NSApp.orderFrontStandardAboutPanel(nil); NSApp.activate(ignoringOtherApps: true) }
        closure(appMenu, S.t("Procurar atualizações…", "Check for Updates…")) { UpdateChecker.shared.check(interactive: true) }
        appMenu.addItem(.separator())
        closure(appMenu, S.t("Definições…", "Settings…"), ",") { [weak self] in self?.showSettings() }
        appMenu.addItem(.separator())
        add(appMenu, S.t("Ocultar a SnapStudio", "Hide SnapStudio"), #selector(NSApplication.hide(_:)), "h")
        add(appMenu, S.t("Sair da SnapStudio", "Quit SnapStudio"), #selector(NSApplication.terminate(_:)), "q")
        let appItem = NSMenuItem(); appItem.submenu = appMenu; main.addItem(appItem)

        let file = NSMenu(title: S.t("Ficheiro", "File"))
        closure(file, S.t("Modo captura", "Capture mode")) { [weak self] in self?.startCapture(.region) }
        closure(file, S.t("Capturar todos os ecrãs", "Capture all screens")) { [weak self] in self?.startCapture(.fullScreen) }
        closure(file, S.t("Gravar vídeo do ecrã", "Record screen video")) { [weak self] in self?.toggleRecording() }
        file.addItem(.separator())
        closure(file, S.t("Abrir imagem…", "Open image…"), "o") { [weak self] in self?.openImageDialog() }
        closure(file, S.t("Histórico de capturas", "Capture history"), "y") { [weak self] in self?.showHistory() }
        file.addItem(.separator())
        add(file, S.t("Fechar", "Close"), #selector(NSWindow.performClose(_:)), "w")
        add(file, S.t("Guardar", "Save"), #selector(CanvasView.saveDocument(_:)), "s")
        add(file, S.t("Guardar como…", "Save As…"), #selector(CanvasView.saveDocumentAs(_:)), "s", [.command, .shift])
        let fileItem = NSMenuItem(); fileItem.submenu = file; main.addItem(fileItem)

        let edit = NSMenu(title: S.t("Editar", "Edit"))
        add(edit, S.t("Anular", "Undo"), Selector(("undo:")), "z")
        add(edit, S.t("Refazer", "Redo"), Selector(("redo:")), "z", [.command, .shift])
        edit.addItem(.separator())
        add(edit, S.t("Cortar", "Cut"), #selector(NSText.cut(_:)), "x")
        add(edit, S.t("Copiar", "Copy"), #selector(NSText.copy(_:)), "c")
        add(edit, S.t("Colar", "Paste"), #selector(NSText.paste(_:)), "v")
        add(edit, S.t("Apagar", "Delete"), #selector(NSText.delete(_:)), "")
        add(edit, S.t("Selecionar tudo", "Select All"), #selector(NSText.selectAll(_:)), "a")
        let editItem = NSMenuItem(); editItem.submenu = edit; main.addItem(editItem)

        let view = NSMenu(title: S.t("Visualização", "View"))
        add(view, S.t("Ampliar", "Zoom In"), #selector(CanvasView.zoomIn(_:)), "=")
        add(view, S.t("Ampliar", "Zoom In"), #selector(CanvasView.zoomIn(_:)), "+")
        view.items.last?.isHidden = true
        view.items.last?.allowsKeyEquivalentWhenHidden = true
        add(view, S.t("Reduzir", "Zoom Out"), #selector(CanvasView.zoomOut(_:)), "-")
        add(view, S.t("Ajustar à janela", "Zoom to Fit"), #selector(CanvasView.zoomToFit(_:)), "9")
        add(view, S.t("Tamanho real", "Actual Size"), #selector(CanvasView.zoomActualSize(_:)), "0")
        view.addItem(.separator())
        add(view, S.t("Imagem anterior", "Previous Image"), #selector(CanvasView.previousImage(_:)), String(UnicodeScalar(NSLeftArrowFunctionKey)!))
        add(view, S.t("Imagem seguinte", "Next Image"), #selector(CanvasView.nextImage(_:)), String(UnicodeScalar(NSRightArrowFunctionKey)!))
        let viewItem = NSMenuItem(); viewItem.submenu = view; main.addItem(viewItem)

        let win = NSMenu(title: S.t("Janela", "Window"))
        add(win, S.t("Minimizar", "Minimize"), #selector(NSWindow.performMiniaturize(_:)), "m")
        add(win, S.t("Zoom", "Zoom"), #selector(NSWindow.performZoom(_:)), "")
        let winItem = NSMenuItem(); winItem.submenu = win; main.addItem(winItem)
        NSApp.windowsMenu = win

        NSApp.mainMenu = main
    }

    // ================= atalhos globais =================

    func registerHotkeys(notify: Bool) {
        let hk = HotKeyCenter.shared
        hk.unregisterAll()
        var failed: [String] = []
        let s = settings
        if !hk.register(id: 1, s.hotkeyRegion, action: { [weak self] in self?.startCapture(.region) }) { failed.append(s.hotkeyRegion.display) }
        if !hk.register(id: 2, s.hotkeyWindow, action: { [weak self] in self?.startCapture(.activeWindow) }) { failed.append(s.hotkeyWindow.display) }
        if !hk.register(id: 3, s.hotkeyFull, action: { [weak self] in self?.startCapture(.fullScreen) }) { failed.append(s.hotkeyFull.display) }
        if !hk.register(id: 4, s.hotkeyRecord, action: { [weak self] in self?.toggleRecording() }) { failed.append(s.hotkeyRecord.display) }
        if notify && !failed.isEmpty {
            self.notify(S.t("Atalho em uso", "Shortcut in use"),
                        S.t("Não foi possível registar: ", "Could not register: ") + failed.joined(separator: ", ") +
                        S.t(". Outra aplicação está a usá-lo. Pode mudá-lo nas Definições.", ". Another app is using it. You can change it in Settings."),
                        path: nil, force: true)
        }
    }

    func suspendHotkeys() { HotKeyCenter.shared.unregisterAll() }

    // ================= captura =================

    func startCapture(_ mode: CaptureMode, delay: Double = 0) {
        captureLog.notice("startCapture mode=\(String(describing: mode), privacy: .public) capturing=\(self.capturing) recording=\(self.recording != nil)")
        if capturing || recording != nil { return }
        let ownWindowInFront = NSApp.isActive && NSApp.windows.contains { $0.isVisible && $0.level == .normal && !($0 is NSPanel) }
        activeAppPidAtRequest = ownWindowInFront ? getpid() : nil
        capturing = true
        // um pop-up aberto guarda e fecha-se antes de capturar
        for e in windows.compactMap({ $0 as? EditorWindowController }) where e.model.compact { e.saveAndClose() }
        closePanel()
        DispatchQueue.main.asyncAfter(deadline: .now() + max(delay, 0.2)) { [weak self] in
            Task { @MainActor in await self?.doCapture(mode) }
        }
    }

    @MainActor
    private func select(_ shots: [ScreenShot], hint: String? = nil, allowAll: Bool = true) async -> SelectionResult {
        await withCheckedContinuation { c in
            RegionSelector.run(shots: shots, hint: hint, allowAll: allowAll) { c.resume(returning: $0) }
        }
    }

    @MainActor
    private func doCapture(_ mode: CaptureMode) async {
        var img: CGImage?
        var scale = NSScreen.main?.backingScaleFactor ?? 2
        do {
            switch mode {
            case .fullScreen:
                let shots = try await Capture.snapshotScreens()
                img = Capture.compose(shots)
                scale = shots.map { $0.scale }.max() ?? scale
            case .activeWindow:
                // se a SnapStudio estava à frente (editor, histórico…), é a janela dela que se captura
                img = try await Capture.captureActiveWindow(pid: activeAppPidAtRequest ?? lastExternalApp?.processIdentifier)
            case .region:
                let shots = try await Capture.snapshotScreens()
                switch await select(shots) {
                case .region(let shot, let r): img = shot.crop(r); scale = shot.scale
                case .allScreens: img = Capture.compose(shots); scale = shots.map { $0.scale }.max() ?? scale
                case .cancelled: break
                }
            }
        } catch {
            capturing = false
            if Capture.isPermissionError(error) { Capture.showPermissionAlert(); return }
            showError(S.t("Erro na captura: ", "Capture error: ") + error.localizedDescription)
            return
        }
        capturing = false
        captureLog.notice("capture result: \(img != nil ? "\(img!.width)x\(img!.height)" : "none", privacy: .public)")
        guard let image = img else { return }
        handleCapture(image, scale: scale)
    }

    private func handleCapture(_ img: CGImage, scale: CGFloat) {
        let url = settings.newCapturePath()
        do { try ImageUtil.save(img, to: url) } catch {
            showError(S.t("Erro ao guardar a captura: ", "Error saving the capture: ") + error.localizedDescription)
            return
        }
        if settings.copyToClipboard { ImageUtil.copyImage(img); lastCopied = true }
        NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
        switch settings.afterCapture {
        case "quick": openEditor(url, image: img, compact: true)
        case "full": openEditor(url, image: img, compact: false)
        default:
            notify(S.t("Captura guardada", "Capture saved"),
                   url.lastPathComponent + (settings.copyToClipboard ? S.t(" — copiada para a área de transferência", " — copied to the clipboard") : "") +
                   S.t("\nClique para editar.", "\nClick to edit."), path: url)
        }
    }

    // ================= vídeo =================

    /// Inicia a gravação (escolhendo a zona) ou, se já estiver a gravar, pára e guarda.
    func toggleRecording() {
        if let r = recording { r.stop(); return }
        if capturing { return }
        capturing = true
        for e in windows.compactMap({ $0 as? EditorWindowController }) where e.model.compact { e.saveAndClose() }
        closePanel()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { [weak self] in
            Task { @MainActor in await self?.beginRecording() }
        }
    }

    @MainActor
    private func beginRecording() async {
        let result: SelectionResult
        do {
            let shots = try await Capture.snapshotScreens()
            result = await select(shots, hint: S.t("Escolha o que gravar: clique numa janela ou arraste uma região",
                                                   "Choose what to record: click a window or drag a region"), allowAll: false)
        } catch {
            capturing = false
            if Capture.isPermissionError(error) { Capture.showPermissionAlert(); return }
            showError(S.t("Erro: ", "Error: ") + error.localizedDescription)
            return
        }
        capturing = false
        guard case .region(let shot, let rect) = result else { return }
        if rect.width * shot.scale < 64 || rect.height * shot.scale < 64 {
            notify(S.t("Zona demasiado pequena", "Area too small"), S.t("Escolha uma zona com pelo menos 64 × 64 pixels.", "Choose an area of at least 64 × 64 pixels."), path: nil, force: true)
            return
        }
        let url = settings.newVideoPath()
        let rec = ScreenRecording(screen: shot.screen, area: rect, url: url, fps: settings.videoFps, cursor: settings.videoCursor)
        rec.ended = { [weak self] r in self?.recordingEnded(r) }
        recording = rec
        statusItem.button?.image = statusImage(recording: true)
        statusItem.button?.toolTip = S.t("SnapStudio — a gravar", "SnapStudio — recording")
        NotificationCenter.default.post(name: .snapRecordingChanged, object: nil)
        rec.begin()
    }

    private func recordingEnded(_ r: ScreenRecording) {
        if recording === r { recording = nil }
        statusItem.button?.image = statusImage(recording: false)
        statusItem.button?.toolTip = "SnapStudio"
        updateRecordingTitle(nil)
        NotificationCenter.default.post(name: .snapRecordingChanged, object: nil)
        if let e = r.error {
            if Capture.isPermissionError(e) { Capture.showPermissionAlert(); return }
            showError(S.t("Não foi possível gravar o vídeo:\n\n", "Could not record the video:\n\n") + e.localizedDescription)
            return
        }
        if r.wasCancelled || !FileManager.default.fileExists(atPath: r.url.path) { return }
        if settings.copyToClipboard { ImageUtil.copyFile(r.url); lastCopied = true }
        NotificationCenter.default.post(name: .snapCapturesChanged, object: nil)
        let bytes = (try? FileManager.default.attributesOfItem(atPath: r.url.path)[.size] as? Int64) ?? 0
        let size = ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
        notify(S.t("Vídeo guardado", "Video saved"),
               r.url.lastPathComponent + "  ·  " + ScreenRecording.format(r.duration) + "  ·  " + size +
               (settings.copyToClipboard ? S.t("\nCopiado — cole com ⌘V. ", "\nCopied — paste with ⌘V. ") : "\n") +
               S.t("Clique para ver.", "Click to watch."), path: r.url, force: true)
    }

    func openVideo(_ url: URL) { NSWorkspace.shared.open(url) }

    // ================= janelas =================

    func openEditor(_ url: URL, image: CGImage?, compact: Bool) {
        if ImageUtil.isVideo(url) { openVideo(url); return }
        closePanel()
        if !compact, let ex = windows.compactMap({ $0 as? EditorWindowController })
            .first(where: { !$0.model.compact && $0.model.fileURL.standardizedFileURL == url.standardizedFileURL }) {
            ex.window?.deminiaturize(nil)
            ex.show()
            return
        }
        if compact {
            for e in windows.compactMap({ $0 as? EditorWindowController }) where e.model.compact { e.saveAndClose() }
        }
        do {
            let m = try EditorModel(url: url, initial: image, compact: compact)
            if compact { m.copied = settings.copyToClipboard && image != nil }
            EditorWindowController(model: m).show()
        } catch {
            showError(S.t("Não foi possível abrir a imagem: ", "Could not open the image: ") + error.localizedDescription)
        }
    }

    func windowOpened(_ wc: NSWindowController) {
        if !windows.contains(where: { $0 === wc }) { windows.append(wc) }
        updateActivationPolicy()
    }

    func windowClosed(_ wc: NSWindowController) {
        windows.removeAll { $0 === wc }
        if historyWC === wc { historyWC = nil }
        if settingsWC === wc { settingsWC = nil; registerHotkeys(notify: true) }
        updateActivationPolicy()
    }

    /// Com janelas normais abertas, a app aparece na Dock e em ⌘⇥; sem elas, vive só na barra de menus.
    private func updateActivationPolicy() {
        let regular = windows.contains { wc in
            if let e = wc as? EditorWindowController { return !e.model.compact }
            return true
        }
        let want: NSApplication.ActivationPolicy = regular ? .regular : .accessory
        if NSApp.activationPolicy() != want {
            NSApp.setActivationPolicy(want)
            if regular { NSApp.activate(ignoringOtherApps: true) }
        }
    }

    func openImageDialog() {
        closePanel()
        let p = NSOpenPanel()
        p.allowedContentTypes = [.png, .jpeg, .bmp, .gif, .tiff, .heic]
        p.directoryURL = settings.outputURL
        NSApp.activate(ignoringOtherApps: true)
        if p.runModal() == .OK, let u = p.url { openEditor(u, image: nil, compact: false) }
    }

    func openFolder() {
        try? FileManager.default.createDirectory(at: settings.outputURL, withIntermediateDirectories: true)
        NSWorkspace.shared.open(settings.outputURL)
    }

    func showHistory() {
        closePanel()
        if let h = historyWC { h.show(); return }
        let wc = HostWindowController(title: S.t("Histórico de capturas", "Capture history"), size: CGSize(width: 980, height: 680),
                                      root: AnyView(HistoryView()), resizable: true, autosave: "SnapStudioHistory")
        historyWC = wc
        wc.show()
    }

    func showSettings(tab: String? = nil) {
        closePanel()
        if let t = tab {
            SettingsStore.pendingTab = t
            NotificationCenter.default.post(name: .snapShowSettingsTab, object: t)
        }
        if let s = settingsWC { s.show(); return }
        let wc = HostWindowController(title: S.t("Definições da SnapStudio", "SnapStudio Settings"), size: SettingsView.size,
                                      root: AnyView(SettingsView()), resizable: false, autosave: nil)
        settingsWC = wc
        wc.show()
    }

    func toggleTheme() {
        settings.themeMode = Theme.isDark ? "light" : "dark"
        settings.save()
        Theme.apply(settings)
    }

    // ================= avisos =================

    func showError(_ text: String) {
        let a = NSAlert()
        a.messageText = "SnapStudio"
        a.informativeText = text
        a.alertStyle = .warning
        NSApp.activate(ignoringOtherApps: true)
        a.runModal()
    }

    func notify(_ title: String, _ body: String, path: URL?, force: Bool = false) {
        guard canNotify, force || settings.showNotification else { return }
        let c = UNMutableNotificationContent()
        c.title = title
        c.body = body
        if let p = path { c.userInfo = ["path": p.path] }
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: c, trigger: nil))
    }

    /// Aviso de versão nova (notificação; sem notificações, abre logo o diálogo).
    func notifyUpdate(_ r: ReleaseInfo) {
        guard canNotify else { UpdateChecker.shared.showUpdateDialog(r); return }
        let c = UNMutableNotificationContent()
        c.title = S.t("Nova versão da SnapStudio", "New SnapStudio version")
        c.body = S.t("A versão \(r.version) está disponível (tem a \(UpdateChecker.currentVersion)). Clique para ver.",
                     "Version \(r.version) is available (you have \(UpdateChecker.currentVersion)). Click to see what's new.")
        c.userInfo = ["update": r.version]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "update-\(r.version)", content: c, trigger: nil))
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        completionHandler([.banner, .list])
    }

    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        if response.notification.request.content.userInfo["update"] != nil {
            DispatchQueue.main.async {
                if let r = UpdateChecker.shared.available { UpdateChecker.shared.showUpdateDialog(r) }
            }
        } else if let p = response.notification.request.content.userInfo["path"] as? String {
            let u = URL(fileURLWithPath: p)
            DispatchQueue.main.async {
                if FileManager.default.fileExists(atPath: p) { self.openEditor(u, image: nil, compact: false) }
            }
        }
        completionHandler()
    }
}

/// Item de menu com um bloco como ação.
final class ClosureMenuItem: NSMenuItem {
    private let block: () -> Void

    init(title: String, action: @escaping () -> Void) {
        block = action
        super.init(title: title, action: #selector(run), keyEquivalent: "")
        target = self
    }
    required init(coder: NSCoder) { fatalError() }

    @objc private func run() { block() }

    /// Mostra o atalho global à direita (só informativo).
    func setShortcutDisplay(_ k: Hotkey) {
        let key = KeyNames.name(Int(k.keyCode)).lowercased()
        guard key.count == 1 else { return }
        keyEquivalent = key
        var m: NSEvent.ModifierFlags = []
        if k.modifiers & 0x1000 != 0 { m.insert(.control) }
        if k.modifiers & 0x0800 != 0 { m.insert(.option) }
        if k.modifiers & 0x0200 != 0 { m.insert(.shift) }
        if k.modifiers & 0x0100 != 0 { m.insert(.command) }
        keyEquivalentModifierMask = m
    }
}

/// Janela simples com conteúdo SwiftUI (histórico, definições).
final class HostWindowController: NSWindowController, NSWindowDelegate {
    init(title: String, size: CGSize, root: AnyView, resizable: Bool, autosave: String?) {
        var style: NSWindow.StyleMask = [.titled, .closable, .miniaturizable]
        if resizable { style.insert(.resizable) }
        let w = NSWindow(contentRect: CGRect(origin: .zero, size: size), styleMask: style, backing: .buffered, defer: false)
        w.title = title
        w.isReleasedWhenClosed = false
        w.tabbingMode = .disallowed
        w.contentView = NSHostingView(rootView: root.tint(Theme.accentColor))
        w.setContentSize(size)
        if resizable { w.minSize = CGSize(width: 640, height: 420) }
        w.center()
        if let a = autosave { w.setFrameAutosaveName(a) }
        super.init(window: w)
        w.delegate = self
    }
    required init?(coder: NSCoder) { fatalError() }

    func show() {
        AppController.shared.windowOpened(self)
        showWindow(nil)
        window?.deminiaturize(nil)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func windowWillClose(_ notification: Notification) { AppController.shared.windowClosed(self) }
}

extension NSFont {
    var bold: NSFont { NSFontManager.shared.convert(self, toHaveTrait: .boldFontMask) }
}
