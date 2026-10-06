import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { discountLinks } from "./discountLines";
import { ReceiptDiscountLine } from "./ReceiptDiscountLine";

describe("ReceiptDiscountLine", () => {
  it("names the discount, its product and the price after the discount", () => {
    render(
      <AppProviders>
        <ReceiptDiscountLine name="OPUST" lineTotal={-30} currency="PLN" productName="Odkurzacz" productLineTotal={99.99} />
      </AppProviders>
    );

    const line = screen.getByRole("article", { name: /Скидка OPUST −30,00.*к товару Odkurzacz/ });
    expect(line).toBeInTheDocument();
    expect(screen.getByText("к «Odkurzacz»")).toBeInTheDocument();
    expect(screen.getByText(/−30,00/)).toBeInTheDocument();
    expect(screen.getByText(/69,99/)).toBeInTheDocument();
  });

  it("flags a discount that has no product above it", () => {
    render(
      <AppProviders locale="en">
        <ReceiptDiscountLine name="RABAT" lineTotal={-2.5} currency="PLN" productName={null} />
      </AppProviders>
    );

    expect(screen.getByRole("article", { name: /not attached to any item/ })).toBeInTheDocument();
    expect(screen.queryByText(/after discount/)).not.toBeInTheDocument();
  });

  it("edits the size as a positive number and reports it negative", () => {
    const onChange = vi.fn();
    const onDelete = vi.fn();
    render(
      <AppProviders>
        <ReceiptDiscountLine name="OPUST" lineTotal={-30} currency="PLN" productName="Odkurzacz" onChange={onChange} onDelete={onDelete} />
      </AppProviders>
    );

    const size = screen.getByLabelText("Размер скидки");
    expect(size).toHaveValue(30);
    fireEvent.change(size, { target: { value: "12.5" } });
    expect(onChange).toHaveBeenLastCalledWith({ name: "OPUST", lineTotal: -12.5 });

    fireEvent.click(screen.getByRole("button", { name: "Удалить скидку OPUST" }));
    expect(onDelete).toHaveBeenCalledOnce();
  });

  it("attaches each discount to the nearest product above it", () => {
    expect(
      discountLinks([{ kind: "discount" }, { kind: "product" }, { kind: "discount" }, {}, { kind: "discount" }]).map((link) => link.target)
    ).toEqual([null, null, 1, null, 3]);
  });

  it("treats a marked or oversized discount as a whole-receipt discount", () => {
    const links = discountLinks([
      { kind: "product", name: "Pudełko", lineTotal: 2 },
      { kind: "discount", name: "OPUST", lineTotal: -2 },
      { kind: "discount", name: "OPUST", lineTotal: -21.59 },
      { kind: "discount", name: "OPUST (podsuma)", lineTotal: -1 }
    ]);
    expect(links).toEqual([
      { target: null, appliesToReceipt: false },
      { target: 0, appliesToReceipt: false },
      { target: null, appliesToReceipt: true },
      { target: null, appliesToReceipt: true }
    ]);
  });

  it("labels a whole-receipt discount calmly and shows no price after it", () => {
    render(
      <AppProviders>
        <ReceiptDiscountLine name="OPUST (podsuma)" lineTotal={-21.59} currency="PLN" productName={null} appliesToReceipt />
      </AppProviders>
    );

    expect(screen.getByRole("article", { name: /Скидка на весь чек OPUST \(podsuma\) −21,59/ })).toBeInTheDocument();
    expect(screen.getByText("на весь чек")).toBeInTheDocument();
    expect(screen.queryByText(/не относится/)).not.toBeInTheDocument();
    expect(screen.queryByText(/после скидки/)).not.toBeInTheDocument();
  });

  it("hides the price after discount when it would not be positive", () => {
    render(
      <AppProviders>
        <ReceiptDiscountLine name="OPUST" lineTotal={-2} currency="PLN" productName="Pudełko" productLineTotal={2} />
      </AppProviders>
    );

    expect(screen.queryByText(/после скидки/)).not.toBeInTheDocument();
  });
});
