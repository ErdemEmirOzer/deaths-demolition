// WP-099 (S-01, C-00 07.10) · td-capture's muxer: timestamped JPEG/PNG frames + an optional WAV → H.264 + AAC MP4,
// with AVFoundation only (macOS built-in; nothing downloaded or installed). Builds on S-09's frames2mp4 (WP-081).
//
// usage: tdmux --frames <dir> --times <frames.tsv> --out <out.mp4> [--wav <file.wav> --wav-offset <s>]
//              [--fps 30] [--mbps 16] [--audio-kbps 192] [--crop WxH [--crop-y px]]
//   frames.tsv  "idx<TAB>t" per line: frame <dir>/f-<idx 6 digits>.jpg|png was grabbed t seconds after video t0
//               (real time — the game is NOT slowed down, so the WAV stays in step).
//   --wav-offset  seconds into the WAV that correspond to video t0 (positive: the WAV started earlier; negative: later).
// The video is laid on a CONSTANT fps grid: grid frame k (time k/fps) shows the latest grabbed frame with t <= k/fps
// (a skipped slot repeats the previous frame), so the MP4 plays at true speed and stays in sync with the audio.
//   --crop      WP-110 (mk-capture): the output is the centred WxH window of every frame (no scaling; W, H even, ≤ the
//               frame); --crop-y moves the window up (+) or down (−) by that many pixels, clamped to the frame.
// exit: 0 written · 1 writer/reader failed · 2 no frames / bad input · 64 usage
import AVFoundation
import AppKit
import CoreVideo

func fail(_ code: Int32, _ msg: String) -> Never {
    FileHandle.standardError.write((msg + "\n").data(using: .utf8)!)
    exit(code)
}

var opts: [String: String] = [:]
var i = 1
let argv = CommandLine.arguments
while i < argv.count {
    let a = argv[i]
    guard a.hasPrefix("--"), i + 1 < argv.count else { fail(64, "usage: tdmux --frames d --times f.tsv --out o.mp4 [--wav w --wav-offset s] [--fps n] [--mbps n] [--audio-kbps n]") }
    opts[String(a.dropFirst(2))] = argv[i + 1]
    i += 2
}
guard let framesDir = opts["frames"], let timesPath = opts["times"], let outPath = opts["out"] else {
    fail(64, "usage: tdmux --frames d --times f.tsv --out o.mp4 [--wav w --wav-offset s] [--fps n] [--mbps n]")
}
let fps = Int32(opts["fps"] ?? "30") ?? 30
let mbps = Int(opts["mbps"] ?? "16") ?? 16
let audioKbps = Int(opts["audio-kbps"] ?? "192") ?? 192
let wavOffset = Double(opts["wav-offset"] ?? "0") ?? 0

// --- the grabbed frames, in time order ------------------------------------------------------------------------------
guard let tsv = try? String(contentsOfFile: timesPath, encoding: .utf8) else { fail(2, "cannot read \(timesPath)") }
var grabbed: [(t: Double, url: URL)] = []
for line in tsv.split(whereSeparator: \.isNewline) {
    let cols = line.split(separator: "\t")
    guard cols.count >= 2, let idx = Int(cols[0]), let t = Double(cols[1]) else { continue }
    let stem = String(format: "f-%06d", idx)
    let dir = URL(fileURLWithPath: framesDir)
    let url = ["jpg", "png"].map { dir.appendingPathComponent("\(stem).\($0)") }
        .first { FileManager.default.fileExists(atPath: $0.path) }
    if let url = url { grabbed.append((t, url)) }
}
grabbed.sort { $0.t < $1.t }
guard let first = grabbed.first, let firstImage = NSImage(contentsOf: first.url),
      let firstCg = firstImage.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
    fail(2, "no frames listed in \(timesPath) were found in \(framesDir)")
}
let srcWidth = firstCg.width, srcHeight = firstCg.height
var width = srcWidth, height = srcHeight, cropOriginY = 0
if let crop = opts["crop"] {
    let parts = crop.split(separator: "x").compactMap { Int($0) }
    guard parts.count == 2, parts[0] > 0, parts[1] > 0, parts[0] % 2 == 0, parts[1] % 2 == 0,
          parts[0] <= srcWidth, parts[1] <= srcHeight else { fail(64, "--crop \(crop): WxH, even, within \(srcWidth)x\(srcHeight)") }
    width = parts[0]; height = parts[1]
    // CoreGraphics origin is bottom-left: the window's bottom edge, moved up by --crop-y, kept inside the frame
    let up = Int(opts["crop-y"] ?? "0") ?? 0
    cropOriginY = min(max((srcHeight - height) / 2 + up, 0), srcHeight - height)
}
let cropOriginX = (srcWidth - width) / 2
let duration = grabbed.last!.t + 1.0 / Double(fps)
let gridFrames = Int((duration * Double(fps)).rounded(.down))

