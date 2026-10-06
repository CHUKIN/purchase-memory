import CoreGraphics
import Darwin
import Foundation
import FoundationModels
import ImageIO
import Translation
import Vision

@Generable(description: "One purchased product from a Polish retail receipt")
private struct GeneratedReceiptItem {
    @Guide(description: "Exact R row ID containing the product name; never cite a quantity-only, price-only, discount, tax, total, or payment row")
    var nameRowId: String

    @Guide(description: "Short English product name used only if a cited source fragment is unavailable")
    var englishName: String

    @Guide(description: "Purchased quantity; package size inside the name is not quantity; nil when not printed")
    var quantity: Double?

    @Guide(description: "Purchased quantity unit: pcs, kg, l, g, or ml; nil when not printed")
    var unit: String?

    @Guide(description: "Price for one purchased unit; nil when not printed")
    var unitPrice: Double?

    @Guide(description: "Total for this product before its directly related discount; nil when not printed")
    var grossLineTotal: Double?

    @Guide(description: "Positive discount amount directly related to this product; nil when absent")
    var discount: Double?

    @Guide(description: "Final total paid for this product after its discount; nil when not printed")
    var lineTotal: Double?
}

@Generable(description: "Receipt metadata extracted only from numbered, translated OCR rows")
private struct GeneratedReceiptMetadata {
    @Guide(description: "Exact R row ID containing the customer-facing merchant brand; prefer the store logo over the legal company name")
    var merchantRowId: String

    @Guide(description: "Customer-facing merchant brand in English, used only if no cited source fragment exists")
    var merchantName: String

    @Guide(description: "Purchase local date and time as yyyy-MM-ddTHH:mm:ss; use an empty string only when no date is printed")
    var purchasedAt: String

    @Guide(description: "ISO currency code such as PLN; use an empty string only when no currency is printed")
    var currency: String

    @Guide(description: "Grand total actually paid for the receipt; never use tax, discount, cash tender, or change totals; use 0 only when absent")
    var receiptTotal: Double

}

@Generable(description: "Purchased products extracted only from the supplied product-area rows")
private struct GeneratedReceiptItems {
    @Guide(description: "Every actual purchased product in reading order; exclude discounts as separate items and exclude numeric-only rows", .maximumCount(50))
    var items: [GeneratedReceiptItem]
}

private struct GeneratedReceipt {
    let metadata: GeneratedReceiptMetadata
    let items: [GeneratedReceiptItem]
}

private struct OcrFragment: Sendable, ReceiptGeometryFragment {
    let id: String
    let order: Int
    let text: String
    let confidence: Float
    let bounds: CGRect
}

/// Rows come from `ReceiptGeometry`: a stable mean anchor plus a tolerance taken
/// from the median fragment height, so a row can no longer grow without bound.
private typealias OcrRow = ReceiptGeometryRow<OcrFragment>
private typealias OcrGeometry = ReceiptGeometryResult<OcrFragment>

private struct SidecarStatus: Encodable {
    let schemaVersion = 1
    let available: Bool
    let foundationModelAvailable: Bool
    let polishTranslationInstalled: Bool
    let message: String?
}

private struct ReceiptItemOutput: Encodable {
    let name: String
    let nameRowId: String?
    let quantity: Double?
    let unit: String?
    let unitPrice: Double?
    let grossLineTotal: Double?
    let discount: Double?
    let lineTotal: Double?
}

private struct ReceiptOutput: Encodable {
    let merchantName: String?
    let purchasedAt: String?
    let currency: String?
    let receiptTotal: Double?
    let items: [ReceiptItemOutput]
}

private struct ExtractionDiagnostics: Encodable {
    let ocrFragmentCount: Int
    let ocrRowCount: Int
    let ocrSkew: Double
    let ocrRowsWithSinglePriceCell: Int
    let ocrRowsWithMultiplePriceCells: Int
    let ocrDurationMs: Int
    let translationDurationMs: Int
    let foundationModelDurationMs: Int
    let totalDurationMs: Int
}

private struct ExtractionOutput: Encodable {
    let schemaVersion = 1
    let receipt: ReceiptOutput
    let diagnostics: ExtractionDiagnostics
}

