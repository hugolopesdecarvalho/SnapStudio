import AppKit
import SwiftUI

final class RecordingState: ObservableObject {
    @Published var countdown: Int = 3
    @Published var elapsed: TimeInterval = 0
    @Published var paused = false
    @Published var started = false
}

/// Uma gravação em curso: moldura vermelha à volta da zona, contagem de 3 s e barra com tempo, Pausar, Parar, Cancelar.
final class ScreenRecording {
    let screen: NSScreen
    let area: CGRect               // pontos locais do ecrã
    let url: URL
    let recorder: VideoRecorder
    let state = RecordingState()
    var ended: ((ScreenRecording) -> Void)?

    private var border: NSWindow?
    private var bar: NSPanel?
    private var countdownWin: NSWindow?
    private var timer: Timer?
    private var stopping = false

    var duration: TimeInterval { recorder.elapsed }

    init(screen: NSScreen, area: CGRect, url: URL, fps: Int, cursor: Bool) {
        self.screen = screen
        self.area = area
        self.url = url
        recorder = VideoRecorder(url: url, screen: screen, area: area, fps: fps, cursor: cursor)
        recorder.onError = { [weak self] _ in self?.stop() }
    }

    var globalArea: CGRect { area.offsetBy(dx: screen.frame.minX, dy: screen.frame.minY) }

    static func format(_ t: TimeInterval) -> String {
        let s = Int(t)
        return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, (s / 60) % 60, s % 60) : String(format: "%02d:%02d", s / 60, s % 60)
    }

    func begin() {
        showBorder()
        showBar()
        showCountdown()
        var n = 3
        state.countdown = n
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] t in
            guard let self = self else { t.invalidate(); return }
            n -= 1
            if n > 0 { self.state.countdown = n; return }
            t.invalidate()
            self.countdownWin?.orderOut(nil)
            self.countdownWin = nil
            self.startRecording()
        }
    }

    private func startRecording() {
        guard !stopping else { return }
        Task { @MainActor in
            do {
                try await recorder.start()
                state.started = true
                timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
                    guard let self = self else { return }
                    self.state.elapsed = self.recorder.elapsed
                    AppController.shared.updateRecordingTitle(self.state.elapsed)
                }
            } catch {
                await recorder.stop(cancel: true)
                finish(error: error)
            }
        }
    }

    func togglePause() {
        guard state.started else { return }
        recorder.paused.toggle()
        state.paused = recorder.paused
    }

    func stop() { end(cancel: false) }
    func cancel() { end(cancel: true) }

    private func end(cancel: Bool) {
        if stopping { return }
        stopping = true
        timer?.invalidate()
        timer = nil
        teardownWindows()
        if !state.started {
            // ainda na contagem: nada gravado
            finish(error: nil, cancelled: true)
            return
        }
        Task { @MainActor in
            await recorder.stop(cancel: cancel)
            finish(error: recorder.error)
        }
    }

    private var finishedError: Error?
    private(set) var wasCancelled = false
    var error: Error? { finishedError }

    private func finish(error: Error?, cancelled: Bool? = nil) {
        teardownWindows()
        finishedError = error
        wasCancelled = cancelled ?? recorder.cancelled
        ended?(self)
    }

    private func teardownWindows() {
        border?.orderOut(nil); border = nil
        bar?.orderOut(nil); bar = nil
        countdownWin?.orderOut(nil); countdownWin = nil
    }

    // ---------- janelas ----------

    private func showBorder() {
        let r = globalArea.insetBy(dx: -4, dy: -4)
        let w = NSWindow(contentRect: r, styleMask: .borderless, backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.ignoresMouseEvents = true
        w.hasShadow = false
        w.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 1)
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        w.sharingType = .none
        w.isReleasedWhenClosed = false
        w.contentView = BorderView(frame: CGRect(origin: .zero, size: r.size))
        w.setFrame(r, display: true)
        w.orderFrontRegardless()
        border = w
    }

    private func showBar() {
        let host = NSHostingView(rootView: RecordingBar(state: state,
                                                        onPause: { [weak self] in self?.togglePause() },
                                                        onStop: { [weak self] in self?.stop() },
                                                        onCancel: { [weak self] in self?.cancel() }))
        let size = host.fittingSize
        let ga = globalArea
        let vis = screen.visibleFrame
        var origin = CGPoint(x: ga.midX - size.width / 2, y: ga.minY - size.height - 14)
        if origin.y < vis.minY + 4 { origin.y = ga.maxY + 14 }                        // por cima da zona
        if origin.y + size.height > vis.maxY - 4 { origin.y = ga.minY + 14 }          // dentro, em baixo
        origin.x = max(vis.minX + 8, min(vis.maxX - size.width - 8, origin.x))
        let p = NSPanel(contentRect: CGRect(origin: origin, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 2)
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        p.isMovableByWindowBackground = true
        p.sharingType = .none
        p.isReleasedWhenClosed = false
        p.contentView = host
        p.orderFrontRegardless()
        bar = p
    }

    private func showCountdown() {
        let size = CGSize(width: 140, height: 140)
        let ga = globalArea
        let r = CGRect(x: ga.midX - size.width / 2, y: ga.midY - size.height / 2, width: size.width, height: size.height)
        let w = NSWindow(contentRect: r, styleMask: .borderless, backing: .buffered, defer: false)
        w.isOpaque = false
        w.backgroundColor = .clear
        w.ignoresMouseEvents = true
        w.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 2)
        w.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        w.sharingType = .none
        w.isReleasedWhenClosed = false
        w.contentView = NSHostingView(rootView: CountdownView(state: state))
        w.orderFrontRegardless()
        countdownWin = w
    }
}

