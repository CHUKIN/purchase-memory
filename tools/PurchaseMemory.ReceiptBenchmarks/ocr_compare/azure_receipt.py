"""Azure AI Document Intelligence `prebuilt-receipt` over REST (v4.0 GA, 2024-11-30).

Raw responses are cached per receipt (cache/azure/receipt_NN.json) and a call
ledger (cache/azure/_calls.json) counts every analyze POST and result GET ever
sent, so reruns cost nothing and the analyze cap holds across runs.

Rate limits: the free F0 tier allows about 1 request/s and 20 analyze calls per
minute, so analyze POSTs are spaced >= 3.1 s and any two requests >= 1.1 s.
The key and endpoint come only from AZURE_DOCINTEL_KEY / AZURE_DOCINTEL_ENDPOINT
and are never printed; errors show the HTTP status and Azure's error code.
"""
import json
import os
import time
import urllib.error
import urllib.request

from . import boxes
from .common import read_json, write_json

API_VERSION = "2024-11-30"
MODEL_ID = "prebuilt-receipt"
# S0 list price for prebuilt models (Document Intelligence pricing page, 0-1M pages tier).
PAID_USD_PER_PAGE = 10.0 / 1000
ANALYZE_SPACING_S = 3.1
REQUEST_SPACING_S = 1.1
FIRST_POLL_S = 3.0
POLL_EVERY_S = 1.5
MAX_POLLS = 30


class AzureError(Exception):
    pass


class AzureClient:
    def __init__(self, cache_dir, max_analyze_calls, locale=None):
        self.cache_dir = cache_dir
        self.ledger_path = cache_dir / "_calls.json"
        self.max_analyze_calls = max_analyze_calls
        self.locale = locale
        self._last_request = 0.0
        self._last_analyze = 0.0

    # ---- ledger -----------------------------------------------------------
    def ledger(self):
        return read_json(self.ledger_path) if self.ledger_path.exists() else {"analyzeCalls": 0, "getCalls": 0}

    def _count(self, key):
        ledger = self.ledger()
        ledger[key] += 1
        write_json(self.ledger_path, ledger)

    # ---- HTTP -------------------------------------------------------------
    def _credentials(self):
        endpoint = os.environ.get("AZURE_DOCINTEL_ENDPOINT", "").rstrip("/")
        key = os.environ.get("AZURE_DOCINTEL_KEY", "")
        if not endpoint or not key:
            raise AzureError("AZURE_DOCINTEL_ENDPOINT / AZURE_DOCINTEL_KEY are not set "
                             "(run through zsh -ic so ~/.zshrc is loaded)")
        return endpoint, key

    def _wait(self, spacing, since):
        delay = since + spacing - time.monotonic()
        if delay > 0:
            time.sleep(delay)

    def _send(self, request):
        self._wait(REQUEST_SPACING_S, self._last_request)
        self._last_request = time.monotonic()
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.status, response.headers, response.read()
        except urllib.error.HTTPError as error:
            return error.code, error.headers, error.read()

    @staticmethod
    def _error_text(status, body):
        try:
            err = json.loads(body).get("error", {})
            return f"HTTP {status} {err.get('code', '')}: {err.get('message', '')[:200]}"
        except (ValueError, AttributeError):
            return f"HTTP {status}"

    def analyze(self, image_bytes):
        """POST the image, poll the result. Returns (response json, seconds, http calls)."""
        endpoint, key = self._credentials()
        query = f"api-version={API_VERSION}" + (f"&locale={self.locale}" if self.locale else "")
        url = f"{endpoint}/documentintelligence/documentModels/{MODEL_ID}:analyze?{query}"
        headers = {"Ocp-Apim-Subscription-Key": key, "Content-Type": "application/octet-stream"}
        calls = 0
        for attempt in range(3):
            if self.ledger()["analyzeCalls"] >= self.max_analyze_calls:
                raise AzureError(f"analyze cap reached ({self.max_analyze_calls} calls in the ledger)")
            self._wait(ANALYZE_SPACING_S, self._last_analyze)
            self._last_analyze = time.monotonic()
            started = time.monotonic()
            status, response_headers, body = self._send(
                urllib.request.Request(url, data=image_bytes, headers=headers, method="POST"))
            self._count("analyzeCalls")
            calls += 1
            if status == 429:
                time.sleep(float(response_headers.get("Retry-After", "10")))
                continue
            if status != 202:
                raise AzureError(self._error_text(status, body))
            operation = response_headers.get("Operation-Location")  # case-insensitive lookup
            break
        else:
            raise AzureError("rate limited three times")

        time.sleep(FIRST_POLL_S)
        for _ in range(MAX_POLLS):
            status, response_headers, body = self._send(
                urllib.request.Request(operation, headers={"Ocp-Apim-Subscription-Key": key}))
            self._count("getCalls")
            calls += 1
            if status == 429:
                time.sleep(float(response_headers.get("Retry-After", "5")))
                continue
            if status != 200:
                raise AzureError(self._error_text(status, body))
            result = json.loads(body)
            if result.get("status") == "succeeded":
                return result, time.monotonic() - started, calls
            if result.get("status") == "failed":
                raise AzureError(f"analysis failed: {json.dumps(result.get('error'))[:300]}")
            time.sleep(POLL_EVERY_S)
        raise AzureError("analysis did not finish in time")

    def cached_or_analyze(self, receipt_id, image_bytes, allow_calls):
        """Cached entry {"meta": ..., "response": ...}; calls Azure only when allowed."""
        path = self.cache_dir / f"receipt_{receipt_id}.json"
        if path.exists():
            return read_json(path), False
        if not allow_calls:
            return None, False
        response, seconds, calls = self.analyze(image_bytes)
        entry = {"meta": {"apiVersion": API_VERSION, "modelId": MODEL_ID, "locale": self.locale,
                          "seconds": round(seconds, 2), "httpCalls": calls,
                          "fetchedAt": time.strftime("%Y-%m-%dT%H:%M:%S%z")},
                 "response": response}
        write_json(path, entry)
        return entry, True


