import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  createImpression,
  deleteImpression,
  listProductImpressions,
  listProductRateableLines,
  listUnratedPurchaseItems
} from "../../api/client";
import type { Impression, RateablePurchaseItem } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { ImpressionForm } from "./ImpressionForm";
import { ImpressionHistory, StoreComparison } from "./ImpressionHistory";
import { ProductImpressionsSection, PurchaseLineImpressions, UnratedPurchasesBlock } from "./ImpressionSections";
import { RatingControl } from "./RatingControl";
import { findRow, pick } from "../../test/ionicHelpers";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return {
    ApiError: actual.ApiError,
    createImpression: vi.fn(),
    deleteImpression: vi.fn(),
    listProductImpressions: vi.fn(),
    listProductRateableLines: vi.fn(),
    listPurchaseImpressions: vi.fn(),
    listUnratedPurchaseItems: vi.fn(),
    fetchProtectedBlob: vi.fn(() => new Promise(() => undefined))
  };
});

function impression(overrides: Partial<Impression>): Impression {
  return {
    id: "i1",
    purchaseId: "p1",
    purchaseItemId: "line1",
    productId: "milk",
    itemName: "MLEKO 2%",
    rating: "like",
    note: null,
    photoUrl: null,
    authorUserId: "anna",
    authorEmail: "anna@example.com",
    isMine: true,
    createdAt: "2026-09-20T10:00:00Z",
    merchantName: "Lidl",
    purchasedAt: "2026-09-19T10:00:00Z",
    currency: "PLN",
    quantity: 1,
    unit: "szt",
    unitPrice: 3.49,
    lineTotal: 3.49,
    ...overrides
  };
}

const history: Impression[] = [
  impression({ id: "b1", authorUserId: "boris", authorEmail: "boris@example.com", isMine: false, rating: "dislike", merchantName: "Biedronka", note: "Кислое", createdAt: "2026-09-22T10:00:00Z" }),
  impression({ id: "a2", rating: "neutral", merchantName: "Biedronka", createdAt: "2026-09-21T10:00:00Z" }),
  impression({ id: "a1", rating: "like", merchantName: "Lidl", note: "Вкусное" })
];

const line = (overrides: Partial<RateablePurchaseItem>): RateablePurchaseItem => ({
  purchaseItemId: "line1",
  purchaseId: "p1",
  productId: "milk",
  productName: "Mleko 2%",
  itemName: "MLEKO 2%",
  merchantName: "Lidl",
  purchasedAt: "2026-09-19T10:00:00Z",
  currency: "PLN",
  unitPrice: 3.49,
  lineTotal: 3.49,
  ...overrides
});

function renderWithApp(node: React.ReactNode, locale: "ru" | "en" = "ru") {
  return render(<AppProviders locale={locale}><MemoryRouter>{node}</MemoryRouter></AppProviders>);
}

