import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { BrowserRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "../api/client";
import type { PurchaseDetail, ReceiptView } from "../api/types";
import { ItemLink, Page } from "../design-system/components";
import { ReceiptDetailPage } from "../features/receipts/ReceiptDetailPage";
import { PurchaseDetailPage } from "../features/purchases/PurchaseDetailPage";
import { dialog } from "../test/ionicHelpers";
import { AppProviders } from "./AppProviders";
import { useReturnState } from "./backNavigation";
import { clearPlaces } from "./place";

/*
 * «Назад» goes back exactly one step, on the real browser history
 * (BrowserRouter on jsdom): an inner screen or a sheet is a step of its own,
 * for the header chevron as for the browser's (or iPhone's) back.
 */

vi.mock("../api/client", async (importOriginal) => ({
  ApiError: (await importOriginal<typeof import("../api/client")>()).ApiError,
  getReceipt: vi.fn(), reviewReceipt: vi.fn(), saveReceipt: vi.fn(), processReceipt: vi.fn(),
  getReceiptAuditTrail: vi.fn(async () => []),
  getPurchase: vi.fn(), updatePurchase: vi.fn(), postLineApprovals: vi.fn(),
  getHaulPhotoStatus: vi.fn(async () => ({ enabled: false })),
  listPurchaseImpressions: vi.fn(async () => []), createImpression: vi.fn(), deleteImpression: vi.fn(),
  getProductCatalog: vi.fn(), getProduct: vi.fn(),
  fetchProtectedBlob: vi.fn(() => new Promise(() => undefined))
}));

const receipt: ReceiptView = {
  id: "ec0383a7-10ad-4548-ad5d-bb5ecb776c9b", status: "ready", uploadedAt: "2026-08-03T12:00:00Z", updatedAt: "2026-08-03T12:01:00Z",
  originalFileName: "receipt.jpg", sourceImageUrl: "/image", imageParts: [{ position: 0, contentType: "image/jpeg", sizeBytes: 42, sourceImageUrl: "/image/0" }],
  provider: null, model: null, processingDurationMs: null, warnings: [], issues: [], error: null, savedPurchaseId: null,
  snapshot: { merchantName: "Lidl", purchasedAt: null, currency: "PLN", receiptTotal: 7, items: [
    { name: "Mleko", quantity: 1, unit: null, unitPrice: 4, lineTotal: 4 },
    { name: "Chleb", quantity: 1, unit: null, unitPrice: 3, lineTotal: 3 }
  ] }
};

const purchase: PurchaseDetail = {
  id: "p1", sourceReceiptId: null, merchantName: "Żabka", purchasedAt: "2026-07-01T10:00:00Z", currency: "PLN", receiptTotal: 15,
  createdAt: "2026-07-01T10:00:00Z", updatedAt: "2026-07-01T10:00:00.123Z", issues: [],
  items: [{
    id: "line-kawa", position: 0, name: "KAWA", quantity: 1, unit: "szt", unitPrice: 15, lineTotal: 15, kind: "product",
    discountedItemPosition: null, productId: null, productName: null, productNeedsReview: false, boundingBox: null, approved: false
  }]
};

function Home() {
  const returnState = useReturnState();
  return (
    <Page title="Главная">
      <ItemLink to={`/receipts/${receipt.id}`} state={returnState}>Open receipt</ItemLink>
      <ItemLink to="/purchases/p1" state={returnState}>Open purchase</ItemLink>
      <ItemLink to="/needs-attention?category=milk">Open queue</ItemLink>
    </Page>
  );
}

function Queue() {
  const returnState = useReturnState();
  return (
    <Page title="Нужно проверить">
      <ItemLink to="/purchases/p1?edit=1" state={returnState}>Correct purchase</ItemLink>
    </Page>
  );
}

function renderApp() {
  render(
    <AppProviders locale="ru">
      <BrowserRouter>
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/receipts/:receiptId" element={<ReceiptDetailPage />} />
          <Route path="/purchases/:purchaseId" element={<PurchaseDetailPage />} />
          <Route path="/purchases" element={<h1>Purchase history</h1>} />
          <Route path="/needs-attention" element={<Queue />} />
        </Routes>
      </BrowserRouter>
    </AppProviders>
  );
}

const chevron = () => screen.getByRole("link", { name: "Главная" });
const fullEditorButton = () => screen.queryByRole("button", { name: /^Магазин, дата и итог/ });
const browserBack = () => act(() => window.history.back());

describe("«назад» is one step", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clearPlaces();
    window.history.replaceState(null, "", "/");
    vi.mocked(api.getReceipt).mockResolvedValue(receipt);
    vi.mocked(api.getPurchase).mockResolvedValue(purchase);
  });

  it("receipt: the chevron in the full editor returns to the carousel, then to Home", async () => {
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open receipt" }));
    fireEvent.click(await screen.findByRole("button", { name: /^Магазин, дата и итог/ }));
    expect(screen.getByLabelText("Магазин")).toHaveValue("Lidl");

    // In the editor the chevron says where it goes: back on this screen, not «Главная».
    expect(screen.queryByRole("link", { name: "Главная" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("link", { name: "Назад" }));
    await waitFor(() => expect(fullEditorButton()).toBeInTheDocument());
    expect(window.location.pathname).toBe(`/receipts/${receipt.id}`);
    expect(screen.queryByLabelText("Магазин")).not.toBeInTheDocument();

    fireEvent.click(chevron());
    expect(await screen.findByRole("link", { name: "Open receipt" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/");
  });

  it("receipt: browser back closes a line sheet, then the full editor, then leaves", async () => {
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open receipt" }));
    fireEvent.click(await screen.findByRole("button", { name: /^Магазин, дата и итог/ }));
    fireEvent.click(screen.getByRole("button", { name: /^Chleb/ }));
    expect(within(dialog("Строка 2")).getByLabelText(/^Название/)).toHaveValue("Chleb");

    browserBack();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByLabelText("Магазин")).toHaveValue("Lidl");

    browserBack();
    await waitFor(() => expect(fullEditorButton()).toBeInTheDocument());
    expect(window.location.pathname).toBe(`/receipts/${receipt.id}`);

    browserBack();
    expect(await screen.findByRole("link", { name: "Open receipt" })).toBeInTheDocument();
  });

  it("receipt: «Проверить строки по одной» leaves no step behind", async () => {
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open receipt" }));
    fireEvent.click(await screen.findByRole("button", { name: /^Магазин, дата и итог/ }));
    fireEvent.click(screen.getByRole("button", { name: "Проверить строки по одной" }));
    await waitFor(() => expect(window.history.state).not.toHaveProperty("pmLayer"));

    browserBack();
    expect(await screen.findByRole("link", { name: "Open receipt" })).toBeInTheDocument();
  });

  it("receipt: saving from the full editor replaces its step, so back from the list does not reopen it", async () => {
    vi.mocked(api.reviewReceipt).mockResolvedValue(receipt);
    vi.mocked(api.saveReceipt).mockResolvedValue({ ...purchase, id: "saved" });
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open receipt" }));
    fireEvent.click(await screen.findByRole("button", { name: /^Магазин, дата и итог/ }));
    fireEvent.click(screen.getAllByRole("button", { name: "Сохранить покупку" }).at(-1)!);
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();

    browserBack();
    await waitFor(() => expect(window.location.pathname).toBe(`/receipts/${receipt.id}`));
    await waitFor(() => expect(fullEditorButton()).toBeInTheDocument());
    expect(screen.queryByLabelText("Магазин")).not.toBeInTheDocument();
  });

  it("receipt: the summary after the last product is a step — browser back and the chevron return to the last product", async () => {
    vi.mocked(api.getPurchase).mockRejectedValue(new Error("not needed"));
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open receipt" }));
    fireEvent.click(await screen.findByRole("button", { name: "Верно, дальше" }));
    fireEvent.click(screen.getByRole("button", { name: "Верно, к итогу" }));
    expect(screen.getByRole("heading", { name: "Всё проверено" })).toBeInTheDocument();
    await waitFor(() => expect(window.history.state).toHaveProperty("pmLayer", 1));

    browserBack();
    await waitFor(() => expect(screen.queryByRole("heading", { name: "Всё проверено" })).not.toBeInTheDocument());
    expect(screen.getByText("Товар 2 из 2")).toBeInTheDocument();
    expect(screen.getByLabelText("Название")).toHaveValue("Chleb");
    expect(window.location.pathname).toBe(`/receipts/${receipt.id}`);

    fireEvent.click(screen.getByRole("button", { name: "Сохранить сейчас" }));
    expect(screen.getByRole("heading", { name: "Всё проверено" })).toBeInTheDocument();
    // In the summary the chevron says where it goes: back on this screen, not «Главная».
    expect(screen.queryByRole("link", { name: "Главная" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("link", { name: "Назад" }));
    await waitFor(() => expect(screen.getByText("Товар 2 из 2")).toBeInTheDocument());

    fireEvent.click(chevron());
    expect(await screen.findByRole("link", { name: "Open receipt" })).toBeInTheDocument();
  });

  it("receipt: saving from the summary replaces its step, so back from the list returns to the products", async () => {
    vi.mocked(api.reviewReceipt).mockResolvedValue(receipt);
    vi.mocked(api.saveReceipt).mockResolvedValue({ ...purchase, id: "saved" });
    vi.mocked(api.getPurchase).mockRejectedValue(new Error("not needed"));
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open receipt" }));
    fireEvent.click(await screen.findByRole("button", { name: "Сохранить сейчас" }));
    expect(screen.getByRole("heading", { name: "Итог покупки" })).toBeInTheDocument();
    await waitFor(() => expect(window.history.state).toHaveProperty("pmLayer", 1));
    fireEvent.click(screen.getByRole("button", { name: "Сохранить покупку" }));
    expect(await screen.findByRole("heading", { name: "Purchase history" })).toBeInTheDocument();

    browserBack();
    await waitFor(() => expect(window.location.pathname).toBe(`/receipts/${receipt.id}`));
    expect(await screen.findByText("Товар 1 из 2")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Итог покупки" })).not.toBeInTheDocument();
  });

  it("purchase: browser back closes the line review and stays on the purchase", async () => {
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open purchase" }));
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));
    expect(within(dialog()).getByLabelText(/^Название/)).toHaveValue("KAWA");

    browserBack();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(window.location.pathname).toBe("/purchases/p1");

    fireEvent.click(chevron());
    expect(await screen.findByRole("link", { name: "Open purchase" })).toBeInTheDocument();
  });

  it("purchase: a line review with an unsaved correction saves it before closing on back", async () => {
    vi.mocked(api.updatePurchase).mockImplementation(async (_id, request) => ({
      ...purchase, items: purchase.items.map((item, position) => ({ ...item, name: request.items[position].name }))
    }));
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open purchase" }));
    fireEvent.click(await screen.findByRole("button", { name: /^KAWA.*сверить с чеком$/ }));
    fireEvent.change(within(dialog()).getByLabelText(/^Название/), { target: { value: "KAWA ZIARNISTA" } });

    browserBack();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(api.updatePurchase).toHaveBeenCalledTimes(1);
    expect(vi.mocked(api.updatePurchase).mock.calls[0][1].items[0]).toMatchObject({ name: "KAWA ZIARNISTA" });
    expect(window.location.pathname).toBe("/purchases/p1");
  });

  it("purchase: «Исправить» → «Отмена» goes back one step, so the next back leaves the purchase", async () => {
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open purchase" }));
    fireEvent.click(await screen.findByRole("button", { name: "Исправить" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Исправить" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Отмена" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Żabka" })).toBeInTheDocument();
    expect(window.location.search).toBe("");

    browserBack();
    expect(await screen.findByRole("link", { name: "Open purchase" })).toBeInTheDocument();
  });

  it("purchase: saving a correction opened from the queue goes back to the queue entry, with no dead step", async () => {
    vi.mocked(api.updatePurchase).mockResolvedValue(purchase);
    renderApp();
    fireEvent.click(await screen.findByRole("link", { name: "Open queue" }));
    fireEvent.click(await screen.findByRole("link", { name: "Correct purchase" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Исправить" })).toBeInTheDocument();
    const queueLength = window.history.length;

    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    expect(await screen.findByRole("link", { name: "Correct purchase" })).toBeInTheDocument();
    expect(`${window.location.pathname}${window.location.search}`).toBe("/needs-attention?category=milk");
    expect(window.history.length).toBe(queueLength);

    browserBack();
    expect(await screen.findByRole("link", { name: "Open queue" })).toBeInTheDocument();
  });
});
