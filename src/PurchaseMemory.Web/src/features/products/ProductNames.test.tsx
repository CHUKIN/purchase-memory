import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import type { ProductDetail, ProductSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { dialog } from "../../test/ionicHelpers";
import { ProductLinkNote } from "../purchases/ProductLinkRows";
import { ProductCard, type ProductCardProps } from "./ProductCard";
import { ProductCatalog } from "./ProductCatalog";

const kefir: ProductDetail = {
  id: "k",
  name: "ACTIV KEFI NAT 280",
  normalizedName: "activ kefi nat 280",
  namePl: "Kefir naturalny Activia 280 g",
  namePlSource: "confirmed",
  nameRu: "Кефир натуральный Activia, 280 г",
  nameRuSource: "suggested",
  createdAt: "2026-10-01T10:00:00Z",
  updatedAt: "2026-10-22T10:00:00Z",
  purchaseCount: 1,
  linkedItemCount: 1,
  unpricedItemCount: 0,
  priceSeries: [],
  frequency: null,
  aliases: []
};

const summary = (overrides: Partial<ProductSummary>): ProductSummary => ({
  id: "k", name: "ACTIV KEFI NAT 280", normalizedName: "activ kefi nat 280", purchaseCount: 2, linkedItemCount: 2, aliasCount: 1,
  createdAt: "2026-10-01T10:00:00Z", updatedAt: "2026-10-22T10:00:00Z", ...overrides
});

function renderCard(product: ProductDetail, props: Partial<ProductCardProps> = {}) {
  return render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <ProductCard product={product} locale="ru-RU" {...props} />
      </MemoryRouter>
    </AppProviders>
  );
}

const names = () => screen.getByRole("region", { name: "Названия" });

