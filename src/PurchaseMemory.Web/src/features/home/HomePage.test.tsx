import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import type { ReceiptView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { HomePage } from "./HomePage";

function receipt(partCount: number): ReceiptView {
  return {
    id: `receipt-${partCount}`,
    status: "ready",
    uploadedAt: "2026-08-03T12:00:00Z",
    updatedAt: "2026-08-03T12:01:00Z",
    originalFileName: "receipt.jpg",
    sourceImageUrl: "/receipt.jpg",
    imageParts: Array.from({ length: partCount }, (_, position) => ({
      position,
      contentType: "image/jpeg",
      sizeBytes: 42,
      sourceImageUrl: `/receipt-${partCount}-${position}.jpg`
    })),
    provider: null,
    model: null,
    processingDurationMs: null,
    snapshot: null,
    warnings: [],
    issues: [],
    error: null,
    savedPurchaseId: null
  };
}

function renderHome(locale: "ru" | "en", partCounts: number[]) {
  return render(
    <AppProviders locale={locale}>
      <MemoryRouter>
        <HomePage recognition={null} receipts={partCounts.map(receipt)} />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("HomePage recent receipts", () => {
  it("uses Russian few and many forms for receipt parts and says nothing for one photo", () => {
    renderHome("ru", [1, 3, 5]);

    expect(screen.queryByText("1 часть")).not.toBeInTheDocument();
    expect(screen.getByText("3 части")).toBeInTheDocument();
    expect(screen.getByText("5 частей")).toBeInTheDocument();
  });

  it("uses the English plural form for receipt parts and says nothing for one photo", () => {
    renderHome("en", [1, 2]);

    expect(screen.queryByText("1 part")).not.toBeInTheDocument();
    expect(screen.getByText("2 parts")).toBeInTheDocument();
  });
});

describe("HomePage «Ревью» card", () => {
  const renderWithCount = (reviewCount: number | null) => render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <HomePage recognition={null} receipts={[]} reviewCount={reviewCount} />
      </MemoryRouter>
    </AppProviders>
  );

  it("is warm with the count while something waits", () => {
    renderWithCount(12);
    const card = screen.getByRole("link", { name: /^Ревью/ });
    expect(card).toHaveAttribute("href", "/needs-attention");
    expect(card).toHaveAttribute("data-tone", "warning");
    expect(card).toHaveTextContent("12");
    expect(card).toHaveAccessibleName("Ревью, 12 пунктов требуют внимания.");
    expect(screen.queryByText("Всё проверено")).not.toBeInTheDocument();
  });

  it("shows 99+ for a long list, like the tab badge", () => {
    renderWithCount(140);
    expect(screen.getByRole("link", { name: /^Ревью/ })).toHaveTextContent("99+");
  });

  it("says «Всё проверено» without a count when nothing waits", () => {
    renderWithCount(0);
    const card = screen.getByRole("link", { name: /^Ревью/ });
    expect(card).not.toHaveAttribute("data-tone");
    expect(card).toHaveTextContent("Всё проверено");
    expect(card).not.toHaveTextContent(/\d/);
  });

  it("is a plain card while the count is unknown", () => {
    renderWithCount(null);
    const card = screen.getByRole("link", { name: "Ревью" });
    expect(card).not.toHaveAttribute("data-tone");
    expect(card).not.toHaveTextContent("Всё проверено");
  });
});
