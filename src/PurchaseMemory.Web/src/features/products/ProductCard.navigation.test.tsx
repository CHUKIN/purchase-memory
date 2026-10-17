import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { describe, expect, it } from "vitest";
import type { ProductDetail, ProductPricePoint } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { useBackTarget } from "../../app/backNavigation";
import { Page } from "../../design-system/components";
import { PurchaseFeed, emptyPurchaseFeedFilters } from "../purchases/PurchaseFeed";
import { ProductCard } from "./ProductCard";

const point = (purchaseId: string, purchasedAt: string, merchantName: string, unitPrice: number): ProductPricePoint =>
  ({ purchaseId, purchasedAt, merchantName, unitPrice });

const points = [
  point("pu1", "2026-07-01T10:00:00Z", "Biedronka", 4.99),
  point("pu2", "2026-07-11T10:00:00Z", "Lidl", 3.49),
  point("pu3", "2026-07-21T10:00:00Z", "Żabka", 6.3)
];

const product: ProductDetail = {
  id: "milk",
  name: "Mleko Łaciate 1L",
  normalizedName: "mleko laciate 1 l",
  categoryId: "dairy",
  createdAt: "2026-07-01T10:00:00Z",
  updatedAt: "2026-07-21T10:00:00Z",
  purchaseCount: 3,
  linkedItemCount: 3,
  unpricedItemCount: 0,
  priceSeries: [{
    currency: "PLN", unit: "szt", minimumPrice: 3.49, averagePrice: 4.9, latestPrice: 6.3, latestPoint: points[2], points
  }],
  frequency: null,
  aliases: []
};

function Probe() {
  const location = useLocation();
  const back = useBackTarget({ to: "/purchases", labelKey: "common.purchases" });
  return (
    <Page title="Opened screen" back={back}>
      <p data-testid="where">{`${location.pathname}${location.search}`}</p>
    </Page>
  );
}

function ProductScreen() {
  const back = useBackTarget({ to: "/products", labelKey: "common.products" });
  return <ProductCard product={product} locale="ru-RU" back={back} />;
}

function FeedScreen() {
  return <PurchaseFeed filters={{ ...emptyPurchaseFeedFilters, category: "dairy" }} onFiltersChange={() => undefined} purchases={[]} totalCount={0} />;
}

function renderApp(initial: string) {
  render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[initial]}>
        <Routes>
          <Route path="/products/:productId" element={<ProductScreen />} />
          <Route path="/purchases/:purchaseId" element={<Probe />} />
          <Route path="/purchases" element={<FeedScreen />} />
          <Route path="*" element={<Probe />} />
        </Routes>
      </MemoryRouter>
    </AppProviders>
  );
}

describe("product card navigation", () => {
  it("lists the product's latest purchases, newest first, each opening that purchase", async () => {
    renderApp("/products/milk");

    const list = screen.getByRole("region", { name: "Последние покупки" });
    const rows = within(list).getAllByRole("link");
    expect(rows.map((row) => row.getAttribute("href"))).toEqual(["/purchases/pu3", "/purchases/pu2", "/purchases/pu1"]);

    fireEvent.click(rows[1]);
    expect(await screen.findByRole("heading", { level: 1, name: "Opened screen" })).toBeInTheDocument();
    expect(screen.getByTestId("where")).toHaveTextContent("/purchases/pu2");
    // The purchase came from the product card: «Назад» (a detail screen, so no name) returns to it.
    fireEvent.click(screen.getByRole("link", { name: "Назад" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Mleko Łaciate 1L" })).toBeInTheDocument();
  });

  it("the related lists carry the way back, and the list shows a back button only then", async () => {
    renderApp("/products/milk");

    fireEvent.click(screen.getByRole("link", { name: "Покупки в этой категории" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Покупки" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("link", { name: "Назад" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Mleko Łaciate 1L" })).toBeInTheDocument();
  });

  it("a tab root opened directly has no back button", () => {
    renderApp("/purchases");

    expect(screen.getByRole("heading", { level: 1, name: "Покупки" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Назад|Товары/ })).not.toBeInTheDocument();
  });
});
