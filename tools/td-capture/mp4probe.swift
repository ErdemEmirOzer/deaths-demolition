// WP-099 (S-01, C-00 07.10) · td-capture's check: one JSON line per MP4 — video codec, size, nominal fps, duration and
// the audio track's codec, sample rate, channels, duration. AVFoundation only (macOS built-in).
// usage: mp4probe <file.mp4>     exit 0 read · 2 unreadable / no video track · 64 usage
import AVFoundation

guard CommandLine.arguments.count == 2 else {
    FileHandle.standardError.write("usage: mp4probe <file.mp4>\n".data(using: .utf8)!)
    exit(64)
}
let asset = AVURLAsset(url: URL(fileURLWithPath: CommandLine.arguments[1]))

func fourcc(_ code: FourCharCode) -> String {
    let bytes = [24, 16, 8, 0].map { UInt8((code >> UInt32($0)) & 0xff) }
    return String(bytes: bytes, encoding: .ascii)?.trimmingCharacters(in: .whitespaces) ?? "?"
}

var out: [String: Any] = ["file": CommandLine.arguments[1], "duration": CMTimeGetSeconds(asset.duration)]
guard let video = asset.tracks(withMediaType: .video).first,
      let vdesc = video.formatDescriptions.first.map({ $0 as! CMFormatDescription }) else {
    FileHandle.standardError.write("no video track\n".data(using: .utf8)!)
    exit(2)
}
let dims = CMVideoFormatDescriptionGetDimensions(vdesc)
out["video"] = ["codec": fourcc(CMFormatDescriptionGetMediaSubType(vdesc)), "width": Int(dims.width),
                "height": Int(dims.height), "fps": Double(video.nominalFrameRate),
                "duration": CMTimeGetSeconds(video.timeRange.duration)]
if let audio = asset.tracks(withMediaType: .audio).first,
   let adesc = audio.formatDescriptions.first.map({ $0 as! CMAudioFormatDescription }),
   let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(adesc)?.pointee {
    out["audio"] = ["codec": fourcc(asbd.mFormatID), "rate": asbd.mSampleRate, "channels": Int(asbd.mChannelsPerFrame),
                    "duration": CMTimeGetSeconds(audio.timeRange.duration)]
} else {
    out["audio"] = NSNull()
}
let data = try JSONSerialization.data(withJSONObject: out, options: [.sortedKeys])
print(String(data: data, encoding: .utf8)!)