describe("Product names (variant 1: Polish on top, Russian under it)", () => {
  it("heads the card with the Polish name, then the Russian one and the receipt text", () => {
    renderCard(kefir);

    expect(screen.getByRole("heading", { level: 1, name: "Kefir naturalny Activia 280 g" })).toBeInTheDocument();
    expect(screen.getByText("Кефир натуральный Activia, 280 г")).toBeInTheDocument();
    expect(screen.getByText("ACTIV KEFI NAT 280").closest("p")).toHaveTextContent("В чеке: ACTIV KEFI NAT 280");
    // Without the edit callbacks the «Названия» block is not there.
    expect(screen.queryByRole("region", { name: "Названия" })).not.toBeInTheDocument();
  });

  it("falls back to the receipt text, unmarked, when there is no name", () => {
    renderCard({ ...kefir, namePl: null, namePlSource: null, nameRu: null, nameRuSource: null });

    expect(screen.getByRole("heading", { level: 1, name: "ACTIV KEFI NAT 280" })).toBeInTheDocument();
    expect(screen.queryByText(/В чеке:/)).not.toBeInTheDocument();
    expect(screen.queryByText("предложено")).not.toBeInTheDocument();
  });

  it("edits a name, confirms the suggested ones with «Верно» and asks again with «Предложить заново»", async () => {
    const onSetName = vi.fn().mockResolvedValue({});
    const onConfirmNames = vi.fn().mockResolvedValue({});
    const onSuggestNames = vi.fn().mockResolvedValue({});
    renderCard(kefir, { onSetName, onConfirmNames, onSuggestNames });

    const block = names();
    expect(within(block).getByText("По-польски").closest("p")).not.toHaveTextContent("предложено");
    expect(within(block).getByText("По-русски").closest("p")).toHaveTextContent("По-русски предложено");

    fireEvent.click(within(block).getByRole("button", { name: "Изменить русское название" }));
    const alert = dialog("По-русски");
    fireEvent.change(within(alert).getByRole("textbox"), { target: { value: "  Кефир Activia  " } });
    fireEvent.click(within(alert).getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onSetName).toHaveBeenCalledWith("ru", "Кефир Activia"));

    fireEvent.click(within(block).getByRole("button", { name: "Верно" }));
    await waitFor(() => expect(onConfirmNames).toHaveBeenCalledTimes(1));
    fireEvent.click(within(block).getByRole("button", { name: "Предложить заново" }));
    await waitFor(() => expect(onSuggestNames).toHaveBeenCalledTimes(1));
  });

  it("offers neither «Верно» nor «Предложить заново» once both names are the person's, and says when asking failed", async () => {
    const { unmount } = renderCard(
      { ...kefir, nameRuSource: "confirmed" },
      { onSetName: vi.fn(), onConfirmNames: vi.fn(), onSuggestNames: vi.fn() }
    );
    expect(within(names()).queryByRole("button", { name: "Верно" })).not.toBeInTheDocument();
    expect(within(names()).queryByRole("button", { name: "Предложить заново" })).not.toBeInTheDocument();
    unmount();

    renderCard(
      { ...kefir, namePl: null, namePlSource: null, nameRu: null, nameRuSource: null },
      { onSetName: vi.fn(), onConfirmNames: vi.fn(), onSuggestNames: vi.fn().mockRejectedValue(new Error("503")) }
    );
    expect(within(names()).getAllByText("Нет — показываем текст из чека")).toHaveLength(2);
    // Nothing suggested: nothing to confirm, but the AI can be asked.
    expect(within(names()).queryByRole("button", { name: "Верно" })).not.toBeInTheDocument();
    fireEvent.click(within(names()).getByRole("button", { name: "Предложить заново" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Сейчас не получилось предложить названия");
  });

  it("lists products by their Polish name with the Russian one under it, and the receipt text when there is none", () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <ProductCatalog
            query="кефир"
            onQueryChange={() => undefined}
            products={[
              summary({ namePl: "Kefir naturalny Activia 280 g", namePlSource: "suggested", nameRu: "Кефир натуральный Activia, 280 г" }),
              summary({ id: "m", name: "MLEKO 3,2 1L", namePl: "Mleko 3,2% Łaciate 1 l", namePlSource: "confirmed", nameRu: "Молоко 3,2% Łaciate, 1 л", nameRuSource: "suggested" }),
              summary({ id: "b", name: "SER GOUDA PLAST 150" })
            ]}
            totalCount={3}
            needsReviewCount={0}
          />
        </MemoryRouter>
      </AppProviders>
    );

    const row = screen.getByRole("link", { name: /Kefir naturalny Activia 280 g/ });
    expect(within(row).getByRole("heading", { level: 2 })).toHaveTextContent("Kefir naturalny Activia 280 g");
    expect(within(row).getByRole("img", { name: "предложено" })).toBeInTheDocument();
    expect(within(row).getByText("Кефир натуральный Activia, 280 г").closest("p")).toHaveTextContent(/^Кефир натуральный Activia, 280 г · 2 покупки$/);
    expect(within(row).queryByText("ACTIV KEFI NAT 280")).not.toBeInTheDocument();

    // The ✦ sticks to the last word, so a narrow row never wraps it alone.
    expect(within(row).getByRole("img", { name: "предложено" }).closest(".pm-nowrap")?.textContent).toBe("g\u00a0");

    const russianOnly = screen.getByRole("link", { name: /Mleko 3,2% Łaciate 1 l/ });
    expect(within(russianOnly).getByRole("img", { name: "предложено" })).toBeInTheDocument();

    const raw = screen.getByRole("link", { name: /SER GOUDA PLAST 150/ });
    expect(within(raw).getByRole("heading", { level: 2 })).toHaveTextContent(/^SER GOUDA PLAST 150$/);
    expect(within(raw).queryByRole("img", { name: "предложено" })).not.toBeInTheDocument();
  });

  it("keeps the receipt text as a purchase line's name with the product's Polish name under it", () => {
    render(
      <AppProviders locale="ru">
        <ProductLinkNote text="Связано с «Kefir naturalny Activia 280 g»" lineName="ACTIV KEFI NAT 280" productName="ACTIV KEFI NAT 280" humanName="Kefir naturalny Activia 280 g" />
      </AppProviders>
    );
    expect(screen.getByText("→ Kefir naturalny Activia 280 g")).toBeInTheDocument();
    expect(screen.getByText("Связано с «Kefir naturalny Activia 280 g»")).toHaveClass("visually-hidden");
  });
});
