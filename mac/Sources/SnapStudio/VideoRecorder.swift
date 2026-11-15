import AppKit
import AVFoundation
import ScreenCaptureKit

/// Grava uma zona de um ecrã para MP4 (H.264) com ScreenCaptureKit + AVAssetWriter.
/// As janelas da própria SnapStudio (moldura, barra) ficam fora do vídeo.
final class VideoRecorder: NSObject, SCStreamOutput, SCStreamDelegate {
    let url: URL
    let screen: NSScreen
    let area: CGRect          // pontos locais do ecrã (origem em baixo à esquerda)
    let fps: Int
    let drawCursor: Bool
    let outputSize: CGSize

    private(set) var error: Error?
    private(set) var cancelled = false
    private(set) var framesWritten = 0
    var onError: ((Error) -> Void)?

    private var stream: SCStream?
    private var writer: AVAssetWriter?
    private var input: AVAssetWriterInput?
    private var adaptor: AVAssetWriterInputPixelBufferAdaptor?
    private let queue = DispatchQueue(label: "SnapStudio.video")
    private let lock = NSLock()

    // tempo (relógio do sistema, o mesmo das amostras do ScreenCaptureKit)
    private var firstPTS: CMTime?
    private var startedAt = CMTime.invalid
    private var pausedTotal = CMTime.zero
    private var pauseStart: CMTime?
    private var lastBuffer: CVPixelBuffer?
    private var lastTime = CMTime.zero
    private var finished = false

    init(url: URL, screen: NSScreen, area: CGRect, fps: Int, cursor: Bool) {
        self.url = url
        self.screen = screen
        self.area = area
        self.fps = max(5, min(60, fps))
        self.drawCursor = cursor
        let scale = screen.backingScaleFactor
        // H.264: dimensões pares e dentro dos limites do codificador
        let sc = min(1.0, min(3840.0 / (area.width * scale), 2160.0 / (area.height * scale)))
        let w = max(2, Int(area.width * scale * sc) & ~1), h = max(2, Int(area.height * scale * sc) & ~1)
        outputSize = CGSize(width: w, height: h)
        super.init()
    }

    private static var now: CMTime { CMClockGetTime(CMClockGetHostTimeClock()) }

    /// Tempo gravado (sem as pausas).
    var elapsed: TimeInterval {
        lock.lock(); defer { lock.unlock() }
        guard startedAt.isValid else { return 0 }
        var t = CMTimeSubtract(CMTimeSubtract(VideoRecorder.now, startedAt), pausedTotal)
        if let p = pauseStart { t = CMTimeSubtract(t, CMTimeSubtract(VideoRecorder.now, p)) }
        return max(0, CMTimeGetSeconds(t))
    }

    var paused: Bool {
        get { lock.lock(); defer { lock.unlock() }; return pauseStart != nil }
        set {
            lock.lock(); defer { lock.unlock() }
            if newValue, pauseStart == nil { pauseStart = VideoRecorder.now }
            else if !newValue, let p = pauseStart {
                pausedTotal = CMTimeAdd(pausedTotal, CMTimeSubtract(VideoRecorder.now, p))
                pauseStart = nil
            }
        }
    }

    func start() async throws {
        let content = try await Capture.shareable()
        let did = Capture.displayID(screen)
        guard let display = content.displays.first(where: { $0.displayID == did }) else { throw CaptureError.noDisplay }
        // a moldura, a barra e a contagem não entram no vídeo; as outras janelas da SnapStudio sim
        let filter = SCContentFilter(display: display, excludingWindows: Capture.ownTransientWindows(content))

        let cfg = SCStreamConfiguration()
        cfg.sourceRect = CGRect(x: area.minX, y: screen.frame.height - area.maxY, width: area.width, height: area.height)
        cfg.width = Int(outputSize.width)
        cfg.height = Int(outputSize.height)
        cfg.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(fps))
        cfg.showsCursor = drawCursor
        cfg.pixelFormat = kCVPixelFormatType_32BGRA
        cfg.queueDepth = 8
        cfg.capturesAudio = false

