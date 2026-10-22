import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { BrowserRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getPurchaseFeed } from "../api/client";
import { ItemLink, Page } from "../design-system/components";
import { PurchaseFeedSection } from "../features/purchases/PurchaseFeedSection";
import { dialog, openFilters, row } from "../test/ionicHelpers";
import { AppProviders } from "./AppProviders";

/*
 * A filter sheet changes the screen's address while it is open (filters
 * replace the entry). Closing it, by «Готово» or by «назад», keeps the
 * filters and leaves no extra step (real browser history, BrowserRouter).
 */

vi.mock("../api/client", () => ({
  getPurchaseFeed: vi.fn(),
  listCategories: vi.fn().mockResolvedValue([
    { id: "dairy", parentId: null, name: "Молочное и яйца", systemKey: "dairy", position: 0, productCount: 0 }
  ]),
  listBrands: vi.fn().mockResolvedValue([]),
  listTags: vi.fn().mockResolvedValue([])
}));

function Home() {
  return <Page title="Главная"><ItemLink to="/purchases">Open purchases</ItemLink></Page>;
}

function renderApp() {
  render(
    <AppProviders locale="ru">
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/purchases" element={<PurchaseFeedSection />} />
        </Routes>
      </BrowserRouter>
    </AppProviders>
  );
}

const layerMark = () => (window.history.state as { pmLayer?: number } | null)?.pmLayer;
const browserBack = () => act(() => window.history.back());

async function openPurchasesWithFilters() {
  renderApp();
  fireEvent.click(await screen.findByRole("link", { name: "Open purchases" }));
  await screen.findByRole("heading", { level: 1, name: "Покупки" });
  const sheet = openFilters();
  await waitFor(() => expect(layerMark()).toBe(1));
  return sheet;
}

describe("a filter sheet is one step, and its filters stay", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/");
    vi.mocked(getPurchaseFeed).mockReset().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, truncated: false });
  });

  it("«Готово» after choosing a filter leaves no step: one back leaves the screen", async () => {
    const sheet = await openPurchasesWithFilters();
    const before = window.history.length;
    fireEvent.click(within(sheet).getByRole("tab", { name: "Нужно проверить" }));
    await waitFor(() => expect(window.location.search).toBe("?status=needsAttention"));
    expect(layerMark()).toBe(1);

    fireEvent.click(within(sheet).getByRole("button", { name: "Готово" }));
    await waitFor(() => expect(layerMark()).toBeUndefined());
    expect(window.location.search).toBe("?status=needsAttention");
    expect(window.history.length).toBe(before);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    browserBack();
    expect(await screen.findByRole("link", { name: "Open purchases" })).toBeInTheDocument();
  });

  it("back while the sheet is open closes it and keeps the chosen filters", async () => {
    const sheet = await openPurchasesWithFilters();
    fireEvent.click(within(sheet).getByRole("tab", { name: "Нужно проверить" }));
    await waitFor(() => expect(window.location.search).toBe("?status=needsAttention"));

    browserBack();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(window.location.search).toBe("?status=needsAttention"));
    expect(layerMark()).toBeUndefined();
    await waitFor(() => expect(getPurchaseFeed).toHaveBeenLastCalledWith(expect.objectContaining({ status: "needsAttention" })));

    browserBack();
    expect(await screen.findByRole("link", { name: "Open purchases" })).toBeInTheDocument();
  });

  it("a picker opened after a filter change closes alone on back; the sheet and filter stay", async () => {
    const sheet = await openPurchasesWithFilters();
    fireEvent.click(within(sheet).getByRole("tab", { name: "Нужно проверить" }));
    await waitFor(() => expect(window.location.search).toBe("?status=needsAttention"));
    fireEvent.click(await waitFor(() => row("Категория")));
    await waitFor(() => expect(layerMark()).toBe(2));

    browserBack();
    await waitFor(() => expect(layerMark()).toBe(1));
    expect(dialog("Фильтры")).toBeInTheDocument();
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(window.location.search).toBe("?status=needsAttention");
  });
});
