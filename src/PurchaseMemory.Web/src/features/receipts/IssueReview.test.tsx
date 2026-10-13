import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { PurchaseDraftIssue, ReceiptReviewAuditEntry, ReceiptSnapshot } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { IssueReview } from "./IssueReview";

const mismatch: PurchaseDraftIssue = {
  type: "receiptTotalMismatch",
  itemPosition: null,
  itemName: null,
  recognizedValue: "9.00 PLN",
  suggestedFix: "Correct the receipt total or the line totals so that they match."
};

const missingPrice: PurchaseDraftIssue = {
  type: "missingItemPrice",
  itemPosition: 1,
  itemName: "Chleb",
  recognizedValue: "",
  suggestedFix: "Set the price for this item or remove it."
};

const snapshot = (overrides: Partial<ReceiptSnapshot> = {}): ReceiptSnapshot => ({
  merchantName: "Lidl",
  purchasedAt: "2026-08-24T17:12:00Z",
  currency: "PLN",
  receiptTotal: 10,
  items: [
    { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
    { name: "Chleb", quantity: 1, unit: null, unitPrice: 5, lineTotal: 5 }
  ],
  ...overrides
});

function renderReview(
  props: Partial<React.ComponentProps<typeof IssueReview>> = {},
  locale: "ru" | "en" = "ru"
) {
  const onSnapshotChange = vi.fn();
  const onOpenFullEditor = vi.fn();
  const onSave = vi.fn();
  render(
    <AppProviders locale={locale}>
      <IssueReview
        snapshot={snapshot()}
        onSnapshotChange={onSnapshotChange}
        issues={[]}
        onOpenFullEditor={onOpenFullEditor}
        onSave={onSave}
        {...props}
      />
    </AppProviders>
  );
  return { onSnapshotChange, onOpenFullEditor, onSave };
}

const photo = { url: "/api/receipts/1/image", croppable: true };

const percent = (value: string) => Number.parseFloat(value.replace("%", ""));

describe("IssueReview", () => {
  it("renders a receipt-level mismatch card with the recognized line sum", () => {
    renderReview({ issues: [mismatch] });

    expect(screen.getByRole("heading", { name: "Сумма строк не сходится с итогом чека" })).toBeInTheDocument();
    expect(screen.getByText("Сумма распознанных строк — 9.00 PLN.")).toBeInTheDocument();
    expect(screen.getByLabelText("Итого")).toHaveValue("10");
  });

  it("renders an item-level missing price card naming the item", () => {
    renderReview({
      issues: [missingPrice],
      snapshot: snapshot({
        items: [
          { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
          { name: "Chleb", quantity: 1, unit: null, unitPrice: null, lineTotal: null }
        ]
      })
    });

    expect(screen.getByRole("heading", { name: "Не распознана цена: Chleb" })).toBeInTheDocument();
    expect(screen.getByText("Ни цена, ни сумма строки не распознаны.")).toBeInTheDocument();
    expect(screen.getByLabelText("Цена")).toHaveValue("");
  });

  it("renders both issue types together and counts the unresolved ones", () => {
    renderReview({
      issues: [missingPrice, mismatch],
      snapshot: snapshot({
        receiptTotal: 99,
        items: [
          { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
          { name: "Chleb", quantity: 1, unit: null, unitPrice: null, lineTotal: 5 }
        ]
      })
    });

    expect(screen.getAllByRole("article")).toHaveLength(2);
    expect(screen.getByText("Осталось 2 неточности.")).toBeInTheDocument();
  });

  it("applies the line sum to the receipt total when the suggested fix is accepted", () => {
    const { onSnapshotChange } = renderReview({ issues: [mismatch] });

    fireEvent.click(screen.getByRole("button", { name: "Взять сумму строк: 9.00" }));

    expect(onSnapshotChange).toHaveBeenCalledWith(expect.objectContaining({ receiptTotal: 9 }));
  });

  it("fills both price fields of a single-unit item from one inline edit", () => {
    const { onSnapshotChange } = renderReview({
      issues: [missingPrice],
      snapshot: snapshot({
        items: [
          { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
          { name: "Chleb", quantity: 1, unit: null, unitPrice: null, lineTotal: null }
        ]
      })
    });

    fireEvent.change(screen.getByLabelText("Цена"), { target: { value: "6" } });

    expect(onSnapshotChange).toHaveBeenCalledWith(
      expect.objectContaining({
        items: [
          expect.objectContaining({ name: "Mleko" }),
          expect.objectContaining({ name: "Chleb", unitPrice: 6, lineTotal: 6 })
        ]
      })
    );
  });

  it("marks a card resolved and enables saving once the snapshot satisfies the rule", () => {
    // The snapshot already adds up, so the server-reported mismatch is stale.
    const { onSave } = renderReview({ issues: [mismatch], snapshot: snapshot({ receiptTotal: 9 }) });

    expect(screen.getByText("Исправлено")).toBeInTheDocument();
    expect(screen.getByText("Все неточности исправлены. Можно сохранять покупку.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Сохранить покупку" }));
    expect(onSave).toHaveBeenCalledTimes(1);
  });

  it("blocks saving while an issue is still unresolved", () => {
    renderReview({ issues: [mismatch] });

    expect(screen.getByRole("button", { name: "Сохранить покупку" })).toBeDisabled();
  });

  it("requests the full editor from the explicit action", () => {
    const { onOpenFullEditor } = renderReview({ issues: [mismatch] });

    fireEvent.click(screen.getByRole("button", { name: "Открыть полный редактор" }));

    expect(onOpenFullEditor).toHaveBeenCalledTimes(1);
  });

  it("renders English copy for the same issues", () => {
    renderReview({ issues: [mismatch] }, "en");

    expect(screen.getByRole("heading", { name: "Line totals do not match the receipt total" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open full editor" })).toBeInTheDocument();
  });
});

describe("IssueReview receipt fragment", () => {
  const boxed = (box: PurchaseDraftIssue["boundingBox"]): PurchaseDraftIssue => ({
    ...mismatch,
    boundingBox: box
  });

  it("crops the receipt photo to the region the issue points at", () => {
    renderReview({ issues: [boxed({ x: 0.1, y: 0.4, width: 0.8, height: 0.05 })], photo });

    const fragment = screen.getByAltText("Фрагмент фотографии чека с этим значением");
    expect(fragment).toHaveAttribute("src", photo.url);
    // 0.02/0.015 of padding widen the box before it becomes offsets and scale.
    expect(percent(fragment.style.width)).toBeCloseTo(100 / 0.84, 6);
    expect(percent(fragment.style.left)).toBeCloseTo((-100 * 0.08) / 0.84, 6);
    expect(percent(fragment.style.top)).toBeCloseTo((-100 * 0.385) / 0.08, 6);
  });

  it("falls back to the whole photo when the issue has no coordinates", () => {
    renderReview({ issues: [boxed(null)], photo });

    expect(screen.getByAltText("Фотография чека целиком")).toHaveAttribute("src", photo.url);
    expect(screen.queryByAltText("Фрагмент фотографии чека с этим значением")).not.toBeInTheDocument();
  });

  it.each([
    ["outside the image", { x: 0.9, y: 0.1, width: 0.5, height: 0.1 }],
    ["without a size", { x: 0.1, y: 0.1, width: 0, height: 0.1 }],
    ["with a negative origin", { x: -0.2, y: 0.1, width: 0.5, height: 0.1 }],
    ["with a non-finite value", { x: Number.NaN, y: 0.1, width: 0.5, height: 0.1 }]
  ])("ignores a box %s and shows the whole photo", (_name, box) => {
    renderReview({ issues: [boxed(box)], photo });

    expect(screen.getByAltText("Фотография чека целиком")).toBeInTheDocument();
  });

  it("does not crop a receipt photographed in several parts", () => {
    renderReview({
      issues: [boxed({ x: 0.1, y: 0.4, width: 0.8, height: 0.05 })],
      photo: { ...photo, croppable: false }
    });

    expect(screen.getByAltText("Фотография чека целиком")).toBeInTheDocument();
  });

  it("shows no image at all when the receipt has no stored photo", () => {
    renderReview({ issues: [boxed({ x: 0.1, y: 0.4, width: 0.8, height: 0.05 })], photo: null });

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    // The card text is unaffected by the missing photo.
    expect(screen.getByText("Сумма распознанных строк — 9.00 PLN.")).toBeInTheDocument();
  });
});

describe("IssueReview change history", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const sampleEntries: ReceiptReviewAuditEntry[] = [
    {
      occurredAt: "2026-08-24T17:20:00Z",
      actor: "Owner",
      fieldName: "MerchantName",
      changeType: "Modified",
      oldValue: "Biedronka",
      newValue: "Biedronka Centrum"
    },
    {
      occurredAt: "2026-08-24T17:20:00Z",
      actor: "Owner",
      fieldName: "Item:1",
      changeType: "ItemRemoved",
      oldValue: "Chleb (qty=1, total=5)",
      newValue: null
    }
  ];

  function stubAuditFetch(entries: ReceiptReviewAuditEntry[]) {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify(entries), { status: 200 }))
    );
  }

  it("does not render a history section without a receiptId", () => {
    renderReview({ issues: [] });

    expect(screen.queryByText("Показать историю изменений")).not.toBeInTheDocument();
  });

  it("stays collapsed until the reviewer expands it, then lists the entries", async () => {
    stubAuditFetch(sampleEntries);
    renderReview({ issues: [], receiptId: "receipt-1" });

    const toggle = screen.getByRole("button", { name: "Показать историю изменений" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(toggle);

    await waitFor(() => expect(screen.getByText("Изменено: MerchantName")).toBeInTheDocument());
    expect(screen.getByText("Удалена позиция: Item:1")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Скрыть историю изменений" })).toHaveAttribute(
      "aria-expanded",
      "true"
    );
  });

  it("shows an empty state when the receipt has no history", async () => {
    stubAuditFetch([]);
    renderReview({ issues: [], receiptId: "receipt-1" });

    fireEvent.click(screen.getByRole("button", { name: "Показать историю изменений" }));

    await waitFor(() => expect(screen.getByText("Изменений пока нет.")).toBeInTheDocument());
  });

  it("renders English copy for the history section", async () => {
    stubAuditFetch(sampleEntries);
    renderReview({ issues: [], receiptId: "receipt-1" }, "en");

    fireEvent.click(screen.getByRole("button", { name: "Show change history" }));

    await waitFor(() => expect(screen.getByText("Changed: MerchantName")).toBeInTheDocument());
  });
});