/// Geometry-only output: OCR fragments and rows WITH coordinates, no model involved.
/// Coordinates are integers on a 0..1000 page scale with y pointing down (0 = top),
/// matching `research/ocr-geometry/drafts`.
private struct GeometryFragmentOutput: Encodable {
    let id: String
    let text: String
    let confidence: Double
    let x: Int
    let y: Int
    let width: Int
    let height: Int
}

private struct GeometryRowOutput: Encodable {
    let id: String
    let y: Int
    let priceCellCount: Int
    let fragmentIds: [String]
    let text: String
    let cells: [GeometryFragmentOutput]
}

private struct GeometryDiagnostics: Encodable {
    let fragmentCount: Int
    let rowCount: Int
    let skew: Double
    let skewReachedSearchBoundary: Bool
    let medianFragmentHeight: Double
    let rowsWithSinglePriceCell: Int
    let rowsWithMultiplePriceCells: Int
    let ocrDurationMs: Int
}

private struct GeometryOutput: Encodable {
    let schemaVersion = 1
    let fragments: [GeometryFragmentOutput]
    let rows: [GeometryRowOutput]
    let diagnostics: GeometryDiagnostics
}

private struct ErrorOutput: Encodable {
    let schemaVersion = 1
    let error: String
}

private enum SidecarError: LocalizedError {
    case invalidArguments
    case imageCannotBeRead(String)
    case noTextRecognized
    case foundationModelUnavailable(String)
    case polishTranslationUnavailable
    case emptyModelResult

    var errorDescription: String? {
        switch self {
        case .invalidArguments:
            return "Usage: PurchaseMemory.AppleIntelligence status | extract <image-path> | geometry <image-path>"
        case .imageCannotBeRead(let path):
            return "Cannot decode receipt image at \(path)."
        case .noTextRecognized:
            return "Apple Vision did not find readable text in the receipt image."
        case .foundationModelUnavailable(let reason):
            return "Apple Foundation Models is unavailable: \(reason)"
        case .polishTranslationUnavailable:
            return "The Polish-to-English Apple Translation model is not installed."
        case .emptyModelResult:
            return "Apple Foundation Models did not return any purchased products."
        }
    }
}

@main
private struct AppleReceiptSidecar {
    static func main() async {
        do {
            try await run()
        } catch {
            writeJson(ErrorOutput(error: error.localizedDescription))
            exit(EXIT_FAILURE)
        }
    }

    private static func run() async throws {
        let arguments = Array(CommandLine.arguments.dropFirst())
        guard let command = arguments.first else {
            throw SidecarError.invalidArguments
        }

        switch command {
        case "status" where arguments.count == 1:
            writeJson(await status())
        case "extract" where arguments.count == 2:
            writeJson(try await extract(imagePath: arguments[1]))
        case "geometry" where arguments.count == 2:
            writeJson(try geometry(imagePath: arguments[1]))
        default:
            throw SidecarError.invalidArguments
        }
    }

    private static func status() async -> SidecarStatus {
        let model = SystemLanguageModel.default
        let foundationAvailable: Bool
        let foundationMessage: String?
        switch model.availability {
        case .available:
            foundationAvailable = model.supportsLocale(Locale(identifier: "en_US"))
            foundationMessage = foundationAvailable ? nil : "The on-device model does not support English."
        case .unavailable(let reason):
            foundationAvailable = false
            foundationMessage = String(describing: reason)
        }

        let translationStatus = await LanguageAvailability().status(
            from: Locale.Language(identifier: "pl"),
            to: Locale.Language(identifier: "en")
        )
        let translationInstalled = translationStatus == .installed
        let available = foundationAvailable && translationInstalled
        let message: String?
        if available {
            message = nil
        } else if !foundationAvailable {
            message = foundationMessage ?? "Apple Foundation Models is not ready."
        } else {
            message = "The Polish-to-English translation model is not installed."
        }

        return SidecarStatus(
            available: available,
            foundationModelAvailable: foundationAvailable,
            polishTranslationInstalled: translationInstalled,
            message: message
        )
    }

