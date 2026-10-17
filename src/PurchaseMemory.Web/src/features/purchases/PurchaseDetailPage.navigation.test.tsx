import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getPurchase, postLineApprovals, updatePurchase } from "../../api/client";
import type { PurchaseDetail } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { useBackTarget } from "../../app/backNavigation";
import { clearPlaces } from "../../app/place";
import { Page } from "../../design-system/components";
import { dialog } from "../../test/ionicHelpers";
import { PurchaseDetailPage } from "./PurchaseDetailPage";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return {
    ApiError: actual.ApiError,
    getPurchase: vi.fn(),
    updatePurchase: vi.fn(),
    getProductCatalog: vi.fn(),
    getProduct: vi.fn(),
    listPurchaseImpressions: vi.fn(() => Promise.resolve([])),
    createImpression: vi.fn(),
    deleteImpression: vi.fn(),
    fetchProtectedBlob: vi.fn(() => new Promise(() => undefined)),
    getReceipt: vi.fn(),
    postLineApprovals: vi.fn()
  };
});

/*
 * «Назад» from a product opened off a purchase line lands on the same line:
 * the review reopens at the card that was open, the editor at the open line
 * sheet. (These tests go through the fallback path, «open the origin's
 * address»: MemoryRouter has no browser history positions.)
 */
const purchase: PurchaseDetail = {
  id: "p1",
  sourceReceiptId: null,
  merchantName: "Żabka",
  purchasedAt: "2026-07-01T10:00:00Z",
  currency: "PLN",
  receiptTotal: 18,
  createdAt: "2026-07-01T10:00:00Z",
  updatedAt: "2026-07-01T10:00:00.123Z",
  items: [
    {
      id: "line-kawa", position: 0, name: "KAWA", quantity: 1, unit: "szt", unitPrice: 15, lineTotal: 15, kind: "product",
      discountedItemPosition: null, productId: "coffee", productName: "Kawa", productNeedsReview: false, boundingBox: null, approved: false
    },
    {
      id: "line-mleko", position: 1, name: "MLEKO 3.2", quantity: 1, unit: "szt", unitPrice: 3, lineTotal: 3, kind: "product",
      discountedItemPosition: null, productId: "milk", productName: "Mleko", productNeedsReview: false, boundingBox: null, approved: false
    }
  ],
  issues: []
};

function ProductScreen() {
  const back = useBackTarget({ to: "/products", labelKey: "common.products" });
  const location = useLocation();
  return <Page title="Product screen" back={back}><p>{location.pathname}</p></Page>;
}

function Where() {
  const location = useLocation();
  return <p data-testid="where">{`${location.pathname}${location.search}`}</p>;
}

function renderAt(path: string) {
  const [pathname, search] = path.split("?");
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[{ pathname, search: search ? `?${search}` : "" }]}>
        <Where />
        <Routes>
          <Route path="/purchases/:purchaseId" element={<PurchaseDetailPage />} />
          <Route path="/products/:productId" element={<ProductScreen />} />
        </Routes>
      </MemoryRouter>
    </AppProviders>
  );
}

const backButton = () => screen.getByRole("link", { name: "Назад" });

