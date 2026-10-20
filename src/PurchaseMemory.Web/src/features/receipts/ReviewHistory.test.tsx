import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReceiptReviewAuditEntry } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { ReviewHistory } from "./ReviewHistory";

const sampleEntries: ReceiptReviewAuditEntry[] = [
  { occurredAt: "2026-08-24T17:20:00Z", actor: "Owner", fieldName: "MerchantName", changeType: "Modified", oldValue: "Biedronka", newValue: "Biedronka Centrum" },
  { occurredAt: "2026-08-24T17:20:00Z", actor: "Owner", fieldName: "Item:1", changeType: "ItemRemoved", oldValue: "Chleb (qty=1, total=5)", newValue: null }
];

function stubAuditFetch(entries: ReceiptReviewAuditEntry[]) {
  vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify(entries), { status: 200 })));
}

function renderHistory(locale: "ru" | "en" = "ru") {
  render(<AppProviders locale={locale}><ReviewHistory receiptId="receipt-1" /></AppProviders>);
}

describe("ReviewHistory", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("stays collapsed until the reviewer expands it, then lists the entries", async () => {
    stubAuditFetch(sampleEntries);
    renderHistory();

    const toggle = screen.getByRole("button", { name: "Показать историю изменений" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(fetch).not.toHaveBeenCalled();

    fireEvent.click(toggle);

    await waitFor(() => expect(screen.getByText("Изменено: MerchantName")).toBeInTheDocument());
    expect(screen.getByText("Удалена позиция: Item:1")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Скрыть историю изменений" })).toHaveAttribute("aria-expanded", "true");
  });

  it("shows an empty state when the receipt has no history", async () => {
    stubAuditFetch([]);
    renderHistory();

    fireEvent.click(screen.getByRole("button", { name: "Показать историю изменений" }));

    await waitFor(() => expect(screen.getByText("Изменений пока нет.")).toBeInTheDocument());
  });

  it("renders English copy for the history section", async () => {
    stubAuditFetch(sampleEntries);
    renderHistory("en");

    fireEvent.click(screen.getByRole("button", { name: "Show change history" }));

    await waitFor(() => expect(screen.getByText("Changed: MerchantName")).toBeInTheDocument());
  });
});