    /// OCR plus geometry only: no Foundation model, no translation, no network.
    /// Used by the .NET side when it wants rows with coordinates instead of glued text,
    /// and by the offline checks that count rows holding exactly one price cell.
    private static func geometry(imagePath: String) throws -> GeometryOutput {
        let startedAt = Date()
        let fragments = try recognizeText(imagePath: imagePath)
        guard !fragments.isEmpty else {
            throw SidecarError.noTextRecognized
        }
        let geometry = analyzeGeometry(fragments)
        let duration = milliseconds(since: startedAt)

        func describe(_ fragment: OcrFragment) -> GeometryFragmentOutput {
            GeometryFragmentOutput(
                id: fragment.id,
                text: fragment.text,
                confidence: Double(fragment.confidence),
                x: Int((fragment.bounds.minX * 1000).rounded()),
                y: Int(((1 - fragment.bounds.maxY) * 1000).rounded()),
                width: Int((fragment.bounds.width * 1000).rounded()),
                height: Int((fragment.bounds.height * 1000).rounded())
            )
        }

        let rows = geometry.rows.enumerated().map { index, row in
            GeometryRowOutput(
                id: "R\(index + 1)",
                y: Int((row.anchorY * 1000).rounded()),
                priceCellCount: row.priceCellCount,
                fragmentIds: row.fragments.map(\.id),
                text: row.fragments.map(\.text).joined(separator: " "),
                cells: row.fragments.map(describe)
            )
        }

        return GeometryOutput(
            fragments: fragments.map(describe),
            rows: rows,
            diagnostics: GeometryDiagnostics(
                fragmentCount: fragments.count,
                rowCount: rows.count,
                skew: Double(geometry.skew),
                skewReachedSearchBoundary: geometry.reachedSearchBoundary,
                medianFragmentHeight: Double(geometry.medianFragmentHeight),
                rowsWithSinglePriceCell: geometry.rowsWithSinglePriceCell,
                rowsWithMultiplePriceCells: geometry.rowsWithMultiplePriceCells,
                ocrDurationMs: duration
            )
        )
    }

    private static func extract(imagePath: String) async throws -> ExtractionOutput {
        let totalStartedAt = Date()
        let currentStatus = await status()
        guard currentStatus.foundationModelAvailable else {
            throw SidecarError.foundationModelUnavailable(currentStatus.message ?? "model is not ready")
        }
        guard currentStatus.polishTranslationInstalled else {
            throw SidecarError.polishTranslationUnavailable
        }

        let ocrStartedAt = Date()
        let fragments = try recognizeText(imagePath: imagePath)
        guard !fragments.isEmpty else {
            throw SidecarError.noTextRecognized
        }
        let geometry = analyzeGeometry(fragments)
        let rows = geometry.rows
        let ocrDuration = milliseconds(since: ocrStartedAt)

        let translationStartedAt = Date()
        let translations = try await translate(fragments)
        let translatedReceipt = createTranslatedReceipt(rows: rows, translations: translations)
        let translatedProductArea = createTranslatedProductArea(rows: rows, translations: translations)
        let translationDuration = milliseconds(since: translationStartedAt)

        let modelStartedAt = Date()
        let generated = try await parseReceipt(
            translatedReceipt: translatedReceipt,
            translatedProductArea: translatedProductArea
        )
        let modelDuration = milliseconds(since: modelStartedAt)

        let rowsById = Dictionary(uniqueKeysWithValues: rows.enumerated().map { ("R\($0.offset + 1)", $0.element) })
        let itemRowIndices = generated.items.compactMap { item in
            rowIndex(item.nameRowId)
        }.sorted()
        let merchantName = canonicalMerchantName(
            sourceRow: rowsById[generated.metadata.merchantRowId],
            generatedName: generated.metadata.merchantName
        )

        let items = generated.items.compactMap { item -> ReceiptItemOutput? in
            let sourceRow = rowsById[item.nameRowId]
            let originalName = sourceRow.flatMap(originalProductName)
            guard let name = originalName ?? normalizedOptional(item.englishName) else {
                return nil
            }
            let adjustment = detectedAdjustment(
                for: item.nameRowId,
                itemRowIndices: itemRowIndices,
                rows: rows
            )
            let quantity = item.quantity
            let unit = normalizedPurchasedUnit(item.unit, quantity: quantity, sourceRow: sourceRow)
            let discount = adjustment?.discount ?? item.discount
            let lineTotal = adjustment?.finalLineTotal ?? item.lineTotal
            let grossLineTotal = item.grossLineTotal
                ?? combinedGrossLineTotal(lineTotal: lineTotal, discount: discount)

            return ReceiptItemOutput(
                name: name,
                nameRowId: rowsById[item.nameRowId] == nil ? nil : item.nameRowId,
                quantity: quantity,
                unit: unit,
                unitPrice: item.unitPrice,
                grossLineTotal: grossLineTotal,
                discount: discount,
                lineTotal: lineTotal
            )
        }

        guard !items.isEmpty else {
            throw SidecarError.emptyModelResult
        }

        return ExtractionOutput(
            receipt: ReceiptOutput(
                merchantName: merchantName,
                purchasedAt: detectedPurchaseDate(rows: rows)
                    ?? normalizedOptional(generated.metadata.purchasedAt),
                currency: detectedCurrency(rows: rows)
                    ?? normalizedOptional(generated.metadata.currency)?.uppercased(),
                receiptTotal: detectedReceiptTotal(rows: rows)
                    ?? positiveOptional(generated.metadata.receiptTotal),
                items: items
            ),
            diagnostics: ExtractionDiagnostics(
                ocrFragmentCount: fragments.count,
                ocrRowCount: rows.count,
                ocrSkew: Double(geometry.skew),
                ocrRowsWithSinglePriceCell: geometry.rowsWithSinglePriceCell,
                ocrRowsWithMultiplePriceCells: geometry.rowsWithMultiplePriceCells,
                ocrDurationMs: ocrDuration,
                translationDurationMs: translationDuration,
                foundationModelDurationMs: modelDuration,
                totalDurationMs: milliseconds(since: totalStartedAt)
            )
        )
    }

