import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getProduct, listProductImpressions } from "../../api/client";
import type { Impression, ProductDetail } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { FoundResult, Searching, slowLookupMs } from "./ScanResults";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getProduct: vi.fn(),
  listProductImpressions: vi.fn()
}));
vi.mock("./useProductMedia", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./useProductMedia")>()),
  useProductMedia: () => ({ data: { mainPhotoId: null, avoid: null } })
}));
vi.mock("./ProductPhoto", () => ({ ProductThumb: () => null }));

const point = (merchantName: string, unitPrice: number, purchasedAt: string) => ({ purchasedAt, merchantName, unitPrice, purchaseId: merchantName });
const latest = point("Biedronka", 5, "2026-11-01T10:00:00Z");

const detail = {
  id: "p1", name: "KEFIR", normalizedName: "kefir", createdAt: "", updatedAt: "",
  purchaseCount: 4, linkedItemCount: 4, unpricedItemCount: 0, aliases: [],
  frequency: { firstPurchasedAt: "2026-09-01T10:00:00Z", lastPurchasedAt: "2026-11-01T10:00:00Z", purchaseCount: 4, averageDaysBetweenPurchases: 20.4 },
  priceSeries: [{
    currency: "PLN", unit: "szt", minimumPrice: 3, averagePrice: 4, latestPrice: 5, latestPoint: latest,
    points: [point("Lidl", 3, "2026-10-01T10:00:00Z"), latest]
  }]
} as unknown as ProductDetail;

const impression = (rating: Impression["rating"]) => ({ id: "i", rating, note: "kwaśny", isMine: true }) as unknown as Impression;

function renderFound() {
  render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <FoundResult productId="p1" code="5900512320045" locale="ru-RU" onOpenProduct={vi.fn()} onScanAgain={vi.fn()} />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("FoundResult", () => {
  beforeEach(() => {
    vi.mocked(getProduct).mockReset().mockResolvedValue(detail);
    vi.mocked(listProductImpressions).mockReset().mockResolvedValue([]);
  });

  it("shows last purchase, how often, the cheapest store and the open-card action", async () => {
    renderFound();

    expect(await screen.findByText(/Biedronka/)).toBeInTheDocument();
    expect(screen.getByText(/Lidl/)).toBeInTheDocument();
    expect(screen.getByText("Как часто покупали").parentElement).toHaveTextContent("4 покупки · В среднем раз в 20 дн.");
    expect(screen.getByRole("button", { name: "Открыть карточку товара" })).toBeInTheDocument();
    expect(screen.queryByText("Вы отметили: не нравится")).not.toBeInTheDocument();
  });

  it("warns in red when you rated it «Не нравится»", async () => {
    vi.mocked(listProductImpressions).mockResolvedValue([impression("dislike")]);
    renderFound();

    const warning = await screen.findByText("Вы отметили: не нравится");
    expect(warning.closest(".pm-avoid-banner")).toHaveAttribute("role", "note");
  });

  it("does not warn for a like", async () => {
    vi.mocked(listProductImpressions).mockResolvedValue([impression("like")]);
    renderFound();

    await screen.findByText(/Biedronka/);
    expect(await screen.findByText("Нравится")).toBeInTheDocument();
    expect(screen.queryByText("Вы отметили: не нравится")).not.toBeInTheDocument();
  });
});

describe("Searching", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("mentions the waking server only after a slow answer", () => {
    render(<AppProviders locale="ru"><Searching code="5900512320045" /></AppProviders>);

    expect(screen.queryByText(/Сервер просыпается/)).not.toBeInTheDocument();
    act(() => { vi.advanceTimersByTime(slowLookupMs + 10); });
    expect(screen.getByText(/Сервер просыпается/)).toBeInTheDocument();
  });
});