describe("purchase line → product → back", () => {
  beforeEach(() => {
    clearPlaces();
    vi.mocked(getPurchase).mockReset().mockResolvedValue(purchase);
    vi.mocked(updatePurchase).mockReset();
    vi.mocked(postLineApprovals).mockReset().mockImplementation(async (items) => ({
      items: items.map((item) => ({ ...item, status: "applied" as const, approvedAt: item.at }))
    }));
  });

  it("the product button on a line opens that product, and «Назад» returns to the purchase", async () => {
    renderAt("/purchases/p1");

    const open = await screen.findByRole("link", { name: "Открыть товар «Mleko»" });
    expect(open).toHaveAttribute("href", "/products/milk");
    fireEvent.click(open);

    expect(await screen.findByRole("heading", { level: 1, name: "Product screen" })).toBeInTheDocument();
    expect(screen.getByTestId("where")).toHaveTextContent("/products/milk");
    fireEvent.click(backButton());
    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(screen.getByTestId("where")).toHaveTextContent("/purchases/p1");
    // Opened from the row, not from the review: no card is reopened.
    expect(screen.queryByTestId("line-review-card")).not.toBeInTheDocument();
  });

  it("the review's «Открыть товар» opens the product; «Назад» reopens the review at the same card", async () => {
    renderAt("/purchases/p1");

    fireEvent.click(await screen.findByRole("button", { name: /^MLEKO 3.2.*сверить с чеком$/ }));
    const card = screen.getByTestId("line-review-card");
    expect(within(card).getByRole("heading", { level: 3, name: "MLEKO 3.2" })).toBeInTheDocument();
    fireEvent.click(within(card).getByRole("button", { name: "Открыть товар «Mleko»" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Product screen" })).toBeInTheDocument();
    fireEvent.click(backButton());

    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    await waitFor(() => expect(within(screen.getByTestId("line-review-card")).getByRole("heading", { level: 3, name: "MLEKO 3.2" })).toBeInTheDocument());
  });

  it("started from the header button, «Назад» still returns to the card that was showing", async () => {
    renderAt("/purchases/p1");

    fireEvent.click(await screen.findByRole("button", { name: /^Проверить строки/ }));
    // First card is KAWA; approve it, so the second one (MLEKO) is showing.
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    await waitFor(() => expect(within(screen.getByTestId("line-review-card")).getByRole("heading", { level: 3, name: "MLEKO 3.2" })).toBeInTheDocument());
    fireEvent.click(within(screen.getByTestId("line-review-card")).getByRole("button", { name: "Открыть товар «Mleko»" }));
    fireEvent.click(await screen.findByRole("link", { name: "Назад" }));

    await waitFor(() => expect(within(screen.getByTestId("line-review-card")).getByRole("heading", { level: 3, name: "MLEKO 3.2" })).toBeInTheDocument());
  });

  it("the review's line sheet links to the product only while nothing is changed", async () => {
    renderAt("/purchases/p1");

    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));
    fireEvent.click(screen.getByRole("button", { name: "Исправить строку" }));
    const sheet = dialog("Строка 1");
    expect(within(sheet).getByRole("button", { name: "Открыть товар «Kawa»" })).toBeInTheDocument();

    fireEvent.change(within(sheet).getByLabelText("Название"), { target: { value: "KAWA 2" } });
    expect(within(sheet).queryByRole("button", { name: "Открыть товар «Kawa»" })).not.toBeInTheDocument();
    fireEvent.change(within(sheet).getByLabelText("Название"), { target: { value: "KAWA" } });
    fireEvent.click(within(sheet).getByRole("button", { name: "Открыть товар «Kawa»" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Product screen" })).toBeInTheDocument();
    fireEvent.click(backButton());
    await waitFor(() => expect(within(screen.getByTestId("line-review-card")).getByRole("heading", { level: 3, name: "KAWA" })).toBeInTheDocument());
  });

  it("the editor's line sheet links to the product (when unchanged) and «Назад» reopens that sheet", async () => {
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: /^MLEKO 3.2/ }));
    const sheet = dialog("Строка 2");
    fireEvent.click(within(sheet).getByRole("button", { name: "Открыть товар «Mleko»" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Product screen" })).toBeInTheDocument();
    fireEvent.click(backButton());
    expect(await screen.findByRole("heading", { level: 1, name: "Исправить" })).toBeInTheDocument();
    expect(screen.getByTestId("where")).toHaveTextContent("/purchases/p1?edit=1");
    expect(dialog("Строка 2")).toBeInTheDocument();
  });

  it("the editor does not offer the product link while there are unsaved changes", async () => {
    renderAt("/purchases/p1?edit=1");

    fireEvent.click(await screen.findByRole("button", { name: /^MLEKO 3.2/ }));
    const sheet = dialog("Строка 2");
    fireEvent.change(within(sheet).getByLabelText("Цена"), { target: { value: "4" } });
    expect(within(sheet).queryByRole("button", { name: "Открыть товар «Mleko»" })).not.toBeInTheDocument();
  });
});
