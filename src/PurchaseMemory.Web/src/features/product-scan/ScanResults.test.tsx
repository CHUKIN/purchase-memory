import "fake-indexeddb/auto";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, createProduct } from "../../api/client";
import { AppProviders } from "../../app/AppProviders";
import { lookupOpenFoodFacts } from "./openFoodFacts";
import { deviceProductMediaStore } from "./productMediaStore";
import { NotFoundResult } from "./ScanResults";

vi.mock("../../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../api/client")>();
  return { ...actual, createProduct: vi.fn(), getProductCatalog: vi.fn() };
});

vi.mock("./openFoodFacts", () => ({ lookupOpenFoodFacts: vi.fn() }));

function renderResult(code: string, onLinked = vi.fn()) {
  render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <NotFoundResult code={code} locale="ru-RU" onLinked={onLinked} onScanAgain={vi.fn()} />
      </MemoryRouter>
    </AppProviders>
  );
  return onLinked;
}

const created = (id: string, name: string) => ({
  id, name, normalizedName: name.toLowerCase(), origin: "barcode" as const, needsReview: false,
  categoryId: null, categorySource: null, brand: null, brandSource: null, createdAt: "", updatedAt: ""
});

describe("NotFoundResult «Новый товар»", () => {
  beforeEach(async () => {
    vi.mocked(createProduct).mockReset();
    vi.mocked(lookupOpenFoodFacts).mockReset();
    await deviceProductMediaStore.clear();
  });

  it("creates the product with the Open Food Facts name, ties the code to it and shows it", async () => {
    vi.mocked(lookupOpenFoodFacts).mockResolvedValue({
      status: "found",
      product: { name: "Kefir naturalny", brand: "Bakoma", quantity: "400 g", imageUrl: null }
    });
    vi.mocked(createProduct).mockResolvedValue(created("new-kefir", "Kefir naturalny"));
    const onLinked = renderResult("5900512320045");

    // No more «products come only from receipts».
    expect(screen.queryByText(/только из чеков/)).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole("button", { name: /^Новый товар.*Kefir naturalny/ }));
    expect(screen.getByLabelText("Название товара")).toHaveValue("Kefir naturalny");
    expect(screen.getByLabelText("Бренд (если знаете)")).toHaveValue("Bakoma");
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    await waitFor(() => expect(onLinked).toHaveBeenCalledWith("new-kefir"));
    expect(createProduct).toHaveBeenCalledWith({ name: "Kefir naturalny", brand: "Bakoma", origin: "barcode" });
    expect(await deviceProductMediaStore.findProductByBarcode("5900512320045")).toBe("new-kefir");
  });

  it("asks for a name when Open Food Facts does not know the code", async () => {
    vi.mocked(lookupOpenFoodFacts).mockResolvedValue({ status: "notFound" });
    vi.mocked(createProduct).mockResolvedValue(created("new-1", "Ser kozi"));
    const onLinked = renderResult("5901234123457");

    fireEvent.click(await screen.findByRole("button", { name: /^Новый товар.*этого кода нет/ }));
    const create = screen.getByRole("button", { name: "Создать товар" });
    expect(create).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Название товара"), { target: { value: "  Ser kozi " } });
    fireEvent.click(create);

    await waitFor(() => expect(onLinked).toHaveBeenCalledWith("new-1"));
    expect(createProduct).toHaveBeenCalledWith({ name: "Ser kozi", brand: null, origin: "barcode" });
  });

  it("uses the existing product when one already has that name", async () => {
    vi.mocked(lookupOpenFoodFacts).mockResolvedValue({ status: "notFound" });
    vi.mocked(createProduct).mockRejectedValue(new ApiError("exists", 409, null, null, null, "old-kefir"));
    const onLinked = renderResult("5900000000017");

    fireEvent.click(await screen.findByRole("button", { name: /^Новый товар/ }));
    fireEvent.change(screen.getByLabelText("Название товара"), { target: { value: "Kefir" } });
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    await waitFor(() => expect(onLinked).toHaveBeenCalledWith("old-kefir"));
    expect(await deviceProductMediaStore.findProductByBarcode("5900000000017")).toBe("old-kefir");
  });

  it("says so when the product cannot be created and links nothing", async () => {
    vi.mocked(lookupOpenFoodFacts).mockResolvedValue({ status: "error" });
    vi.mocked(createProduct).mockRejectedValue(new Error("offline"));
    const onLinked = renderResult("5900000000024");

    fireEvent.click(await screen.findByRole("button", { name: /^Новый товар/ }));
    fireEvent.change(screen.getByLabelText("Название товара"), { target: { value: "Kefir" } });
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось создать товар. Проверьте интернет.");
    expect(onLinked).not.toHaveBeenCalled();
    expect(await deviceProductMediaStore.findProductByBarcode("5900000000024")).toBeNull();
  });
});
