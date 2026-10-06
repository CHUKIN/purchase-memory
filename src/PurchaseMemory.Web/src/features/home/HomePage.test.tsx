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
  it("uses Russian one, few, and many forms for receipt parts", () => {
    renderHome("ru", [1, 3, 5]);

    expect(screen.getByText("1 часть")).toBeInTheDocument();
    expect(screen.getByText("3 части")).toBeInTheDocument();
    expect(screen.getByText("5 частей")).toBeInTheDocument();
  });

  it("uses English singular and plural forms for receipt parts", () => {
    renderHome("en", [1, 2]);

    expect(screen.getByText("1 part")).toBeInTheDocument();
    expect(screen.getByText("2 parts")).toBeInTheDocument();
  });
});
