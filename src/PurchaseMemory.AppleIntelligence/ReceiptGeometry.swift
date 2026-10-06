import CoreGraphics
import Foundation

/// A single OCR fragment placed on the page. `bounds` is the Vision normalized
/// bounding box (0..1, y axis pointing UP, 0 = bottom of the page).
protocol ReceiptGeometryFragment {
    var text: String { get }
    var bounds: CGRect { get }
}

/// Fragments that share one printed line of the receipt.
/// `anchorY` is the running mean of the member mid-Y values in page space
/// (0..1, y axis pointing DOWN, 0 = top of the receipt) after skew correction.
struct ReceiptGeometryRow<Fragment: ReceiptGeometryFragment> {
    var anchorY: CGFloat
    var fragments: [Fragment]
    /// Members recognised as a price cell of the amount column; set by `analyze`.
    var priceCellCount = 0
}

/// Outcome of the skew search: the chosen correction plus the rows built with it.
struct ReceiptGeometryResult<Fragment: ReceiptGeometryFragment> {
    /// Vertical drift, in 0..1000 page units, accumulated across the full page width.
    let skew: CGFloat
    let rows: [ReceiptGeometryRow<Fragment>]
    let rowsWithSinglePriceCell: Int
    let rowsWithMultiplePriceCells: Int
    /// True when the optimum sits at the edge of the search range and the real
    /// skew may well be larger than the search could express.
    let reachedSearchBoundary: Bool
    /// Median fragment height in 0..1 page units; the row tolerance is derived from it.
    let medianFragmentHeight: CGFloat
}

/// Groups OCR fragments into receipt rows.
///
/// Deliberate properties; the first two were learned from `research/ocr-geometry/drafts`,
/// the third from the live stage-2 run on receipt 4:
///
/// 1. A row is anchored by the running MEAN of its members' mid-Y, and the
///    tolerance comes from the MEDIAN fragment height of the whole receipt.
///    Neither grows as fragments are added, so a row cannot snowball into half
///    the receipt the way a `union`-grown bounding box does.
/// 2. The photo is almost never perfectly square to the page, so the vertical
///    drift across the page width is searched for before grouping.
/// 3. A fragment joins the NEAREST row that has no member in the same column, not
///    simply the previous row, and multi-line boxes never move an anchor — see
///    `groupIntoRows`.
enum ReceiptGeometry {
    /// Half-width of the skew search, in 0..1000 page units. Deliberately generous:
    /// the research draft started at 30 and found it too small for real photos.
    static let searchLimit: CGFloat = 60
    static let searchStep: CGFloat = 0.5
    static let toleranceFactor: CGFloat = 0.6

