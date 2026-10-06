// Apple Vision OCR -> JSON fragments WITH bounding boxes (no row grouping).
import CoreGraphics
import Foundation
import ImageIO
import Vision

let args = Array(CommandLine.arguments.dropFirst())
guard let path = args.first else { exit(2) }
let languages = args.count > 1 ? Array(args.dropFirst()) : ["pl-PL"]
guard
    let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil),
    let image = CGImageSourceCreateImageAtIndex(src, 0, nil)
else { exit(1) }
let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]
let raw = (props?[kCGImagePropertyOrientation] as? NSNumber)?.uint32Value ?? 1
let orientation = CGImagePropertyOrientation(rawValue: raw) ?? .up

let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.recognitionLanguages = languages
request.usesLanguageCorrection = true
let handler = VNImageRequestHandler(cgImage: image, orientation: orientation)
try handler.perform([request])

struct Frag: Encodable { let t: String; let x: Int; let y: Int; let w: Int; let h: Int }
let frags = (request.results ?? []).compactMap { obs -> Frag? in
    guard let c = obs.topCandidates(1).first else { return nil }
    let s = c.string.trimmingCharacters(in: .whitespacesAndNewlines)
    if s.isEmpty { return nil }
    let b = obs.boundingBox
    // y flipped so 0 = top of receipt; scaled to 0..1000 integers to save tokens
    return Frag(t: s,
                x: Int((b.minX * 1000).rounded()),
                y: Int(((1 - b.maxY) * 1000).rounded()),
                w: Int((b.width * 1000).rounded()),
                h: Int((b.height * 1000).rounded()))
}.sorted { $0.y != $1.y ? $0.y < $1.y : $0.x < $1.x }

let data = try JSONEncoder().encode(frags)
print(String(data: data, encoding: .utf8)!)
