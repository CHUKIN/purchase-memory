import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { ProductDetail, ProductPricePoint, ProductPriceSeries } from "../../api/types";
import { getProductCatalog } from "../../api/client";
import { AppProviders } from "../../app/AppProviders";
import { sampleCategories } from "../categories/categoryFixtures";
import { dialog } from "../../test/ionicHelpers";
import { ProductCard } from "./ProductCard";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return { ...actual, getProductCatalog: vi.fn() };
});

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
    const lastPurchase = screen.getByRole("region", { name: "Последняя покупка" });
    expect(lastPurchase).toHaveTextContent("Żabka");
    expect(lastPurchase).toHaveTextContent("21 июл. 2026 г.");

    // The latest price is the big figure; minimum and average follow per unit.
    expect(lastPurchase).toHaveTextContent("6,30");
    const figures = within(lastPurchase).getAllByText(/\/шт\./).map((node) => node.textContent ?? "");
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

    const kid = screen.getByRole("checkbox", { name: "для ребёнка" });
    const party = screen.getByRole("checkbox", { name: "к празднику" });
    expect(kid).toBeChecked();
    expect(party).not.toBeChecked();
    fireEvent.click(party);
    fireEvent.click(kid);
    expect(party).toBeChecked();
    expect(kid).not.toBeChecked();

    fireEvent.change(screen.getByLabelText("Новый тег"), { target: { value: "  веган " } });
    fireEvent.click(screen.getByRole("button", { name: "Создать тег" }));
    await waitFor(() => expect(onCreateTag).toHaveBeenCalledWith("веган"));
    expect(await screen.findByRole("checkbox", { name: "веган" })).toBeChecked();
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
    expect(screen.getByRole("checkbox", { name: "Łódź" })).toBeChecked();
    expect(onCreateTag).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onSetTags).toHaveBeenCalledWith(["city"]));
  });
  it("starts fresh on another product: no suggestions carried over and none applied to it", async () => {
    const allTags = [
      { id: "kid", name: "для ребёнка", productCount: 2 },
      { id: "vegan", name: "веган", productCount: 1 }
    ];
    const onSetTags = vi.fn().mockResolvedValue(undefined);
    const onSuggestTags = vi.fn().mockResolvedValue({
      productId: "a",
      offeredTagCount: 2,
      suggestions: [{ tagId: "vegan", name: "веган", reason: "растительный продукт" }]
    });
    const card = (product: ProductDetail) => (
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCard
            product={product}
            locale="ru-RU"
            allTags={allTags}
            onSetTags={onSetTags}
            onCreateTag={vi.fn()}
            onSuggestTags={onSuggestTags}
          />
        </MemoryRouter>
      </AppProviders>
    );
    const { rerender } = render(card(detail({ id: "a", tags: [] })));
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    expect(await screen.findByRole("button", { name: "Добавить тег «веган»" })).toBeInTheDocument();

    rerender(card(detail({ id: "b", name: "Chleb", tags: [] })));

    expect(screen.queryByRole("button", { name: "Добавить тег «веган»" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Предложить теги" })).toBeInTheDocument();
    expect(onSetTags).not.toHaveBeenCalled();
  });

  it("drops an in-flight suggestion when the product changes before it arrives", async () => {
    let resolve: (value: unknown) => void = () => undefined;
    const onSuggestTags = vi.fn().mockReturnValue(new Promise((done) => { resolve = done; }));
    const card = (product: ProductDetail) => (
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCard
            product={product}
            locale="ru-RU"
            allTags={[{ id: "vegan", name: "веган", productCount: 1 }]}
            onSetTags={vi.fn()}
            onCreateTag={vi.fn()}
            onSuggestTags={onSuggestTags}
          />
        </MemoryRouter>
      </AppProviders>
    );
    const { rerender } = render(card(detail({ id: "a", tags: [] })));
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    rerender(card(detail({ id: "b", tags: [] })));
    resolve({ productId: "a", offeredTagCount: 1, suggestions: [{ tagId: "vegan", name: "веган", reason: "" }] });

    await waitFor(() => expect(screen.getByRole("button", { name: "Предложить теги" })).toBeEnabled());
    expect(screen.queryByRole("button", { name: "Добавить тег «веган»" })).not.toBeInTheDocument();
  });

  describe("«проверить» and merging", () => {
    const flagged = () => detail({
      id: "f9",
      name: "ACTIV KEFI NAT 280",
      needsReview: true,
      reviewReason: "ambiguousMatch",
      reviewCandidates: [
        { productId: "k1", name: "ACTIV KEFI BOR 280", brand: "Activia", categoryId: null, purchaseCount: 6, score: 0.75 }
      ]
    });

    function renderWithActions(product: ProductDetail) {
      const actions = { onMergeInto: vi.fn(async () => undefined), onConfirmDistinct: vi.fn(async () => undefined) };
      render(
        <AppProviders locale="ru">
          <MemoryRouter>
            <ProductCard product={product} locale="ru-RU" reviewActions={actions} />
          </MemoryRouter>
        </AppProviders>
      );
      return actions;
    }

    it("shows the flag with the candidates; «Да, это он» merges only after a confirmation naming both", async () => {
      const actions = renderWithActions(flagged());

      const banner = screen.getByRole("region", { name: "Нужно проверить" });
      expect(banner).toHaveTextContent("ACTIV KEFI BOR 280");
      expect(banner).toHaveTextContent("Activia · 6 покупок");
      expect(within(banner).getByRole("link", { name: "ACTIV KEFI BOR 280" })).toHaveAttribute("href", "/products/k1");

      fireEvent.click(within(banner).getByRole("button", { name: "Да, «ACTIV KEFI NAT 280» — это «ACTIV KEFI BOR 280»" }));
      const confirm = screen.getByRole("alertdialog", { name: "Объединить товары?" });
      expect(confirm).toHaveTextContent("Покупки, названия из чеков и теги товара «ACTIV KEFI NAT 280» перейдут в «ACTIV KEFI BOR 280». «ACTIV KEFI NAT 280» исчезнет.");
      expect(actions.onMergeInto).not.toHaveBeenCalled();
      fireEvent.click(within(confirm).getByRole("button", { name: "Объединить" }));

      await waitFor(() => expect(actions.onMergeInto).toHaveBeenCalledWith({ id: "k1", name: "ACTIV KEFI BOR 280" }));
    });

    it("«Это другой товар» settles the flag without merging", async () => {
      const actions = renderWithActions(flagged());

      fireEvent.click(screen.getByRole("button", { name: "«ACTIV KEFI NAT 280» — это другой товар" }));

      await waitFor(() => expect(actions.onConfirmDistinct).toHaveBeenCalledTimes(1));
      expect(actions.onMergeInto).not.toHaveBeenCalled();
    });

    it("says so when settling fails", async () => {
      const actions = renderWithActions(flagged());
      actions.onConfirmDistinct.mockRejectedValueOnce(new Error("offline"));

      fireEvent.click(screen.getByRole("button", { name: "«ACTIV KEFI NAT 280» — это другой товар" }));

      expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось сохранить.");
    });

    it("has no banner for an ordinary product, and only the mark without actions", () => {
      renderWithActions(detail());
      expect(screen.queryByRole("region", { name: "Нужно проверить" })).not.toBeInTheDocument();
      cleanup();

      renderCard(flagged());
      expect(screen.queryByRole("region", { name: "Нужно проверить" })).not.toBeInTheDocument();
      expect(screen.getByText("проверить")).toBeInTheDocument();
    });

    it("«Объединить с…» finds another product (never itself), confirms and merges", async () => {
      vi.mocked(getProductCatalog).mockResolvedValue({
        items: [{ id: "m2", name: "Mleko Łaciate 3,2% 1 l", normalizedName: "mleko laciate 3 2 1 l", purchaseCount: 9, linkedItemCount: 9, aliasCount: 2, createdAt: "", updatedAt: "" }],
        page: 1, pageSize: 8, totalCount: 1, hasMore: false, needsReviewCount: 0
      });
      const actions = renderWithActions(detail());

      fireEvent.click(screen.getByRole("button", { name: "Объединить с…" }));
      const picker = dialog("Объединить с товаром");
      expect(picker).toHaveTextContent("Куда перенести «Mleko Łaciate 3,2% 1L»?");
      fireEvent.change(within(picker).getByLabelText("Найти товар"), { target: { value: "mleko" } });
      fireEvent.click(await within(picker).findByRole("button", { name: "Объединить с «Mleko Łaciate 3,2% 1 l»" }));
      expect(getProductCatalog).toHaveBeenCalledWith({ query: "mleko", excludeProductId: "a", pageSize: 8 });

      fireEvent.click(within(screen.getByRole("alertdialog", { name: "Объединить товары?" })).getByRole("button", { name: "Объединить" }));
      await waitFor(() => expect(actions.onMergeInto).toHaveBeenCalledWith({ id: "m2", name: "Mleko Łaciate 3,2% 1 l" }));
    });
  });
});
