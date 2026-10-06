import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { ProductDetail, ProductPricePoint, ProductPriceSeries } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { sampleCategories } from "../categories/categoryFixtures";
import { ProductCard } from "./ProductCard";

function point(purchasedAt: string, merchantName: string | null, unitPrice: number): ProductPricePoint {
  return { purchasedAt, merchantName, unitPrice, purchaseId: `${purchasedAt}-${unitPrice}` };
}

function series(unit: string, points: ProductPricePoint[]): ProductPriceSeries {
  const prices = points.map((entry) => entry.unitPrice);
  return {
    currency: "PLN",
    unit,
    minimumPrice: Math.min(...prices),
    averagePrice: prices.reduce((sum, price) => sum + price, 0) / prices.length,
    latestPrice: prices[prices.length - 1],
    latestPoint: points[points.length - 1],
    points
  };
}

function detail(overrides: Partial<ProductDetail> = {}): ProductDetail {
  const pieces = series("szt", [
    point("2026-07-01T10:00:00Z", "Biedronka", 4.99),
    point("2026-07-11T10:00:00Z", "Lidl", 3.49),
    point("2026-07-21T10:00:00Z", "Żabka", 6.3)
  ]);

  return {
    id: "a",
    name: "Mleko Łaciate 3,2% 1L",
    normalizedName: "mleko laciate 3 2 1 l",
    createdAt: "2026-07-01T10:00:00Z",
    updatedAt: "2026-07-21T10:00:00Z",
    purchaseCount: 3,
    linkedItemCount: 3,
    unpricedItemCount: 0,
    priceSeries: [pieces],
    frequency: {
      firstPurchasedAt: "2026-07-01T10:00:00Z",
      lastPurchasedAt: "2026-07-21T10:00:00Z",
      purchaseCount: 3,
      averageDaysBetweenPurchases: 10
    },
    aliases: [{ merchantKey: "biedronka", rawText: "MLEKO LACIATE 3,2% 1L" }],
    ...overrides
  };
}

