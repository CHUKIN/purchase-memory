import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getRecognitionStatus } from "../api/client";
import { App } from "./App";
import { AppProviders } from "./AppProviders";
import { localeStorageKey } from "./LocaleContext";

vi.mock("../api/client", () => ({
  getReviewQueueCount: vi.fn().mockResolvedValue({ totalCount: 0, byKind: {} }),
  getReviewQueue: vi.fn().mockResolvedValue({ totalCount: 0, groups: [] }),
  getRecognitionStatus: vi.fn().mockResolvedValue({ available: true, mode: "Hybrid", models: ["glm-ocr", "qwen3.5:4b"], message: null }),
  listRecentReceipts: vi.fn().mockResolvedValue([]),
  listMyUnsavedReceipts: vi.fn().mockResolvedValue([]),
  getPurchaseFeed: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false, truncated: false }),
  getProductCatalog: vi.fn().mockResolvedValue({ items: [], page: 1, pageSize: 30, totalCount: 0, hasMore: false, pendingMatchCount: 0 }),
  listCategories: vi.fn().mockResolvedValue([]),
  listBrands: vi.fn().mockResolvedValue([]),
  listTags: vi.fn().mockResolvedValue([]),
  listUnratedPurchaseItems: vi.fn().mockResolvedValue([]),
  createImpression: vi.fn(),
  deleteImpression: vi.fn(),
  getAnalyticsContext: vi.fn().mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: [], brands: [] }),
  listAnalyticsHistory: vi.fn().mockResolvedValue({ items: [], hasMore: false })
}));

const tabs = () => screen.getByRole("navigation", { name: /Разделы|Sections/ });

describe("App", () => {
  beforeEach(() => {
    window.localStorage.clear();
    window.history.replaceState({}, "", "/");
    vi.mocked(getRecognitionStatus).mockResolvedValue({ available: true, mode: "Hybrid", models: ["glm-ocr", "qwen3.5:4b"], message: null });
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: vi.fn(() => "blob:receipt") });
    Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: vi.fn() });
  });

  afterEach(() => {
    window.history.replaceState({}, "", "/");
  });

  it("starts on Home with one main action and no invented purchase metrics", async () => {
    render(<AppProviders><App /></AppProviders>);
    expect(await screen.findByRole("heading", { level: 1, name: "Главная" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Сфотографировать чек/ })).toHaveAttribute("href", "/purchases/new");
    expect(screen.getByRole("link", { name: /^Нужно проверить/ })).toHaveAttribute("href", "/needs-attention");
    expect(await screen.findByText(/Сервис готов принимать фотографии\./)).toBeInTheDocument();
    expect(screen.queryByText("1 284,70 zł")).not.toBeInTheDocument();
  });

  it("supports deep links and keeps the four sections in the tab bar", async () => {
    window.history.replaceState({}, "", "/products");
    render(<AppProviders><App /></AppProviders>);

    expect(await screen.findByRole("heading", { level: 1, name: "Товары" })).toBeInTheDocument();
    for (const name of ["Главная", "Покупки", "Товары", "Аналитика"]) {
      expect(within(tabs()).getByRole("link", { name })).toBeInTheDocument();
    }
    expect(within(tabs()).getByRole("link", { name: "Товары" })).toHaveAttribute("aria-current", "page");
  });

  it("keeps help and sign-out in «Ещё» on Home, without a language switch", async () => {
    render(<AppProviders><App /></AppProviders>);
    fireEvent.click(await screen.findByRole("button", { name: "Ещё: подсказки, выход" }));
    const settings = screen.getByRole("dialog");
    expect(within(settings).getByRole("link", { name: /Подсказки/ })).toHaveAttribute("href", "/help");
    expect(within(settings).queryByRole("tab", { name: "English" })).not.toBeInTheDocument();
    expect(within(settings).queryByText("Язык")).not.toBeInTheDocument();
  });

  it("stays Russian even if English was stored before", async () => {
    window.localStorage.setItem(localeStorageKey, "en");
    render(<AppProviders><App /></AppProviders>);
    expect(await screen.findByRole("heading", { level: 1, name: "Главная" })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe("ru");
  });

  it("uses browser history for tab navigation and back", async () => {
    render(<AppProviders><App /></AppProviders>);
    await screen.findByRole("heading", { level: 1, name: "Главная" });
    fireEvent.click(within(tabs()).getByRole("link", { name: "Аналитика" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Аналитика" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/analytics");

    window.history.back();
    window.dispatchEvent(new PopStateEvent("popstate"));
    await waitFor(() => expect(window.location.pathname).toBe("/"));
    expect(await screen.findByRole("heading", { level: 1, name: "Главная" })).toBeInTheDocument();
  });

  it("keeps receipt recognition disabled after the status request fails", async () => {
    vi.mocked(getRecognitionStatus).mockRejectedValue(new Error("Backend unavailable"));
    window.history.replaceState({}, "", "/purchases/new");
    render(<AppProviders><App /></AppProviders>);

    expect(await screen.findByText("Сервис распознавания недоступен")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Выбрать из галереи"), { target: { files: [new File(["receipt"], "receipt.jpg", { type: "image/jpeg" })] } });

    expect(screen.getByRole("button", { name: "Загрузить и распознать" })).toBeDisabled();
  });
});
