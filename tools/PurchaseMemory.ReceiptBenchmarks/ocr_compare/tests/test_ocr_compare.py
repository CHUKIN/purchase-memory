"""Unit tests for the network-free helpers. Run:
python3 -m unittest discover -s tools/PurchaseMemory.ReceiptBenchmarks/ocr_compare/tests -t tools/PurchaseMemory.ReceiptBenchmarks
"""
import json
import tempfile
import pathlib
import types
import unittest

from ocr_compare import azure_receipt, boxes, haiku_matrix, tesseract_ocr
from ocr_compare.common import load_score_items

si = load_score_items()


def word(text, x, y, w=0.05, h=0.02):
    return {"text": text, "box": [x, y, w, h]}


class BoxTests(unittest.TestCase):
    def test_polygon_to_box_normalises_pixels(self):
        self.assertEqual(boxes.polygon_to_box([10, 20, 110, 20, 110, 40, 10, 40], 200, 100), [0.05, 0.2, 0.5, 0.2])

    def test_plausible_rejects_missing_huge_and_outside(self):
        self.assertTrue(boxes.plausible([0.1, 0.3, 0.6, 0.03]))
        self.assertFalse(boxes.plausible(None))
        self.assertFalse(boxes.plausible([0.1, 0.1, 0.8, 0.5]))   # half the receipt
        self.assertFalse(boxes.plausible([0.8, 0.3, 0.5, 0.03]))  # runs off the right edge

    def test_hit_by_name_and_by_price_and_miss(self):
        words = [word("Placki", 0.2, 0.40), word("ziemniaczane", 0.26, 0.40),
                 word("1", 0.2, 0.43), word("x17,99", 0.25, 0.43), word("17,99B", 0.6, 0.43),
                 word("Pudelko", 0.2, 0.60)]
        name_box = [0.18, 0.395, 0.5, 0.03]
        price_row = [0.18, 0.428, 0.5, 0.025]
        item = {"name": "Placki ziemniaczane 8 szt.", "lineTotal": 17.99}
        self.assertTrue(boxes.check_box({**item, "box": name_box}, words, si.normalize)["nameHit"])
        price = boxes.check_box({**item, "box": price_row}, words, si.normalize)
        self.assertTrue(price["priceHit"] and price["hit"])
        miss = boxes.check_box({**item, "box": [0.18, 0.595, 0.5, 0.03]}, words, si.normalize)
        self.assertTrue(miss["plausible"])
        self.assertFalse(miss["hit"])

    def test_no_reference_words_means_no_hit(self):
        result = boxes.check_box({"name": "X", "lineTotal": 1, "box": [0.1, 0.1, 0.5, 0.02]}, None, si.normalize)
        self.assertEqual((result["present"], result["plausible"], result["hit"]), (True, True, False))

    def test_price_token_does_not_match_inside_longer_number(self):
        self.assertTrue(boxes.price_printed(5.98, "1 x5,98 5,98B"))
        self.assertFalse(boxes.price_printed(5.98, "125,98"))