    private static func recognizeText(imagePath: String) throws -> [OcrFragment] {
        let imageUrl = URL(fileURLWithPath: imagePath)
        guard
            let imageSource = CGImageSourceCreateWithURL(imageUrl as CFURL, nil),
            let image = CGImageSourceCreateImageAtIndex(imageSource, 0, nil)
        else {
            throw SidecarError.imageCannotBeRead(imagePath)
        }

        let properties = CGImageSourceCopyPropertiesAtIndex(imageSource, 0, nil) as? [CFString: Any]
        let orientationRawValue = (properties?[kCGImagePropertyOrientation] as? NSNumber)?.uint32Value ?? 1
        let orientation = CGImagePropertyOrientation(rawValue: orientationRawValue) ?? .up

        let request = VNRecognizeTextRequest()
        request.recognitionLevel = .accurate
        request.recognitionLanguages = ["pl-PL"]
        request.usesLanguageCorrection = true

        let handler = VNImageRequestHandler(cgImage: image, orientation: orientation)
        try handler.perform([request])

        let recognized = (request.results ?? []).compactMap { observation -> (String, Float, CGRect)? in
            guard let candidate = observation.topCandidates(1).first else {
                return nil
            }
            let text = candidate.string.trimmingCharacters(in: .whitespacesAndNewlines)
            return text.isEmpty ? nil : (text, candidate.confidence, observation.boundingBox)
        }.sorted { left, right in
            let verticalDifference = abs(left.2.midY - right.2.midY)
            return verticalDifference > 0.004
                ? left.2.midY > right.2.midY
                : left.2.minX < right.2.minX
        }

        return recognized.enumerated().map { index, value in
            OcrFragment(
                id: "F\(index + 1)",
                order: index,
                text: value.0,
                confidence: value.1,
                bounds: value.2
            )
        }
    }

    /// Corrects the photo skew and groups fragments into rows, top to bottom.
    private static func analyzeGeometry(_ fragments: [OcrFragment]) -> OcrGeometry {
        let geometry = ReceiptGeometry.analyze(fragments)
        if geometry.reachedSearchBoundary {
            FileHandle.standardError.write(Data(
                "warning: skew search hit its boundary (skew=\(geometry.skew)); the photo may be tilted further than the search range covers\n".utf8
            ))
        }
        return geometry
    }

