import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getProductCatalog } from "../../api/client";
import type { ProductSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { CatalogSearchModal } from "./CatalogSearchModal";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getProductCatalog: vi.fn()
}));

vi.mock("../product-scan/ProductPhoto", () => ({
  ProductThumb: ({ photoId, placeholder }: { photoId?: string | null; placeholder?: boolean }) =>
    photoId || placeholder ? <span data-testid="thumb" data-photo={photoId ?? ""} /> : null
}));

const product = (id: string, name: string, mainPhotoId: string | null) => ({
  id, name, normalizedName: name.toLowerCase(), origin: "receipt", needsReview: false, purchaseCount: 1, linkedItemCount: 1,
  aliasCount: 0, createdAt: "", mainPhotoId
}) as unknown as ProductSummary;

describe("CatalogSearchModal thumbnails", () => {
  beforeEach(() => {
    vi.mocked(getProductCatalog).mockReset().mockResolvedValue({
      items: [product("p1", "Kefir", "photo-1"), product("p2", "Ser", null)],
      totalCount: 2
    } as never);
  });

  it("shows the main photo of a result from the catalog answer and a placeholder without one", async () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <CatalogSearchModal isOpen onClose={vi.fn()} title="Товар" subject="KEFIR" onChoose={vi.fn()} chooseLabel={(name) => `Выбрать ${name}`} />
        </MemoryRouter>
      </AppProviders>
    );

    fireEvent.input(await screen.findByRole("searchbox"), { target: { value: "ke" } });
    await waitFor(() => expect(screen.getAllByTestId("thumb")).toHaveLength(2));
    expect(screen.getAllByTestId("thumb").map((thumb) => thumb.getAttribute("data-photo"))).toEqual(["photo-1", ""]);
    // One catalog request for the whole list; photos need no per-row product request.
    expect(getProductCatalog).toHaveBeenCalledTimes(1);
  });
});