function renderCard(product: ProductDetail) {
  render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <ProductCard product={product} locale="ru-RU" />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("ProductCard", () => {
  it("answers what the product cost last time, where and when", () => {
    renderCard(detail());

    expect(screen.getByRole("heading", { level: 1, name: "Mleko Łaciate 3,2% 1L" })).toBeInTheDocument();
    const lastPurchase = screen.getByRole("heading", { name: "Последняя покупка" }).parentElement;
    expect(lastPurchase).toHaveTextContent("Żabka");
    expect(lastPurchase).toHaveTextContent("21 июл. 2026 г.");

    const figures = screen.getAllByRole("definition").map((node) => node.textContent);
    expect(figures.some((text) => text?.includes("6,30"))).toBe(true);
    expect(figures.some((text) => text?.includes("3,49"))).toBe(true);
    expect(figures.some((text) => text?.includes("4,93"))).toBe(true);
  });

  it("repeats every charted price as a table, so the chart is never the only source", () => {
    renderCard(detail());

    const table = screen.getByRole("table");
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(3);

    // Newest first, matching how the page reads from the top.
    expect(rows[0]).toHaveTextContent("Żabka");
    expect(rows[2]).toHaveTextContent("Biedronka");
  });

  it("counts shopping trips, agreeing with the frequency block beneath it", () => {
    // One receipt, two jars: one purchase, two lines. The header must not say
    // «2 покупки» while the frequency block says the product was bought once.
    renderCard(detail({
      purchaseCount: 1,
      linkedItemCount: 2,
      frequency: {
        firstPurchasedAt: "2026-07-01T10:00:00Z",
        lastPurchasedAt: "2026-07-01T10:00:00Z",
        purchaseCount: 1,
        averageDaysBetweenPurchases: null
      }
    }));

    expect(screen.getByText("1 покупка")).toBeInTheDocument();
    expect(screen.getByText("Покупали один раз — про регулярность говорить рано.")).toBeInTheDocument();
  });

  it("states how often the product is bought", () => {
    renderCard(detail());

    expect(screen.getByText("В среднем раз в 10 дн.")).toBeInTheDocument();
  });

  it("refuses to invent a rhythm from a single purchase and still shows its price", () => {
    const single = series("szt", [point("2026-07-01T10:00:00Z", "Biedronka", 4.99)]);
    renderCard(detail({
      purchaseCount: 1,
      linkedItemCount: 1,
      priceSeries: [single],
      frequency: {
        firstPurchasedAt: "2026-07-01T10:00:00Z",
        lastPurchasedAt: "2026-07-01T10:00:00Z",
        purchaseCount: 1,
        averageDaysBetweenPurchases: null
      }
    }));

    expect(screen.getByText("Покупали один раз — про регулярность говорить рано.")).toBeInTheDocument();
    expect(screen.getByText("Пока есть только одна цена, поэтому графику нечего показывать.")).toBeInTheDocument();
    expect(within(screen.getByRole("table")).getAllByRole("row")).toHaveLength(2);
  });

  it("keeps a second unit of measure separate instead of averaging it in", () => {
    const perKilogram = series("kg", [
      point("2026-07-02T10:00:00Z", "Biedronka", 24.9),
      point("2026-07-12T10:00:00Z", "Lidl", 22.5)
    ]);
    const perPiece = series("szt", [point("2026-07-01T10:00:00Z", "Biedronka", 2.49)]);

    renderCard(detail({ name: "Pomidory malinowe", priceSeries: [perKilogram, perPiece] }));

    expect(screen.getByRole("heading", { name: "Другие единицы измерения" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "за штуку" })).toBeInTheDocument();
    expect(screen.getByText(/Цены за разные единицы не сравниваются/)).toBeInTheDocument();
    expect(screen.getAllByRole("table")).toHaveLength(2);
  });

  it("admits when some lines had no readable price", () => {
    renderCard(detail({ unpricedItemCount: 2 }));

    expect(screen.getByText(/В 2 позициях цену прочитать не удалось/)).toBeInTheDocument();
  });

  it("says there is no history at all rather than showing an empty chart", () => {
    renderCard(detail({ priceSeries: [], frequency: null, unpricedItemCount: 3 }));

    expect(screen.getByText("Ни в одной позиции не удалось прочитать цену, поэтому истории цены нет."))
      .toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("lists the receipt spellings that were folded into this product", () => {
    renderCard(detail());

    expect(screen.getByText("MLEKO LACIATE 3,2% 1L")).toBeInTheDocument();
    expect(screen.getByText("biedronka")).toBeInTheDocument();
  });

  it("lets a person confirm the suggested category from the card", async () => {
    const onClassify = vi.fn().mockResolvedValue(undefined);
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCard
            product={detail({ categoryId: "cheese", categorySource: "suggested", brand: null, brandSource: null })}
            locale="ru-RU"
            categories={sampleCategories}
            onClassify={onClassify}
          />
        </MemoryRouter>
      </AppProviders>
    );

    expect(screen.getByRole("heading", { name: "Категория и бренд" })).toBeInTheDocument();
    expect(screen.getByText("Молочное и яйца / Сыр")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить" }));
    await waitFor(() => expect(onClassify).toHaveBeenCalledWith({ categoryId: "cheese" }));
  });

  it("links its category and brand to the filtered feed and catalog", () => {
    renderCard(detail({ categoryId: "milk", categorySource: "confirmed", brand: "Łaciate", brandSource: "confirmed" }));

    const related = screen.getByRole("navigation", { name: "Связанные списки" });
    expect(within(related).getByRole("link", { name: "Покупки в этой категории" })).toHaveAttribute("href", "/purchases?category=milk");
    expect(within(related).getByRole("link", { name: "Товары в этой категории" })).toHaveAttribute("href", "/products?category=milk");
    expect(within(related).getByRole("link", { name: "Покупки бренда Łaciate" })).toHaveAttribute("href", "/purchases?brand=%C5%81aciate");
  });

  it("offers no related lists for an unsorted product without a brand", () => {
    renderCard(detail({ categoryId: null, brand: null }));
    expect(screen.queryByRole("navigation", { name: "Связанные списки" })).not.toBeInTheDocument();
  });

  it("shows its tags and links each one to the filtered feed", () => {
    renderCard(detail({ tags: [{ id: "kid", name: "для ребёнка" }] }));
    expect(screen.getByRole("list", { name: "Теги товара" })).toHaveTextContent("для ребёнка");
    const related = screen.getByRole("navigation", { name: "Связанные списки" });
    expect(within(related).getByRole("link", { name: "Покупки с тегом «для ребёнка»" })).toHaveAttribute("href", "/purchases?tag=kid");
  });

  it("edits tags: picks existing ones, removes one and creates a new one inline", async () => {
    const onSetTags = vi.fn().mockResolvedValue(undefined);
    const onCreateTag = vi.fn().mockResolvedValue({ id: "new", name: "веган" });
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCard
            product={detail({ tags: [{ id: "kid", name: "для ребёнка" }] })}
            locale="ru-RU"
            allTags={[
              { id: "kid", name: "для ребёнка", productCount: 2 },
              { id: "party", name: "к празднику", productCount: 0 }
            ]}
            onSetTags={onSetTags}
            onCreateTag={onCreateTag}
          />
        </MemoryRouter>
      </AppProviders>
    );

    expect(screen.getByRole("heading", { name: "Теги" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Изменить теги" }));

    const kid = screen.getByRole("button", { name: "для ребёнка" });
    const party = screen.getByRole("button", { name: "к празднику" });
    expect(kid).toHaveAttribute("aria-pressed", "true");
    expect(party).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(party);
    fireEvent.click(kid);
    expect(party).toHaveAttribute("aria-pressed", "true");
    expect(kid).toHaveAttribute("aria-pressed", "false");

    fireEvent.change(screen.getByLabelText("Новый тег"), { target: { value: "  веган " } });
    fireEvent.click(screen.getByRole("button", { name: "Создать тег" }));
    await waitFor(() => expect(onCreateTag).toHaveBeenCalledWith("веган"));
    expect(await screen.findByRole("button", { name: "веган" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByLabelText("Новый тег")).toHaveValue("");

    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onSetTags).toHaveBeenCalledWith(["party", "new"]));
    expect(await screen.findByRole("button", { name: "Изменить теги" })).toBeInTheDocument();
  });

  it("explains a duplicate tag and keeps the editor open when saving fails", async () => {
    const { ApiError } = await import("../../api/client");
    const onSetTags = vi.fn().mockRejectedValue(new Error("offline"));
    const onCreateTag = vi.fn().mockRejectedValue(new ApiError("exists", 409));
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCard
            product={detail({ tags: [] })}
            locale="ru-RU"
            allTags={[]}
            onSetTags={onSetTags}
            onCreateTag={onCreateTag}
          />
        </MemoryRouter>
      </AppProviders>
    );

    expect(screen.getByText("Тегов нет")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Изменить теги" }));
    expect(screen.getByText("Тегов пока нет — создайте первый.")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Новый тег"), { target: { value: "Lodz" } });
    fireEvent.click(screen.getByRole("button", { name: "Создать тег" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Такой тег уже есть");

    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    expect(await screen.findByText("Не удалось сохранить теги. Попробуйте ещё раз.")).toBeInTheDocument();
    expect(screen.getByLabelText("Новый тег")).toBeInTheDocument();
  });

  it("picks an existing tag when its name is typed without Polish letters or case", async () => {
    const onCreateTag = vi.fn();
    const onSetTags = vi.fn().mockResolvedValue(undefined);
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCard
            product={detail({ tags: [] })}
            locale="ru-RU"
            allTags={[{ id: "city", name: "Łódź", productCount: 1 }]}
            onSetTags={onSetTags}
            onCreateTag={onCreateTag}
          />
        </MemoryRouter>
      </AppProviders>
    );

    fireEvent.click(screen.getByRole("button", { name: "Изменить теги" }));
    fireEvent.change(screen.getByLabelText("Новый тег"), { target: { value: "  LODZ " } });
    fireEvent.click(screen.getByRole("button", { name: "Создать тег" }));
    expect(screen.getByRole("button", { name: "Łódź" })).toHaveAttribute("aria-pressed", "true");
    expect(onCreateTag).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onSetTags).toHaveBeenCalledWith(["city"]));
  });
});