    private static func translate(_ fragments: [OcrFragment]) async throws -> [String: String] {
        let session = TranslationSession(
            installedSource: Locale.Language(identifier: "pl"),
            target: Locale.Language(identifier: "en"),
            preferredStrategy: .lowLatency
        )
        let requests = fragments.map {
            TranslationSession.Request(sourceText: $0.text, clientIdentifier: $0.id)
        }
        let responses = try await session.translations(from: requests)

        return Dictionary(uniqueKeysWithValues: responses.compactMap { response in
            guard let identifier = response.clientIdentifier else {
                return nil
            }
            return (identifier, response.targetText)
        })
    }

    private static func createTranslatedReceipt(
        rows: [OcrRow],
        translations: [String: String]
    ) -> String {
        rows.enumerated().map { rowIndex, row in
            let fragments = row.fragments.map { fragment in
                let translated = translations[fragment.id] ?? fragment.text
                return "[\(fragment.id)] \(translated)"
            }.joined(separator: " | ")
            return "[R\(rowIndex + 1)] \(fragments)"
        }.joined(separator: "\n")
    }

    private static func createTranslatedProductArea(
        rows: [OcrRow],
        translations: [String: String]
    ) -> String {
        let productArea = productAreaRange(rows: rows)
        guard !productArea.isEmpty else {
            return createTranslatedReceipt(rows: rows, translations: translations)
        }

        return rows[productArea].enumerated().map { offset, row in
            let rowId = "R\(productArea.lowerBound + offset + 1)"
            let fragments = row.fragments.map { fragment in
                let translated = translations[fragment.id] ?? fragment.text
                return "[\(fragment.id)] \(translated)"
            }.joined(separator: " | ")
            return "[\(rowId)] \(fragments)"
        }.joined(separator: "\n")
    }

    private static func parseReceipt(
        translatedReceipt: String,
        translatedProductArea: String
    ) async throws -> GeneratedReceipt {
        let metadataSession = LanguageModelSession(instructions: """
            You extract metadata from Polish grocery receipt OCR translated into English.
            Use only supplied text and numbers. Never invent a date, amount, merchant, currency, or row ID.
            Choose the customer-facing store brand, not its legal company name. The receipt total is the grand total paid,
            never a tax subtotal, discount total, cash tender, or change. You MUST respond in U.S. English.
            """)
        metadataSession.prewarm(promptPrefix: Prompt("Extract receipt metadata from numbered OCR rows."))
        let metadata = try await metadataSession.respond(
            to: "Extract receipt metadata from these rows:\n\(translatedReceipt)",
            generating: GeneratedReceiptMetadata.self,
            options: GenerationOptions(temperature: 0)
        ).content

        let itemsSession = LanguageModelSession(instructions: """
            You extract purchased products from a preselected product area of Polish receipt OCR translated into English.
            Every returned item must have a natural-language product name in a supplied row. Numeric-only rows, discounts,
            headers, totals, taxes, and payments are never products. A Discount row and a following standalone final amount
            belong to the preceding product. Package size inside a name is not purchased quantity. Use only supplied values,
            preserve exact R row IDs, and return items in reading order. You MUST respond in U.S. English.
            """)
        itemsSession.prewarm(promptPrefix: Prompt("Extract purchased products from product-area rows."))
        let items = try await itemsSession.respond(
            to: """
                Extract every purchased product from these PRODUCT AREA rows.
                Decimal commas may have been translated to decimal points. Cite the single R row containing each name.
                Use the final product amount after a directly related discount.

                \(translatedProductArea)
                """,
            generating: GeneratedReceiptItems.self,
            options: GenerationOptions(temperature: 0)
        ).content.items

        return GeneratedReceipt(metadata: metadata, items: items)
    }