class TesseractParserTests(unittest.TestCase):
    TSV = "\n".join([
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext",
        "1\t1\t0\t0\t0\t0\t0\t0\t200\t100\t-1\t",
        "5\t1\t1\t1\t1\t1\t10\t10\t40\t5\t90\tPARAGON",
        "5\t1\t1\t1\t1\t2\t60\t10\t40\t5\t90\tFISKALNY",
        "5\t1\t1\t1\t2\t1\t10\t20\t40\t5\t90\tPlacki",
        "5\t1\t1\t1\t3\t1\t10\t30\t20\t5\t90\t1",
        "5\t1\t1\t1\t3\t2\t40\t30\t30\t5\t90\tx17,99",
        "5\t1\t1\t1\t3\t3\t150\t30\t30\t5\t90\t17,99B",
        "5\t1\t1\t1\t4\t1\t10\t40\t40\t5\t90\tOPUST",
        "5\t1\t1\t1\t4\t2\t150\t40\t30\t5\t90\t2,00",
        "5\t1\t1\t1\t5\t1\t10\t50\t40\t5\t90\tPTU",
        "5\t1\t1\t1\t5\t2\t150\t50\t30\t5\t90\t1,20",
        "5\t1\t1\t1\t6\t1\t10\t60\t40\t5\t90\tSUMA",
        "5\t1\t1\t1\t6\t2\t60\t60\t30\t5\t90\tPLN",
        "5\t1\t1\t1\t6\t3\t150\t60\t30\t5\t90\t15,99",
    ])

    def test_parse_tsv_groups_words_into_normalised_lines(self):
        words, lines = tesseract_ocr.parse_tsv(self.TSV, 1, 1)
        self.assertEqual(len(words), 13)
        self.assertEqual(lines[0]["text"], "PARAGON FISKALNY")
        for got, expected in zip(lines[0]["box"], [0.05, 0.1, 0.45, 0.05]):
            self.assertAlmostEqual(got, expected)

    def test_parse_receipt_pairs_name_row_with_price_row_and_reads_total(self):
        _, lines = tesseract_ocr.parse_tsv(self.TSV, 1, 1)
        parsed = tesseract_ocr.parse_receipt(lines, si.fold)
        self.assertEqual([(i["name"], i["lineTotal"]) for i in parsed["items"]], [("Placki", 17.99), ("OPUST", -2.0)])
        self.assertEqual(parsed["total"], 15.99)
        self.assertEqual(parsed["items"][0]["box"][1], 0.2)               # starts at the name row
        self.assertAlmostEqual(sum(parsed["items"][0]["box"][1::2]), 0.35)  # ends at the price row

    def test_quantity_variants_are_stripped_from_names(self):
        for text in ("C_SZPINAK 1*5,99= 5,99 C", "C_SZPINAK Iszt*5,99= 5,99 C", "C_SZPINAK 0,609k9*5,99= 5,99 C"):
            lines = [{"text": text, "box": [0, 0.1, 1, 0.02]}]
            parsed = tesseract_ocr.parse_receipt(lines, si.fold)
            self.assertEqual(parsed["items"][0]["name"], "C_SZPINAK", text)


class AzureMappingTests(unittest.TestCase):
    def test_items_total_and_word_boxes(self):
        response = {"analyzeResult": {
            "pages": [{"width": 100, "height": 200, "words": [
                {"content": "Mleko", "polygon": [10, 20, 30, 20, 30, 30, 10, 30]}]}],
            "documents": [{"fields": {
                "MerchantName": {"valueString": "Shop"},
                "Total": {"valueCurrency": {"amount": 4.5},
                          "boundingRegions": [{"pageNumber": 1, "polygon": [50, 100, 90, 100, 90, 110, 50, 110]}]},
                "Items": {"valueArray": [
                    {"valueObject": {"Description": {"valueString": "Mleko"},
                                     "TotalPrice": {"valueCurrency": {"amount": 3.5}}},
                     "boundingRegions": [{"pageNumber": 1, "polygon": [10, 20, 90, 20, 90, 40, 10, 40]}]},
                    {"valueObject": {"Description": {"content": "Bułka"}, "Price": {"valueCurrency": {"amount": 1.0}}}}]}}}]}}
        parsed = azure_receipt.to_parsed(response)
        self.assertEqual(parsed["total"], 4.5)
        self.assertEqual(parsed["items"][0], {"name": "Mleko", "quantity": None, "unitPrice": None,
                                              "lineTotal": 3.5, "box": [0.1, 0.1, 0.8, 0.1]})
        self.assertEqual((parsed["items"][1]["lineTotal"], parsed["items"][1]["box"]), (1.0, None))
        self.assertEqual(azure_receipt.words(response), [{"text": "Mleko", "box": [0.1, 0.1, 0.2, 0.05]}])

    def test_cache_is_used_without_credentials(self):
        with tempfile.TemporaryDirectory() as tmp:
            cache = pathlib.Path(tmp)
            (cache / "receipt_01.json").write_text(json.dumps({"meta": {"seconds": 1}, "response": {}}))
            client = azure_receipt.AzureClient(cache, max_analyze_calls=0)
            entry, fresh = client.cached_or_analyze("01", b"x", allow_calls=True)
            self.assertFalse(fresh)
            self.assertEqual(entry["meta"]["seconds"], 1)
            self.assertEqual(client.cached_or_analyze("02", b"x", allow_calls=False), (None, False))