    /// A printed "<count> x <unit price>" cell — the marker of a product line.
    ///
    /// Accepts `x`, `X` and `×` (receipts print `1 x7,55` and `1 X7,55`, Vision
    /// sometimes returns the multiplication sign) and requires TWO decimal digits,
    /// optionally split by one space as Vision does (`x2, 13`). Money always has two
    /// decimals; a package size such as `2X1,5L` or `10X1,5` has one and no longer
    /// matches. A name that does carry two decimals (`2X1,50L`) is still rejected by
    /// the amount-column check in `priceCellColumnLeftEdge`, so the pattern alone is
    /// not the only guard.
    ///
    /// The pattern is a compile-time constant, so a failure here is a programming
    /// error, not a runtime condition: crash instead of silently reporting "no price
    /// cell anywhere", which would collapse the skew search to 0 without a trace.
    private static let priceCellExpression: NSRegularExpression = {
        do {
            return try NSRegularExpression(pattern: #"[xX×]\s?\d+[.,]\s?\d\d(?!\d)"#)
        } catch {
            fatalError("ReceiptGeometry price-cell pattern does not compile: \(error)")
        }
    }()

    /// Share of the text width, measured left from the amount column's right edge,
    /// that still counts as the amount column. Product names end in the left half of
    /// a receipt, the "<count> x <price> <total>" block ends at the right margin.
    static let amountColumnWidthShare: CGFloat = 0.3

    static func containsPriceCellPattern(_ text: String) -> Bool {
        let range = NSRange(text.startIndex..<text.endIndex, in: text)
        return priceCellExpression.firstMatch(in: text, range: range) != nil
    }

    /// Left boundary (0..1 page units) a fragment's RIGHT edge has to reach to count as
    /// a price cell. The amount column is located as the median right edge of all
    /// pattern matches — most of them are real prices, so a stray match inside a
    /// product name cannot move it — and it extends `amountColumnWidthShare` of the
    /// text width to the left. Horizontal positions are not affected by the skew, so
    /// this is computed once per receipt, outside the skew search.
    static func priceCellColumnLeftEdge<Fragment: ReceiptGeometryFragment>(
        _ fragments: [Fragment]
    ) -> CGFloat {
        let matchEdges = fragments
            .filter { containsPriceCellPattern($0.text) }
            .map(\.bounds.maxX)
            .sorted()
        guard !matchEdges.isEmpty,
              let textLeft = fragments.map(\.bounds.minX).min(),
              let textRight = fragments.map(\.bounds.maxX).max() else {
            return 0
        }
        let columnRight = matchEdges[matchEdges.count / 2]
        return columnRight - amountColumnWidthShare * (textRight - textLeft)
    }

    static func isPriceCell<Fragment: ReceiptGeometryFragment>(
        _ fragment: Fragment,
        columnLeftEdge: CGFloat
    ) -> Bool {
        fragment.bounds.maxX >= columnLeftEdge && containsPriceCellPattern(fragment.text)
    }

    /// Upper middle height, not a true median: on an even count it returns the higher
    /// of the two middle values instead of their average. Kept deliberately — every
    /// measurement in `ПЛАН_КОНВЕЙЕР_OCR_ГЕОМЕТРИЯ.md` was taken with this behaviour.
    static func medianFragmentHeight<Fragment: ReceiptGeometryFragment>(
        _ fragments: [Fragment]
    ) -> CGFloat {
        let heights = fragments.map(\.bounds.height).sorted()
        guard !heights.isEmpty else {
            return 0.01
        }
        return heights[heights.count / 2]
    }

    /// Mid-Y of a fragment in page space (0 = top) after applying `skew`.
    static func correctedMidY<Fragment: ReceiptGeometryFragment>(
        _ fragment: Fragment,
        skew: CGFloat
    ) -> CGFloat {
        (1 - fragment.bounds.midY) - skew / 1000 * fragment.bounds.minX
    }

    /// A fragment taller than this many median heights spans more than one printed
    /// line (Vision merges a smudged pair of lines into one box). Its mid-Y falls
    /// BETWEEN two lines, so it must not seed or move a row anchor.
    static let tallFragmentFactor: CGFloat = 2.0

    /// Share of the narrower fragment's width two fragments may overlap by and still sit
    /// side by side on one printed line; Vision boxes of neighbours touch or slightly
    /// overlap, fragments of two different lines stacked in one column overlap fully.
    static let sameLineOverlapShare: CGFloat = 0.3

    static func overlapsHorizontally<Fragment: ReceiptGeometryFragment>(
        _ first: Fragment,
        _ second: Fragment
    ) -> Bool {
        let overlap = min(first.bounds.maxX, second.bounds.maxX) - max(first.bounds.minX, second.bounds.minX)
        let narrower = min(first.bounds.width, second.bounds.width)
        return overlap > sameLineOverlapShare * narrower
    }

    /// Groups fragments into rows in two passes.
    ///
    /// Pass 1 places regular-height fragments top to bottom. Each joins the row whose
    /// anchor is NEAREST within `tolerance` AND that has no member in the same
    /// horizontal span. Joining simply `rows.last` glued two printed lines together on
    /// receipt 4 (`PAPRYKA CZERWONA` + `PAPRYK CORNO`): the tolerance (0.6 median
    /// heights) is close to the line pitch, so one fragment drifting towards the next
    /// line dragged the anchor far enough to swallow that whole line. Nearest-anchor
    /// alone does not fix it — when the next line starts, its row does not exist yet.
    /// The overlap rule does: two fragments of one printed line never sit on top of
    /// each other horizontally, so a fragment under an existing member must belong to
    /// another line.
    ///
    /// Pass 2 places tall, multi-line fragments without moving any anchor (see
    /// `rowInsideSpan`), or gives them a row of their own if nothing fits.
    static func groupIntoRows<Fragment: ReceiptGeometryFragment>(
        _ fragments: [Fragment],
        skew: CGFloat,
        tolerance: CGFloat,
        medianHeight: CGFloat,
        separatesStackedFragments: Bool = true
    ) -> [ReceiptGeometryRow<Fragment>] {
        let tallLimit = medianHeight * tallFragmentFactor
        let placed = fragments
            .map { (y: correctedMidY($0, skew: skew), fragment: $0) }
            .sorted { $0.y < $1.y }

        func nearestRow(
            to y: CGFloat,
            in rows: [ReceiptGeometryRow<Fragment>],
            excludingRowsUnder fragment: Fragment?
        ) -> Int? {
            var best: (index: Int, distance: CGFloat)?
            for (index, row) in rows.enumerated() {
                let distance = abs(row.anchorY - y)
                guard distance <= tolerance, distance < (best?.distance ?? .infinity) else {
                    continue
                }
                if let fragment, row.fragments.contains(where: { overlapsHorizontally($0, fragment) }) {
                    continue
                }
                best = (index, distance)
            }
            return best?.index
        }

        // A tall box covers several printed lines, so its mid-Y says little about which
        // one it belongs to. It goes to the row, among those whose anchor lies inside
        // the box's vertical span, that has no member in the same column — that is the
        // line the box actually carries text for. On receipt 4 Vision merged
        // `PAPRYK CORNO` and the first OPUST line into one box, whose other half is
        // a separate `PAPRYK CORNO` fragment; the box belongs with the orphan `-4,38C`.
        func rowInsideSpan(
            of fragment: Fragment,
            y: CGFloat,
            in rows: [ReceiptGeometryRow<Fragment>]
        ) -> Int? {
            let halfHeight = fragment.bounds.height / 2
            var best: (index: Int, distance: CGFloat)?
            for (index, row) in rows.enumerated() {
                let distance = abs(row.anchorY - y)
                guard distance <= max(halfHeight, tolerance),
                      distance < (best?.distance ?? .infinity),
                      !row.fragments.contains(where: { overlapsHorizontally($0, fragment) }) else {
                    continue
                }
                best = (index, distance)
            }
            return best?.index
        }

        var rows: [ReceiptGeometryRow<Fragment>] = []
        for item in placed where item.fragment.bounds.height <= tallLimit {
            let stackCheck = separatesStackedFragments ? item.fragment : nil
            if let index = nearestRow(to: item.y, in: rows, excludingRowsUnder: stackCheck) {
                let count = CGFloat(rows[index].fragments.count)
                rows[index].anchorY = (rows[index].anchorY * count + item.y) / (count + 1)
                rows[index].fragments.append(item.fragment)
            } else {
                rows.append(ReceiptGeometryRow(anchorY: item.y, fragments: [item.fragment]))
            }
        }
        for item in placed where item.fragment.bounds.height > tallLimit {
            let target = separatesStackedFragments
                ? rowInsideSpan(of: item.fragment, y: item.y, in: rows)
                : nearestRow(to: item.y, in: rows, excludingRowsUnder: nil)
            if let index = target {
                rows[index].fragments.append(item.fragment)
            } else {
                rows.append(ReceiptGeometryRow(anchorY: item.y, fragments: [item.fragment]))
            }
        }

        return rows
            .sorted { $0.anchorY < $1.anchorY }
            .map { row in
                var sorted = row
                sorted.fragments.sort { $0.bounds.minX < $1.bounds.minX }
                return sorted
            }
    }

    static func countPriceCells<Fragment: ReceiptGeometryFragment>(
        _ row: ReceiptGeometryRow<Fragment>,
        columnLeftEdge: CGFloat
    ) -> Int {
        row.fragments.reduce(into: 0) { total, fragment in
            total += isPriceCell(fragment, columnLeftEdge: columnLeftEdge) ? 1 : 0
        }
    }

    /// Searches the skew that lines product names up with their prices.
    ///
    /// Objective: rows holding exactly one price cell, minus twice the rows holding
    /// several (a row that swallowed two prices is worse than a row that missed one).
    /// On a tie the SMALLEST correction wins: on a short receipt the objective is
    /// flat, and without that rule a flat objective drifts to the search boundary.
    ///
    /// The search groups WITHOUT the same-column rule. That rule makes a row with two
    /// price cells nearly impossible at any skew, which flattens the objective: on
    /// receipt 4 every skew from −60 to +52 then scored the same and the tie rule
    /// picked 0, shifting names one line against their prices. Without the rule a
    /// wrong skew still merges neighbouring price cells and is penalised, so the
    /// search keeps finding the tilt; the final rows are then built with the rule.
    static func analyze<Fragment: ReceiptGeometryFragment>(
        _ fragments: [Fragment]
    ) -> ReceiptGeometryResult<Fragment> {
        let medianHeight = medianFragmentHeight(fragments)
        let tolerance = medianHeight * toleranceFactor
        let columnLeftEdge = priceCellColumnLeftEdge(fragments)

        var bestSkew: CGFloat = 0
        var bestScore = Int.min

        let steps = Int((searchLimit * 2 / searchStep).rounded())
        for index in 0...steps {
            let skew = -searchLimit + CGFloat(index) * searchStep
            let rows = groupIntoRows(
                fragments, skew: skew, tolerance: tolerance, medianHeight: medianHeight,
                separatesStackedFragments: false)
            var single = 0
            var multiple = 0
            for row in rows {
                let priceCells = countPriceCells(row, columnLeftEdge: columnLeftEdge)
                if priceCells == 1 {
                    single += 1
                } else if priceCells > 1 {
                    multiple += 1
                }
            }
            let score = single - 2 * multiple
            let isBetter = score > bestScore
                || (score == bestScore && abs(skew) < abs(bestSkew))
            if isBetter {
                bestScore = score
                bestSkew = skew
            }
        }

        let rows = groupIntoRows(
            fragments, skew: bestSkew, tolerance: tolerance, medianHeight: medianHeight
        ).map { row in
            var counted = row
            counted.priceCellCount = countPriceCells(row, columnLeftEdge: columnLeftEdge)
            return counted
        }

        return ReceiptGeometryResult(
            skew: bestSkew,
            rows: rows,
            rowsWithSinglePriceCell: rows.filter { $0.priceCellCount == 1 }.count,
            rowsWithMultiplePriceCells: rows.filter { $0.priceCellCount > 1 }.count,
            reachedSearchBoundary: abs(bestSkew) >= searchLimit - searchStep,
            medianFragmentHeight: medianHeight
        )
    }
}