    private static func productAreaRange(rows: [OcrRow]) -> Range<Int> {
        let startIndex = rows.firstIndex(where: { row in
            normalizedForMatching(originalRowText(row) ?? "").contains("PARAGON FISKALNY")
        }).map { $0 + 1 } ?? 0

        let endIndex = rows.indices.first(where: { index in
            guard index >= startIndex else {
                return false
            }
            let text = normalizedForMatching(originalRowText(rows[index]) ?? "")
            let boundaryMarkers = [
                "SPRZEDAZ OPODATKOWANA",
                "SUMA PTU",
                "SUMA PLN",
                "ROZLICZENIE PLATNOSCI"
            ]
            return boundaryMarkers.contains(where: text.contains)
        }) ?? rows.endIndex

        return startIndex < endIndex ? startIndex..<endIndex : rows.startIndex..<rows.endIndex
    }

    private static func detectedAdjustment(
        for rowId: String,
        itemRowIndices: [Int],
        rows: [OcrRow]
    ) -> (discount: Double, finalLineTotal: Double?)? {
        guard let currentIndex = rowIndex(rowId), rows.indices.contains(currentIndex) else {
            return nil
        }

        let area = productAreaRange(rows: rows)
        let nextItemIndex = itemRowIndices.first(where: { $0 > currentIndex }) ?? area.upperBound
        let upperBound = min(nextItemIndex, area.upperBound)
        guard currentIndex < upperBound else {
            return nil
        }

        let candidateRows = Array(rows[currentIndex..<upperBound])
        guard let discountRowOffset = candidateRows.firstIndex(where: { row in
            let text = normalizedForMatching(originalRowText(row) ?? "")
            return text.contains("RABAT") || text.contains("OPUST") || text.contains("DISCOUNT")
        }) else {
            return nil
        }

        let rowsFromDiscount = candidateRows[discountRowOffset...]
        let amounts = rowsFromDiscount.flatMap { monetaryAmounts(in: originalRowText($0) ?? "") }
        guard let negativeDiscount = amounts.first(where: { $0 < 0 }) else {
            return nil
        }

        let finalLineTotal = rowsFromDiscount.dropFirst().reversed().lazy
            .flatMap { monetaryAmounts(in: originalRowText($0) ?? "") }
            .first(where: { $0 >= 0 })

        return (abs(negativeDiscount), finalLineTotal)
    }

    private static func detectedPurchaseDate(rows: [OcrRow]) -> String? {
        let patterns = [
            #"\b\d{4}-\d{2}-\d{2}(?:[ T]\d{1,2}:\d{2}(?::\d{2})?)?\b"#,
            #"\b\d{2}[./]\d{2}[./]\d{4}(?:[ T]\d{1,2}:\d{2}(?::\d{2})?)?\b"#
        ]
        let receiptText = rows.compactMap(originalRowText).joined(separator: "\n")
        for pattern in patterns {
            if let value = firstMatch(pattern: pattern, in: receiptText) {
                return value
            }
        }
        return nil
    }

    private static func detectedCurrency(rows: [OcrRow]) -> String? {
        let text = normalizedForMatching(rows.compactMap(originalRowText).joined(separator: " "))
        return text.contains("PLN") || text.contains(" ZL") ? "PLN" : nil
    }

    private static func detectedReceiptTotal(rows: [OcrRow]) -> Double? {
        let preferredMarkers = ["SUMA PLN", "TOTAL PLN"]
        for row in rows.reversed() {
            let text = originalRowText(row) ?? ""
            let normalized = normalizedForMatching(text)
            if preferredMarkers.contains(where: normalized.contains),
               let amount = monetaryAmounts(in: text).last(where: { $0 >= 0 }) {
                return amount
            }
        }

        for row in rows.reversed() {
            let text = originalRowText(row) ?? ""
            if normalizedForMatching(text).contains("PLN"),
               let amount = monetaryAmounts(in: text).last(where: { $0 >= 0 }) {
                return amount
            }
        }
        return nil
    }

    private static func monetaryAmounts(in value: String) -> [Double] {
        let pattern = #"-?\d{1,6}[.,]\d{2,3}"#
        guard let expression = try? NSRegularExpression(pattern: pattern) else {
            return []
        }
        let range = NSRange(value.startIndex..<value.endIndex, in: value)
        return expression.matches(in: value, range: range).compactMap { match in
            guard let swiftRange = Range(match.range, in: value) else {
                return nil
            }
            let raw = value[swiftRange].replacingOccurrences(of: ",", with: ".")
            return Double(raw)
        }
    }