final class BorderView: NSView {
    override func draw(_ dirtyRect: NSRect) {
        NSColor(srgbRed: 0.9, green: 0.13, blue: 0.16, alpha: 1).setStroke()
        let p = NSBezierPath(rect: bounds.insetBy(dx: 1.5, dy: 1.5))
        p.lineWidth = 3
        p.stroke()
    }
}

struct CountdownView: View {
    @ObservedObject var state: RecordingState
    var body: some View {
        ZStack {
            Circle().fill(Color.black.opacity(0.65))
            Text("\(state.countdown)").font(.system(size: 64, weight: .bold, design: .rounded)).foregroundStyle(.white)
        }
        .frame(width: 140, height: 140)
    }
}

struct RecordingBar: View {
    @ObservedObject var state: RecordingState
    let onPause: () -> Void
    let onStop: () -> Void
    let onCancel: () -> Void

    var body: some View {
        HStack(spacing: 10) {
            Circle().fill(state.paused ? Color.orange : Color.red).frame(width: 10, height: 10)
            Text(state.started ? ScreenRecording.format(state.elapsed) : S.t("A começar…", "Starting…"))
                .font(.system(size: 13, weight: .semibold).monospacedDigit())
                .frame(minWidth: 70, alignment: .leading)
            Button { onPause() } label: {
                Label(state.paused ? S.t("Retomar", "Resume") : S.t("Pausar", "Pause"), systemImage: state.paused ? "play.fill" : "pause.fill")
            }
            .disabled(!state.started)
            Button { onStop() } label: { Label(S.t("Parar", "Stop"), systemImage: "stop.fill") }
                .buttonStyle(.borderedProminent).tint(.red)
            Button { onCancel() } label: { Image(systemName: "xmark") }
                .help(S.t("Cancelar (não guarda)", "Cancel (don't save)"))
        }
        .controlSize(.regular)
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
        .background(.regularMaterial, in: Capsule())
        .overlay(Capsule().strokeBorder(Color.primary.opacity(0.12)))
        .padding(4)
    }
}
