// Track B research helper: raw Apple Vision OCR only (no translation, no Foundation Models).
// Mirrors the recognition settings of src/PurchaseMemory.AppleIntelligence/AppleReceiptSidecar.swift
// (.accurate level, usesLanguageCorrection) but keeps the ORIGINAL Polish/German text and
// groups fragments into rows, so the raw text can be fed to a text-only Ollama LLM.
//
// Usage: ocr_only <image-path> [lang1 lang2 ...]     default languages: pl-PL

import CoreGraphics
import Foundation
import ImageIO
import Vision

let args = Array(CommandLine.arguments.dropFirst())
guard let path = args.first else {
    FileHandle.standardError.write(Data("Usage: ocr_only <image> [langs...]\n".utf8))
    exit(2)
}
let languages = args.count > 1 ? Array(args.dropFirst()) : ["pl-PL"]

guard
    let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil),
    let image = CGImageSourceCreateImageAtIndex(src, 0, nil)
else {
    FileHandle.standardError.write(Data("Cannot decode \(path)\n".utf8))
    exit(1)
}

let props = CGImageSourceCopyPropertiesAtIndex(src, 0, nil) as? [CFString: Any]
let raw = (props?[kCGImagePropertyOrientation] as? NSNumber)?.uint32Value ?? 1
let orientation = CGImagePropertyOrientation(rawValue: raw) ?? .up

let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.recognitionLanguages = languages
request.usesLanguageCorrection = true

let handler = VNImageRequestHandler(cgImage: image, orientation: orientation)
try handler.perform([request])

struct Frag { let text: String; let b: CGRect }

let frags = (request.results ?? []).compactMap { obs -> Frag? in
    guard let c = obs.topCandidates(1).first else { return nil }
    let t = c.string.trimmingCharacters(in: .whitespacesAndNewlines)
    return t.isEmpty ? nil : Frag(text: t, b: obs.boundingBox)
}.sorted { l, r in
    abs(l.b.midY - r.b.midY) > 0.004 ? l.b.midY > r.b.midY : l.b.minX < r.b.minX
}

// Same row-grouping heuristic as the production sidecar.
func sameRow(_ l: CGRect, _ r: CGRect) -> Bool {
    let overlap = max(0, min(l.maxY, r.maxY) - max(l.minY, r.minY))
    let minH = max(0.0001, min(l.height, r.height))
    let ratio = overlap / minH
    let dist = abs(l.midY - r.midY)
    let tol = max(l.height, r.height) * 0.58
    return ratio >= 0.32 || dist <= tol
}

var rows: [(frags: [Frag], b: CGRect)] = []
for f in frags {
    if let i = rows.lastIndex(where: { sameRow(f.b, $0.b) }) {
        rows[i].frags.append(f)
        rows[i].b = rows[i].b.union(f.b)
    } else {
        rows.append(([f], f.b))
    }
}
rows = rows.map { r in (r.frags.sorted { $0.b.minX < $1.b.minX }, r.b) }
           .sorted { $0.b.midY > $1.b.midY }

let out = rows.map { $0.frags.map(\.text).joined(separator: "  ") }.joined(separator: "\n")
print(out)