    private static func firstMatch(pattern: String, in value: String) -> String? {
        guard
            let expression = try? NSRegularExpression(pattern: pattern),
            let match = expression.firstMatch(
                in: value,
                range: NSRange(value.startIndex..<value.endIndex, in: value)
            ),
            let range = Range(match.range, in: value)
        else {
            return nil
        }
        return String(value[range])
    }

    private static func rowIndex(_ rowId: String) -> Int? {
        guard rowId.hasPrefix("R"), let oneBased = Int(rowId.dropFirst()), oneBased > 0 else {
            return nil
        }
        return oneBased - 1
    }

    private static func normalizedPurchasedUnit(
        _ value: String?,
        quantity: Double?,
        sourceRow: OcrRow?
    ) -> String? {
        guard let unit = normalizedOptional(value)?.lowercased() else {
            return nil
        }
        let rowText = normalizedForMatching(sourceRow.flatMap(originalRowText) ?? "")
        let hasPurchasedCount = firstMatch(pattern: #"\b\d+\s*[xX]\s*\d"#, in: rowText) != nil
        if let quantity, quantity.rounded() == quantity, hasPurchasedCount {
            return "pcs"
        }
        if unit == "kg", let quantity, quantity.rounded() == quantity, !rowText.contains(" KG") {
            return "pcs"
        }
        return unit
    }

    private static func canonicalMerchantName(sourceRow: OcrRow?, generatedName: String) -> String? {
        let sourceName = sourceRow.flatMap(originalRowText)
        let combined = normalizedForMatching([sourceName, generatedName]
            .compactMap { $0 }
            .joined(separator: " "))
        let knownMerchants: [(markers: [String], name: String)] = [
            (["BIEDRONKA", "JERONIMO MARTINS"], "Biedronka"),
            (["LIDL", "LICK SP"], "Lidl"),
            (["ZABKA"], "Żabka"),
            (["AUCHAN"], "Auchan"),
            (["CARREFOUR"], "Carrefour"),
            (["E.LECLERC", "E LECLERC", "LECLERC"], "E.Leclerc"),
            (["KAUFLAND"], "Kaufland"),
            (["ALDI"], "Aldi"),
            (["POLOMARKET"], "Polomarket"),
            (["DINO"], "Dino")
        ]

        if let merchant = knownMerchants.first(where: { entry in
            entry.markers.contains(where: combined.contains)
        }) {
            return merchant.name
        }
        return normalizedOptional(sourceName) ?? normalizedOptional(generatedName)
    }

    private static func combinedGrossLineTotal(lineTotal: Double?, discount: Double?) -> Double? {
        guard let lineTotal else {
            return nil
        }
        return lineTotal + (discount ?? 0)
    }

    private static func positiveOptional(_ value: Double) -> Double? {
        value > 0 ? value : nil
    }

    private static func originalRowText(_ row: OcrRow) -> String? {
        normalizedOptional(row.fragments.map(\.text).joined(separator: " "))
    }

    private static func originalProductName(_ row: OcrRow) -> String? {
        let nameFragments = row.fragments
            .filter { $0.bounds.minX < 0.55 }
            .map(\.text)
        return normalizedOptional(nameFragments.joined(separator: " "))
    }

    private static func normalizedForMatching(_ value: String) -> String {
        value
            .folding(options: [.caseInsensitive, .diacriticInsensitive], locale: Locale(identifier: "pl_PL"))
            .uppercased()
    }

    private static func normalizedOptional(_ value: String?) -> String? {
        guard let normalized = value?.trimmingCharacters(in: .whitespacesAndNewlines), !normalized.isEmpty else {
            return nil
        }
        return normalized
    }

    private static func milliseconds(since date: Date) -> Int {
        Int((Date().timeIntervalSince(date) * 1_000).rounded())
    }

    private static func writeJson<T: Encodable>(_ value: T) {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.sortedKeys]
            let data = try encoder.encode(value)
            FileHandle.standardOutput.write(data)
            FileHandle.standardOutput.write(Data([0x0A]))
        } catch {
            let fallback = "{\"error\":\"Could not encode sidecar response.\"}\n"
            FileHandle.standardOutput.write(Data(fallback.utf8))
        }
    }
}