class HaikuMatrixTests(unittest.TestCase):
    def test_matrix_has_three_prompts_by_four_levels(self):
        self.assertEqual(len(haiku_matrix.variants()), 12)
        defs = haiku_matrix.prompts()
        bbox = defs["bbox-required"]
        self.assertIn("WYMAGANY", bbox[0])
        self.assertEqual(bbox[1]["properties"]["items"]["items"]["properties"]["boundingBox"]["type"], "object")
        self.assertNotIn("maxItems", json.dumps(defs["prod-v3"][1]))

    def test_full_structured_discounts_become_negative_lines_after_their_item(self):
        data = {"merchantName": "S", "purchasedAt": None, "currency": "PLN", "receiptTotal": 3.0,
                "receiptTotalBox": {"x": 0.1, "y": 0.9, "width": 0.3, "height": 0.02},
                "items": [{"name": "A", "lineTotal": 2.0, "box": {"x": 0.1, "y": 0.2, "width": 0.5, "height": 0.02}},
                          {"name": "B", "lineTotal": 2.0, "box": None}],
                "discounts": [{"label": "OPUST", "amount": 1.0, "appliesToItem": 0, "box": None}]}
        parsed = haiku_matrix.to_parsed(data, "full")
        self.assertEqual([(i["name"], i["lineTotal"]) for i in parsed["items"]], [("A", 2.0), ("OPUST", -1.0), ("B", 2.0)])
        self.assertEqual(parsed["items"][0]["box"], [0.1, 0.2, 0.5, 0.02])

    def test_production_v4_contract_is_the_measured_full_structured_variant(self):
        # The backend's contract v4 must stay byte-identical to what was measured.
        source = (haiku_matrix.ROOT / "src" / "PurchaseMemory.Application" / "Receipts"
                  / "ReceiptStructuredAnalysisContract.cs").read_text()
        self.assertEqual(haiku_matrix._raw_literal(source, "BasePrompt"), haiku_matrix.FULL_PROMPT)
        self.assertEqual(json.loads(haiku_matrix._raw_literal(source, "JsonSchema")), haiku_matrix.FULL_SCHEMA)

    def test_estimate_is_positive_and_worst_case_bounds_it(self):
        rows = haiku_matrix.estimate(["01", "02"], repeats=2)
        self.assertEqual(len(rows), 12)
        for row in rows:
            self.assertEqual(row["calls"], 4)
            self.assertGreater(row["estimatedUsd"], 0)
            self.assertLess(row["estimatedUsd"], row["worstCaseUsd"])

    def test_run_refuses_without_budget_and_stops_at_budget(self):
        with self.assertRaises(SystemExit):
            haiku_matrix.run(pathlib.Path("."), pathlib.Path("."), ["01"], [("prod-v3", "low")], 1, None,
                             client=object())

        class FakeStream:
            def __enter__(self):
                return self

            def __exit__(self, *exc):
                return False

            def get_final_message(self):
                body = json.dumps({"merchantName": None, "purchasedAt": None, "currency": None,
                                   "receiptTotal": 1.0, "receiptTotalBoundingBox": None, "rawLines": [],
                                   "items": [{"name": "A", "quantity": 1, "unit": None, "unitPrice": 1.0,
                                              "lineTotal": 1.0, "boundingBox": None}]})
                return types.SimpleNamespace(
                    usage=types.SimpleNamespace(input_tokens=3000, output_tokens=2000), stop_reason="end_turn",
                    content=[types.SimpleNamespace(type="text", text=body)])

        calls = []
        fake = types.SimpleNamespace(messages=types.SimpleNamespace(
            stream=lambda **request: calls.append(request) or FakeStream()))
        with tempfile.TemporaryDirectory() as tmp:
            images, out = pathlib.Path(tmp) / "img", pathlib.Path(tmp) / "out"
            images.mkdir()
            for n in ("01", "02", "03"):
                (images / f"receipt_{n}.jpg").write_bytes(b"jpeg")
            worst = 2 * haiku_matrix.MEASURED_INPUT_TOKENS * haiku_matrix.PRICE_IN \
                + haiku_matrix.MAX_TOKENS * haiku_matrix.PRICE_OUT
            # Budget fits two worst cases only: two calls, then stop.
            spent = haiku_matrix.run(images, out, ["01", "02", "03"], [("prod-v3", "off")], 1,
                                     budget_usd=worst + 0.0013 + 1e-6, client=fake)
            self.assertEqual(len(calls), 2)
            self.assertEqual(calls[0]["thinking"], {"type": "disabled"})
            self.assertAlmostEqual(spent, 2 * 0.0013)
            record = json.loads(next((out / "runs" / "haiku-prod-v3-off").glob("*__01.json")).read_text())
            self.assertEqual(record["status"], "success")
            self.assertEqual(record["parsed"]["items"][0]["lineTotal"], 1.0)


if __name__ == "__main__":
    unittest.main()