        try? FileManager.default.removeItem(at: url)
        let w = try AVAssetWriter(outputURL: url, fileType: .mp4)
        let bitrate = Int(outputSize.width * outputSize.height) * fps / 8
        let settings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: Int(outputSize.width),
            AVVideoHeightKey: Int(outputSize.height),
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: max(2_000_000, bitrate),
                AVVideoExpectedSourceFrameRateKey: fps,
                AVVideoMaxKeyFrameIntervalKey: fps * 2,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
            ],
        ]
        let inp = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        inp.expectsMediaDataInRealTime = true
        let ad = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: inp, sourcePixelBufferAttributes: nil)
        guard w.canAdd(inp) else { throw NSError(domain: "SnapStudio", code: 10, userInfo: [NSLocalizedDescriptionKey: "AVAssetWriter"]) }
        w.add(inp)
        guard w.startWriting() else { throw w.error ?? NSError(domain: "SnapStudio", code: 11) }
        w.startSession(atSourceTime: .zero)
        writer = w; input = inp; adaptor = ad

        let s = SCStream(filter: filter, configuration: cfg, delegate: self)
        try s.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        stream = s
        markStarted()
        try await s.startCapture()
    }

    private func markStarted() {
        lock.lock(); defer { lock.unlock() }
        startedAt = VideoRecorder.now
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sb: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, sb.isValid, !finished else { return }
        guard let atts = CMSampleBufferGetSampleAttachmentsArray(sb, createIfNecessary: false) as? [[SCStreamFrameInfo: Any]],
              let raw = atts.first?[.status] as? Int, SCFrameStatus(rawValue: raw) == .complete,
              let pb = sb.imageBuffer else { return }
        let pts = sb.presentationTimeStamp
        lock.lock()
        let isPaused = pauseStart != nil
        let pausedSoFar = pausedTotal
        if firstPTS == nil && !isPaused { firstPTS = pts }
        let first = firstPTS
        lock.unlock()
        guard !isPaused, let f = first else { return }
        var t = CMTimeSubtract(CMTimeSubtract(pts, f), pausedSoFar)
        if CMTimeCompare(t, lastTime) <= 0 && framesWritten > 0 { t = CMTimeAdd(lastTime, CMTime(value: 1, timescale: 600)) }
        if let inp = input, inp.isReadyForMoreMediaData, adaptor?.append(pb, withPresentationTime: t) == true {
            framesWritten += 1
            lastTime = t
        }
        lastBuffer = pb
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        self.error = error
        DispatchQueue.main.async { self.onError?(error) }
    }

    /// Pára e escreve o ficheiro (ou apaga-o, se cancelado).
    func stop(cancel: Bool) async {
        cancelled = cancel
        if let s = stream { try? await s.stopCapture() }
        stream = nil
        let end = CMTime(seconds: elapsed, preferredTimescale: 600)
        await withCheckedContinuation { (c: CheckedContinuation<Void, Never>) in
            queue.async { [self] in
                finished = true
                guard let w = writer, let inp = input else { c.resume(); return }
                if cancel || framesWritten == 0 {
                    if !cancel && error == nil {
                        error = NSError(domain: "SnapStudio", code: 12, userInfo: [NSLocalizedDescriptionKey:
                            S.t("Não foi gravado nenhum fotograma.", "No frames were recorded.")])
                    }
                    w.cancelWriting()
                    try? FileManager.default.removeItem(at: url)
                    c.resume()
                    return
                }
                // repetir o último fotograma para o vídeo durar até ao fim (ecrã parado não gera fotogramas)
                if let pb = lastBuffer, CMTimeCompare(end, lastTime) > 0, inp.isReadyForMoreMediaData {
                    adaptor?.append(pb, withPresentationTime: end)
                    lastTime = end
                }
                inp.markAsFinished()
                w.endSession(atSourceTime: CMTimeMaximum(end, lastTime))
                w.finishWriting {
                    if w.status == .failed { self.error = w.error }
                    c.resume()
                }
            }
        }
    }
}