describe("impressions", () => {
  beforeEach(() => {
    vi.mocked(createImpression).mockReset();
    vi.mocked(deleteImpression).mockReset();
    vi.mocked(listProductImpressions).mockReset().mockResolvedValue(history);
    vi.mocked(listProductRateableLines).mockReset().mockResolvedValue([
      line({ purchaseItemId: "line2", merchantName: "Biedronka", purchasedAt: "2026-09-21T10:00:00Z", unitPrice: 2.99 }),
      line({})
    ]);
    vi.mocked(listUnratedPurchaseItems).mockReset().mockResolvedValue([]);
  });

  it("offers the three-level scale as one named radio group", () => {
    const onChange = vi.fn();
    renderWithApp(<RatingControl legend="Оценка" value="neutral" onChange={onChange} />);

    const group = screen.getByRole("tablist", { name: "Оценка" });
    const radios = within(group).getAllByRole("tab");
    expect(radios.map((radio) => radio.textContent)).toEqual(["Не нравится", "Средне", "Нравится"]);
    expect(within(group).getByRole("tab", { name: "Средне" })).toHaveAttribute("aria-selected", "true");

    fireEvent.click(within(group).getByRole("tab", { name: "Нравится" }));
    expect(onChange).toHaveBeenCalledWith("like");
  });

  it("uses English labels in the English locale", () => {
    renderWithApp(<RatingControl legend="Rating" value={null} onChange={vi.fn()} />, "en");
    expect(screen.getByRole("tab", { name: "So-so" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dislike" })).toBeInTheDocument();
  });

  it("groups history by person, the viewer first, and lets only the author delete", async () => {
    const onDelete = vi.fn().mockResolvedValue(undefined);
    renderWithApp(<ImpressionHistory impressions={history} locale="ru-RU" onDelete={onDelete} />);

    const groups = screen.getAllByRole("heading", { level: 3 });
    expect(groups.map((heading) => heading.textContent)).toEqual(["Вы · 2 впечатления", "boris@example.com · 1 впечатление"]);

    const boris = screen.getByRole("region", { name: "boris@example.com" });
    expect(within(boris).getByText("Кислое")).toBeInTheDocument();
    expect(within(boris).getByText(/Biedronka/)).toBeInTheDocument();
    expect(within(boris).queryByRole("button", { name: "Удалить" })).not.toBeInTheDocument();

    const mine = screen.getByRole("region", { name: "Вы" });
    expect(within(mine).getByText(/Lidl · .* · 3,49/)).toBeInTheDocument();
    fireEvent.click(within(mine).getAllByRole("button", { name: "Удалить" })[0]);
    expect(onDelete).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Да, удалить" }));
    await waitFor(() => expect(onDelete).toHaveBeenCalledWith("a2"));
  });

  it("compares opinions by store in a table", () => {
    renderWithApp(<StoreComparison impressions={history} />);
    const table = screen.getByRole("table");
    const biedronka = within(table).getByRole("row", { name: /Biedronka/ });
    expect(within(biedronka).getAllByRole("cell").map((cell) => cell.textContent)).toEqual([
      "1", "1", "0", "boris@example.com: Не нравится; Вы: Средне"
    ]);
    expect(within(table).getByRole("row", { name: /Lidl/ })).toHaveTextContent("Вы: Нравится");
  });

  it("does not submit without a rating, then sends rating, note and photo", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    renderWithApp(<ImpressionForm onSubmit={onSubmit} />);

    fireEvent.click(screen.getByRole("button", { name: "Сохранить впечатление" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Выберите оценку.");
    expect(onSubmit).not.toHaveBeenCalled();

    const photo = new File(["x"], "inside.jpg", { type: "image/jpeg" });
    fireEvent.click(screen.getByRole("tab", { name: "Нравится" }));
    fireEvent.change(screen.getByLabelText("Заметка (необязательно)"), { target: { value: "Хрустящее" } });
    fireEvent.change(screen.getByLabelText("Фото впечатления (необязательно)"), { target: { files: [photo] } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить впечатление" }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith({ rating: "like", note: "Хрустящее", photo }));
    expect(await screen.findByRole("status")).toHaveTextContent("Впечатление сохранено.");
    expect(screen.getByRole("tab", { name: "Нравится" })).toHaveAttribute("aria-selected", "false");
  });

  it("rates the chosen purchase from the product page and shows the history", async () => {
    vi.mocked(createImpression).mockResolvedValue(history[0]);
    renderWithApp(<ProductImpressionsSection productId="milk" locale="ru-RU" />);

    expect(await screen.findByRole("heading", { level: 2, name: "Впечатления" })).toBeInTheDocument();
    expect(await screen.findByRole("region", { name: "boris@example.com" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Сравнение по магазинам" })).toBeInTheDocument();

    // The most recent purchase is chosen until another one is picked.
    expect(await findRow("Какую покупку оцениваете")).toHaveTextContent(/Biedronka · .* · 2,99/);
    pick("Какую покупку оцениваете", "Lidl");
    fireEvent.click(screen.getByRole("tab", { name: "Средне" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить впечатление" }));

    await waitFor(() => expect(createImpression).toHaveBeenCalledWith({ purchaseItemId: "line1", rating: "neutral", note: "", photo: null }));
  });

  it("shows recent unrated purchases as one-tap ratings and hides itself when there are none", async () => {
    vi.mocked(listUnratedPurchaseItems).mockResolvedValue([line({ purchaseItemId: "fresh", productName: "Chleb", merchantName: "Żabka" })]);
    vi.mocked(createImpression).mockResolvedValue(history[0]);
    const { unmount } = renderWithApp(<UnratedPurchasesBlock locale="ru-RU" />);

    expect(await screen.findByRole("heading", { name: "Оценить недавнее" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Chleb" })).toHaveAttribute("href", "/products/milk");
    fireEvent.click(within(screen.getByRole("tablist", { name: "Оценка: Chleb" })).getByRole("tab", { name: "Нравится" }));
    await waitFor(() => expect(createImpression).toHaveBeenCalledWith({ purchaseItemId: "fresh", rating: "like", note: "", photo: null }));
    unmount();

    vi.mocked(listUnratedPurchaseItems).mockClear().mockResolvedValue([]);
    renderWithApp(<UnratedPurchasesBlock locale="ru-RU" />);
    await waitFor(() => expect(listUnratedPurchaseItems).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole("heading", { name: "Оценить недавнее" })).not.toBeInTheDocument();
  });

  it("summarises a purchase line's ratings per person and rates that exact line", async () => {
    vi.mocked(createImpression).mockResolvedValue(history[0]);
    const item = {
      id: "line1", position: 0, name: "MLEKO 2%", quantity: 1, unit: "szt", unitPrice: 3.49, lineTotal: 3.49, kind: "product" as const,
      discountedItemPosition: null, productId: "milk", productName: "Mleko", pendingMatch: false, candidates: [], boundingBox: null
    };
    renderWithApp(<PurchaseLineImpressions item={item} impressions={[...history, impression({ id: "other", purchaseItemId: "line9" })]} locale="ru-RU" />);

    const summary = screen.getByRole("list", { name: "Оценки «MLEKO 2%»" });
    expect(within(summary).getAllByRole("listitem").map((entry) => entry.textContent)).toEqual([
      "Вы: Средне", "boris@example.com: Не нравится"
    ]);

    fireEvent.click(screen.getByRole("button", { name: "Оценить «MLEKO 2%»" }));
    fireEvent.click(screen.getByRole("tab", { name: "Не нравится" }));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить впечатление" }));
    await waitFor(() => expect(createImpression).toHaveBeenCalledWith({ purchaseItemId: "line1", rating: "dislike", note: "", photo: null }));
  });
});
