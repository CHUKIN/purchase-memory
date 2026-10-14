"""Unit tests for the Haiku + Azure box hybrid and the box verdict (no network)."""
import unittest

from ocr_compare import hybrid, report
from ocr_compare.common import load_score_items

si = load_score_items()


def item(name, total, box=None):
    return {"name": name, "lineTotal": total, "box": box}


def box(y):
    return [0.2, y, 0.6, 0.03]


HAIKU = [item("Mleko 3,2%", 3.49), item("Chleb żytni", 5.99), item("Masło", 5.99), item("OPUST", -1.0)]
AZURE = [item("MLEKO 3,2% 1L", 3.49, box(0.30)), item("CHLEB ZYTNI", 5.99, box(0.35)),
         item("MASŁO EXTRA", 5.99, box(0.40))]


class MatcherTests(unittest.TestCase):
    def test_amount_takes_first_unused_in_order(self):
        self.assertEqual(hybrid.match_amount(HAIKU, AZURE), {0: 0, 1: 1, 2: 2})

    def test_amount_alone_swaps_lines_with_equal_prices_out_of_order(self):
        haiku = [item("Masło", 5.99), item("Chleb żytni", 5.99)]
        self.assertEqual(hybrid.match_amount(haiku, AZURE), {0: 1, 1: 2})  # wrong rows
        self.assertEqual(hybrid.match_name_amount(haiku, AZURE, si.similar), {0: 2, 1: 1})

    def test_name_is_one_to_one_and_ignores_unlike_names(self):
        result = hybrid.match_name(HAIKU, AZURE, si.similar)
        self.assertEqual(result, {0: 0, 1: 1, 2: 2})
        self.assertNotIn(3, result)

    def test_name_amount_needs_some_name_overlap_for_an_amount_match(self):
        haiku = [item("Zupełnie inna rzecz", 3.49)]
        self.assertEqual(hybrid.match_name_amount(haiku, AZURE, si.similar), {})

    def test_aligned_keeps_receipt_order(self):
        # "Masło" price misread by Azure; order + names still align all three.
        azure = [AZURE[0], AZURE[1], item("MASŁO EXTRA", 9.99, box(0.40))]
        self.assertEqual(hybrid.match_aligned(HAIKU, azure, si.similar), {0: 0, 1: 1, 2: 2})
        # An amount-only candidate behind an already used row is not taken out of order.
        azure = [item("PODATEK", 5.99, box(0.10)), AZURE[0], AZURE[1]]
        self.assertEqual(hybrid.match_aligned(HAIKU[:2], azure, si.similar), {0: 1, 1: 2})

    def test_aligned_handles_empty_lists(self):
        self.assertEqual(hybrid.match_aligned([], AZURE, si.similar), {})
        self.assertEqual(hybrid.match_aligned(HAIKU, [], si.similar), {})


class HybridiseTests(unittest.TestCase):
    LINES = [{"text": "MLEKO 3,2% 1L", "box": box(0.30)}, {"text": "OPUST", "box": [0.2, 0.45, 0.2, 0.02]},
             {"text": "-1,00", "box": [0.7, 0.47, 0.1, 0.02]}, {"text": "SUMA PLN 14,47", "box": box(0.6)}]

    def test_items_and_totals_stay_haikus_only_boxes_change(self):
        haiku = {"total": 14.47, "totalBox": None, "items": [dict(i) for i in HAIKU]}
        azure = {"total": 99.0, "totalBox": box(0.6), "items": AZURE}
        out = hybrid.hybridise(haiku, azure, [], "aligned", si.similar, si.normalize)
        self.assertEqual([(i["name"], i["lineTotal"]) for i in out["items"]],
                         [(i["name"], i["lineTotal"]) for i in HAIKU])
        self.assertEqual(out["total"], 14.47)
        self.assertEqual(out["totalBox"], box(0.6))
        self.assertEqual([i["boxSource"] for i in out["items"]], ["azure-item"] * 3 + [None])
        self.assertIsNone(haiku["items"][0]["box"])  # input not mutated

    def test_lines_fallback_joins_name_line_with_price_line_below(self):
        haiku = {"total": 14.47, "items": [dict(i) for i in HAIKU]}
        out = hybrid.hybridise(haiku, {"items": AZURE}, self.LINES, "aligned+lines", si.similar, si.normalize)
        discount = out["items"][3]
        self.assertEqual(discount["boxSource"], "azure-line")
        for got, expected in zip(discount["box"], [0.2, 0.45, 0.6, 0.04]):
            self.assertAlmostEqual(got, expected)

    def test_own_box_is_the_last_fallback(self):
        haiku = {"total": 1.0, "items": [item("Nic podobnego", 7.77, box(0.9))]}
        plain = hybrid.hybridise(haiku, {"items": AZURE}, [], "aligned+lines", si.similar, si.normalize)
        own = hybrid.hybridise(haiku, {"items": AZURE}, [], "aligned+lines+own", si.similar, si.normalize)
        self.assertIsNone(plain["items"][0]["box"])
        self.assertEqual((own["items"][0]["box"], own["items"][0]["boxSource"]), (box(0.9), "haiku"))

    def test_shift_boxes_moves_every_box_to_another_line(self):
        records = {"01": {"configuration": "x", "parsed": {"items": [item("a", 1, box(0.1)), item("b", 2, box(0.2))]}}}
        shifted = hybrid.shift_boxes(records, 1, "cal")
        self.assertEqual([i["box"] for i in shifted["01"]["parsed"]["items"]], [box(0.2), box(0.1)])
        self.assertEqual(records["01"]["parsed"]["items"][0]["box"], box(0.1))


class BoxVerdictTests(unittest.TestCase):
    WORDS = [{"text": "Mleko", "box": [0.2, 0.30, 0.1, 0.02]}, {"text": "3,49", "box": [0.7, 0.30, 0.1, 0.02]},
             {"text": "Chleb", "box": [0.2, 0.40, 0.1, 0.02]}, {"text": "5,99", "box": [0.7, 0.40, 0.1, 0.02]},
             {"text": "SUMA", "box": [0.2, 0.60, 0.1, 0.02]}]
    GT = [{"name": "Mleko", "lineTotal": 3.49}, {"name": "Chleb", "lineTotal": 5.99}]

    def verdict(self, gt_index, name, total, b):
        from ocr_compare import boxes
        check = boxes.check_box({"name": name, "lineTotal": total, "box": b}, self.WORDS, si.normalize)
        return report.box_verdict(self.GT, gt_index, b, check, self.WORDS, si.normalize)

    def test_right_other_line_and_miss(self):
        self.assertEqual(self.verdict(0, "Mleko", 3.49, [0.15, 0.295, 0.7, 0.03]), "right")
        self.assertEqual(self.verdict(0, "Mleko", 3.49, [0.15, 0.395, 0.7, 0.03]), "other-line")
        self.assertEqual(self.verdict(0, "Mleko", 3.49, [0.15, 0.595, 0.7, 0.03]), "miss")

    def test_price_only_hit_next_to_another_name_is_ambiguous(self):
        # Recognised name differs from what is printed, the price matches, Chleb is in the box too.
        self.assertEqual(self.verdict(0, "Xyzzy", 5.99, [0.15, 0.395, 0.7, 0.03]), "ambiguous")


if __name__ == "__main__":
    unittest.main()
