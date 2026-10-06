// Apple Vision OCR -> rows grouped by a STABLE y anchor (no runaway union), emitted as JSON.
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
try VNImageRequestHandler(cgImage: image, orientation: orientation).perform([request])

struct Frag { let text: String; let midY: CGFloat; let minX: CGFloat; let h: CGFloat }
let frags = (request.results ?? []).compactMap { obs -> Frag? in
    guard let c = obs.topCandidates(1).first else { return nil }
    let s = c.string.trimmingCharacters(in: .whitespacesAndNewlines)
    if s.isEmpty { return nil }
    let b = obs.boundingBox
    return Frag(text: s, midY: 1 - b.midY, minX: b.minX, h: b.height)
}.sorted { $0.midY < $1.midY }

// Typical glyph height -> row tolerance. Median is robust against the big header text.
let heights = frags.map(\.h).sorted()
let medianH = heights.isEmpty ? 0.01 : heights[heights.count / 2]
let tol = medianH * 0.6

// Anchor stays the running MEAN of member midYs, so a row never grows unboundedly.
struct Row { var anchor: CGFloat; var n: Int; var frags: [Frag] }
var rows: [Row] = []
for f in frags {
    if let i = rows.firstIndex(where: { abs($0.anchor - f.midY) <= tol }) {
        rows[i].frags.append(f)
        rows[i].anchor = (rows[i].anchor * CGFloat(rows[i].n) + f.midY) / CGFloat(rows[i].n + 1)
        rows[i].n += 1
    } else {
        rows.append(Row(anchor: f.midY, n: 1, frags: [f]))
    }
}

struct OutRow: Encodable { let y: Int; let cells: [String] }
let out = rows.sorted { $0.anchor < $1.anchor }.map { r in
    OutRow(y: Int((r.anchor * 1000).rounded()),
           cells: r.frags.sorted { $0.minX < $1.minX }.map(\.text))
}
FileHandle.standardError.write(Data("rows=\(out.count) medianH=\(medianH) tol=\(tol)\n".utf8))
print(String(data: try JSONEncoder().encode(out), encoding: .utf8)!)
