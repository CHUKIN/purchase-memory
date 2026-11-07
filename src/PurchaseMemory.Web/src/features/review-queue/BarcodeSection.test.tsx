import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import type { RememberedBarcode } from "../product-scan/useProductMedia";
import { BarcodeItems, type BarcodeItemsProps } from "./BarcodeSection";

const withOff: RememberedBarcode = {
  code: "5900512320045",
  rememberedAt: Date.UTC(2026, 10, 1, 10),
  off: { name: "Kefir naturalny", brand: "Bakoma", quantity: "400 g", imageUrl: null }
};
const unknown: RememberedBarcode = { code: "5901234123457", rememberedAt: Date.UTC(2026, 10, 2, 10), off: null };

function renderItems(props: Partial<BarcodeItemsProps>, codes = [withOff, unknown]) {
  const onLink = vi.fn().mockResolvedValue(true);
  const onForget = vi.fn().mockResolvedValue(undefined);
  render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <BarcodeItems codes={codes} onLink={onLink} onForget={onForget} {...props} />
      </MemoryRouter>
    </AppProviders>
  );
}

describe("remembered codes: «Создать товар»", () => {
  it("has no create button when the screen cannot create products", () => {
    renderItems({});
    expect(screen.queryByRole("button", { name: /Создать товар для штрихкода/ })).not.toBeInTheDocument();
  });

  it("shows the database name under the code and prefills the form with it", async () => {
    const onCreate = vi.fn().mockResolvedValue({ ok: true, productId: "new-kefir" });
    const onOpenProduct = vi.fn();
    renderItems({ onCreate, onOpenProduct });

    expect(screen.getByText("Bakoma · Kefir naturalny")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Создать товар для штрихкода 5900512320045" }));
    expect(screen.getByLabelText("Название товара")).toHaveValue("Kefir naturalny");
    expect(screen.getByLabelText("Бренд (если знаете)")).toHaveValue("Bakoma");
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    await waitFor(() => expect(onOpenProduct).toHaveBeenCalledWith("new-kefir"));
    expect(onCreate).toHaveBeenCalledWith("5900512320045", "Kefir naturalny", "Bakoma", "Kefir naturalny");
  });

  it("asks for a name when the database did not know the code", async () => {
    const onCreate = vi.fn().mockResolvedValue({ ok: true, productId: "new-1" });
    renderItems({ onCreate, onOpenProduct: vi.fn() });

    fireEvent.click(screen.getByRole("button", { name: "Создать товар для штрихкода 5901234123457" }));
    expect(screen.getByRole("button", { name: "Создать товар" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Название товара"), { target: { value: " Ser kozi " } });
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    await waitFor(() => expect(onCreate).toHaveBeenCalledWith("5901234123457", "Ser kozi", "", null));
  });

  it("keeps the form open and says why when the code went to another product", async () => {
    const onCreate = vi.fn().mockResolvedValue({ ok: false, reason: "taken" });
    const onOpenProduct = vi.fn();
    renderItems({ onCreate, onOpenProduct });

    fireEvent.click(screen.getByRole("button", { name: "Создать товар для штрихкода 5900512320045" }));
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Этот штрихкод уже привязан к другому товару.");
    expect(onOpenProduct).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Название товара")).toHaveValue("Kefir naturalny");
  });

  it("says so when the request fails", async () => {
    const onCreate = vi.fn().mockRejectedValue(new Error("offline"));
    renderItems({ onCreate, onOpenProduct: vi.fn() });

    fireEvent.click(screen.getByRole("button", { name: "Создать товар для штрихкода 5900512320045" }));
    fireEvent.click(screen.getByRole("button", { name: "Создать товар" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось создать товар. Проверьте интернет.");
  });
});