// --- writer: video ------------------------------------------------------------------------------------------------
let out = URL(fileURLWithPath: outPath)
try? FileManager.default.removeItem(at: out)
let writer: AVAssetWriter
do { writer = try AVAssetWriter(outputURL: out, fileType: .mp4) } catch { fail(1, "writer: \(error)") }
let vInput = AVAssetWriterInput(mediaType: .video, outputSettings: [
    AVVideoCodecKey: AVVideoCodecType.h264,
    AVVideoWidthKey: width,
    AVVideoHeightKey: height,
    AVVideoCompressionPropertiesKey: [AVVideoAverageBitRateKey: mbps * 1_000_000,
                                      AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
                                      AVVideoExpectedSourceFrameRateKey: fps]
])
vInput.expectsMediaDataInRealTime = false
let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: vInput, sourcePixelBufferAttributes: [
    kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32ARGB,
    kCVPixelBufferWidthKey as String: width,
    kCVPixelBufferHeightKey as String: height
])
writer.add(vInput)

// --- writer + reader: audio (optional) ----------------------------------------------------------------------------
var aInput: AVAssetWriterInput?
var aReader: AVAssetReader?
var aOutput: AVAssetReaderTrackOutput?
if let wavPath = opts["wav"] {
    let asset = AVURLAsset(url: URL(fileURLWithPath: wavPath))
    guard let track = asset.tracks(withMediaType: .audio).first else { fail(2, "no audio track in \(wavPath)") }
    let desc = track.formatDescriptions.first.map { $0 as! CMAudioFormatDescription }
    let asbd = desc.flatMap { CMAudioFormatDescriptionGetStreamBasicDescription($0)?.pointee }
    let rate = asbd?.mSampleRate ?? 48000
    let channels = Int(asbd?.mChannelsPerFrame ?? 2)
    do { aReader = try AVAssetReader(asset: asset) } catch { fail(1, "audio reader: \(error)") }
    // the WAV second that matches video t0; a negative offset (audio started after t0) is handled by shifting PTS
    let start = max(0, wavOffset)
    aReader!.timeRange = CMTimeRange(start: CMTime(seconds: start, preferredTimescale: 48000),
                                     duration: CMTime(seconds: duration - max(0, -wavOffset), preferredTimescale: 48000))
    aOutput = AVAssetReaderTrackOutput(track: track, outputSettings: [
        AVFormatIDKey: kAudioFormatLinearPCM, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
        AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false])
    aReader!.add(aOutput!)
    aInput = AVAssetWriterInput(mediaType: .audio, outputSettings: [
        AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: rate, AVNumberOfChannelsKey: min(channels, 2),
        AVEncoderBitRateKey: audioKbps * 1000])
    aInput!.expectsMediaDataInRealTime = false
    writer.add(aInput!)
}

writer.startWriting()
writer.startSession(atSourceTime: .zero)

// --- feed both inputs concurrently (AVAssetWriter interleaves: feeding all video first deadlocks once audio is due) --
let group = DispatchGroup()
var repeated = 0
var audioSeconds = 0.0

