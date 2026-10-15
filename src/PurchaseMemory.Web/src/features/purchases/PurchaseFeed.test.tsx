import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { PurchaseView } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { openFilters, setDate } from "../../test/ionicHelpers";
import { PurchaseFeed, emptyPurchaseFeedFilters, type PurchaseFeedFilters } from "./PurchaseFeed";

function purchase(overrides: Partial<PurchaseView> = {}): PurchaseView {
  return {
    id: "6d0b2b2e-8b1e-4b1e-9c2b-000000000001",
    merchantName: "Biedronka",
    purchasedAt: "2026-08-24T17:12:00Z",
    currency: "PLN",
    receiptTotal: 42.5,
    createdAt: "2026-08-24T18:00:00Z",
    updatedAt: "2026-08-24T18:00:00Z",
    items: [{ name: "Mleko", quantity: 1, unit: "pcs", unitPrice: 4.5, lineTotal: 4.5 }],
    issues: [],
    ...overrides
  };
}

function renderFeed(
  props: Partial<React.ComponentProps<typeof PurchaseFeed>> = {},
  locale: "ru" | "en" = "ru"
) {
  const onFiltersChange = vi.fn();
  render(
    <AppProviders locale={locale}>
      <MemoryRouter>
        <PurchaseFeed
          filters={emptyPurchaseFeedFilters}
          onFiltersChange={onFiltersChange}
          purchases={[purchase()]}
          totalCount={1}
          {...props}
        />
      </MemoryRouter>
    </AppProviders>
  );
  return { onFiltersChange };
}

const purchaseLink = () => screen.getByRole("link", { name: /^Biedronka/ });

describe("PurchaseFeed", () => {
  it("shows merchant, item count and the reviewed status for a clean purchase", () => {
    renderFeed();

    expect(screen.getByRole("heading", { level: 1, name: "Покупки" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 2, name: "Biedronka" })).toBeInTheDocument();
    expect(purchaseLink()).toHaveAttribute("href", "/purchases/6d0b2b2e-8b1e-4b1e-9c2b-000000000001");
    expect(within(purchaseLink()).getByText(/1 позиция/)).toBeInTheDocument();
    expect(within(purchaseLink()).getByText("Проверено")).toBeInTheDocument();
    expect(within(purchaseLink()).getByText("42,50 PLN")).toBeInTheDocument();
    expect(screen.getByText(/1 покупка/)).toBeInTheDocument();
  });

  it("marks a purchase with issues as needing attention and names each issue", () => {
    renderFeed({
      purchases: [purchase({
        issues: [
          { type: "receiptTotalMismatch", itemPosition: null, itemName: null, recognizedValue: "38.00 PLN", suggestedFix: "" },
          { type: "missingItemPrice", itemPosition: 0, itemName: "Mleko", recognizedValue: "", suggestedFix: "" }
        ]
      })]
    });

    expect(within(purchaseLink()).getByText("Нужно проверить")).toBeInTheDocument();
    expect(screen.getByText("Сумма строк не сходится с итогом чека")).toBeInTheDocument();
    expect(screen.getByText("Не распознана цена: Mleko")).toBeInTheDocument();
  });

  it("uses the distinct empty state when no purchase exists at all", () => {
    renderFeed({ purchases: [], totalCount: 0 });

    expect(screen.getByRole("heading", { name: "Покупок пока нет" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Сфотографировать чек/ })).toHaveAttribute("href", "/purchases/new");
    expect(screen.queryByRole("heading", { name: "Ничего не найдено" })).not.toBeInTheDocument();
  });

  it("uses the distinct empty state when filters exclude every purchase", () => {
    const filters: PurchaseFeedFilters = { ...emptyPurchaseFeedFilters, status: "needsAttention" };
    const { onFiltersChange } = renderFeed({ purchases: [], totalCount: 0, filters });

    expect(screen.getByRole("heading", { name: "Ничего не найдено" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Сбросить фильтры" }));
    expect(onFiltersChange).toHaveBeenCalledWith(emptyPurchaseFeedFilters);
  });

  it("renders loading and error states instead of the list", () => {
    const { unmount } = render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <PurchaseFeed filters={emptyPurchaseFeedFilters} onFiltersChange={vi.fn()} purchases={[]} totalCount={0} loading />
        </MemoryRouter>
      </AppProviders>
    );
    expect(screen.getByRole("status", { name: "Загружаем покупки…" })).toHaveAttribute("aria-busy", "true");
    unmount();

    renderFeed({ purchases: [], totalCount: 0, error: "Не удалось загрузить покупки." });
    expect(screen.getByRole("alert")).toHaveTextContent("Не удалось загрузить покупки.");
  });

  it("keeps the search on screen and every other filter in the sheet, reporting each change", () => {
    const { onFiltersChange } = renderFeed();

    fireEvent.change(screen.getByLabelText("Поиск"), { target: { value: "mleko" } });
    expect(onFiltersChange).toHaveBeenCalledWith({ ...emptyPurchaseFeedFilters, query: "mleko" });

    const sheet = openFilters();
    expect(within(sheet).getByRole("heading", { name: "Фильтры" })).toBeInTheDocument();
    setDate("Дата с", "2026-08-01");
    expect(onFiltersChange).toHaveBeenCalledWith({ ...emptyPurchaseFeedFilters, from: "2026-08-01" });

    setDate("Дата по", "2026-08-24");
    expect(onFiltersChange).toHaveBeenCalledWith({ ...emptyPurchaseFeedFilters, to: "2026-08-24" });

    fireEvent.click(within(sheet).getByRole("tab", { name: "Нужно проверить" }));
    expect(onFiltersChange).toHaveBeenCalledWith({ ...emptyPurchaseFeedFilters, status: "needsAttention" });
  });

  it("counts the filters set in the sheet on the toolbar button and resets them but not the search", () => {
    const filters: PurchaseFeedFilters = { ...emptyPurchaseFeedFilters, query: "mleko", from: "2026-08-01", status: "reviewed", brand: "Łaciate" };
    const { onFiltersChange } = renderFeed({ filters });

    expect(screen.getByRole("button", { name: "Фильтры, выбрано 3" })).toBeInTheDocument();
    const sheet = openFilters();
    fireEvent.click(within(sheet).getByRole("button", { name: "Сбросить" }));
    expect(onFiltersChange).toHaveBeenCalledWith({ ...emptyPurchaseFeedFilters, query: "mleko" });
  });

  it("offers the load-more action only while another page exists", () => {
    const onLoadMore = vi.fn();
    renderFeed({ hasMore: true, onLoadMore });

    fireEvent.click(screen.getByRole("button", { name: "Показать ещё" }));
    expect(onLoadMore).toHaveBeenCalledOnce();
  });

  it("translates the feed into English", () => {
    renderFeed({ purchases: [purchase({ items: [] })], totalCount: 1 }, "en");

    expect(screen.getByRole("heading", { level: 1, name: "Purchases" })).toBeInTheDocument();
    expect(screen.getByText("1 purchase")).toBeInTheDocument();
    expect(screen.getByText(/0 items/)).toBeInTheDocument();
    const sheet = openFilters();
    expect(within(sheet).getByRole("tab", { name: "Needs attention" })).toBeInTheDocument();
  });
});