# ---- response mapping -------------------------------------------------------

def _page(response):
    pages = response.get("analyzeResult", {}).get("pages") or []
    return pages[0] if pages else {"width": 0, "height": 0, "words": [], "lines": []}


def _amount(field):
    if not field:
        return None
    if "valueCurrency" in field:
        return field["valueCurrency"].get("amount")
    return field.get("valueNumber")


def _region_box(field, page):
    regions = (field or {}).get("boundingRegions") or []
    return boxes.union([boxes.polygon_to_box(r.get("polygon"), page["width"], page["height"])
                        for r in regions if r.get("pageNumber", 1) == 1])


def words(response):
    """Azure's word layer as reference words for the box check."""
    page = _page(response)
    return [{"text": w["content"], "box": boxes.polygon_to_box(w.get("polygon"), page["width"], page["height"])}
            for w in page.get("words") or [] if w.get("polygon")]


def to_parsed(response):
    """prebuilt-receipt fields -> the common `parsed` shape (items keep their box)."""
    page = _page(response)
    documents = response.get("analyzeResult", {}).get("documents") or []
    fields = documents[0].get("fields", {}) if documents else {}
    items = []
    for entry in (fields.get("Items") or {}).get("valueArray") or []:
        value = entry.get("valueObject") or {}
        description = value.get("Description") or {}
        name = description.get("valueString") or description.get("content") or entry.get("content") or ""
        quantity = (value.get("Quantity") or {}).get("valueNumber")
        unit_price = _amount(value.get("Price"))
        line_total = _amount(value.get("TotalPrice"))
        if line_total is None and unit_price is not None and quantity in (None, 1):
            line_total = unit_price
        items.append({"name": name.replace("\n", " "), "quantity": quantity, "unitPrice": unit_price,
                      "lineTotal": line_total, "box": _region_box(entry, page)})
    date = (fields.get("TransactionDate") or {}).get("valueDate")
    return {"merchant": (fields.get("MerchantName") or {}).get("valueString"),
            "date": date, "currency": None,
            "total": _amount(fields.get("Total")),
            "totalBox": _region_box(fields.get("Total"), page),
            "items": items}