// video: constant-fps grid — grid frame k (k/fps) shows the latest frame grabbed at or before k/fps
group.enter()
var k = 0, cursor = 0
var lastUrl: URL? = nil
var lastBuffer: CVPixelBuffer? = nil
vInput.requestMediaDataWhenReady(on: DispatchQueue(label: "tdmux.video")) {
    while vInput.isReadyForMoreMediaData {
        if k >= gridFrames { vInput.markAsFinished(); group.leave(); return }
        autoreleasepool {
            let t = Double(k) / Double(fps)
            while cursor + 1 < grabbed.count && grabbed[cursor + 1].t <= t + 1e-6 { cursor += 1 }
            let url = grabbed[cursor].url
            if url != lastUrl || lastBuffer == nil {
                if let image = NSImage(contentsOf: url), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
                    var buffer: CVPixelBuffer?
                    CVPixelBufferPoolCreatePixelBuffer(nil, adaptor.pixelBufferPool!, &buffer)
                    if let pixel = buffer {
                        CVPixelBufferLockBaseAddress(pixel, [])
                        let context = CGContext(data: CVPixelBufferGetBaseAddress(pixel), width: width, height: height,
                                                bitsPerComponent: 8, bytesPerRow: CVPixelBufferGetBytesPerRow(pixel),
                                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                                bitmapInfo: CGImageAlphaInfo.noneSkipFirst.rawValue)
                        context?.draw(cg, in: CGRect(x: -cropOriginX, y: -cropOriginY, width: srcWidth, height: srcHeight))
                        CVPixelBufferUnlockBaseAddress(pixel, [])
                        lastBuffer = pixel; lastUrl = url
                    }
                }
            } else {
                repeated += 1
            }
            if let buf = lastBuffer { adaptor.append(buf, withPresentationTime: CMTime(value: Int64(k), timescale: fps)) }
            k += 1
        }
    }
}

// audio: retimed so WAV second `offset` lands on video t0
if let reader = aReader, let output = aOutput, let input = aInput {
    guard reader.startReading() else { fail(1, "audio reader: \(String(describing: reader.error))") }
    let shift = CMTime(seconds: max(0, wavOffset), preferredTimescale: 48000)
    let lead = CMTime(seconds: max(0, -wavOffset), preferredTimescale: 48000)
    group.enter()
    input.requestMediaDataWhenReady(on: DispatchQueue(label: "tdmux.audio")) {
        while input.isReadyForMoreMediaData {
            guard let sample = output.copyNextSampleBuffer() else { input.markAsFinished(); group.leave(); return }
            var timing = CMSampleTimingInfo()
            CMSampleBufferGetSampleTimingInfo(sample, at: 0, timingInfoOut: &timing)
            timing.presentationTimeStamp = CMTimeAdd(CMTimeSubtract(timing.presentationTimeStamp, shift), lead)
            timing.decodeTimeStamp = .invalid
            var retimed: CMSampleBuffer?
            CMSampleBufferCreateCopyWithNewTiming(allocator: nil, sampleBuffer: sample, sampleTimingEntryCount: 1,
                                                  sampleTimingArray: &timing, sampleBufferOut: &retimed)
            guard let buf = retimed else { continue }
            input.append(buf)
            audioSeconds = CMTimeGetSeconds(CMTimeAdd(timing.presentationTimeStamp, CMSampleBufferGetDuration(buf)))
        }
    }
}
group.wait()

let done = DispatchSemaphore(value: 0)
writer.finishWriting { done.signal() }
done.wait()
guard writer.status == .completed else { fail(1, "writer failed: \(String(describing: writer.error))") }
print(String(format: "tdmux: wrote %@ — %d grid frames (%d repeated) at %d fps, %dx%d, %.2f s video, %.2f s audio",
             out.path, gridFrames, repeated, fps, width, height, Double(gridFrames) / Double(fps), audioSeconds))
