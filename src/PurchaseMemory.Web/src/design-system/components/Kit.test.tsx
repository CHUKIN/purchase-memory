import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { dialog, pick, row, setDate } from "../../test/ionicHelpers";
import { DateField, DecimalInput, parseDecimal, SelectRow } from ".";

describe("DecimalInput", () => {
  it("reads a comma and a dot as the decimal separator and ignores what is not a number yet", () => {
    expect(parseDecimal("12,5")).toBe(12.5);
    expect(parseDecimal(" 1 284.32 ")).toBe(1284.32);
    expect(parseDecimal("")).toBeNull();
    expect(parseDecimal(",")).toBeUndefined();
    expect(parseDecimal("12a")).toBeUndefined();
  });

  it("shows the Russian comma, reports numbers and keeps half-typed text", () => {
    const onChange = vi.fn();
    function Total() {
      const [value, setValue] = useState<number | null>(4.5);
      return <DecimalInput label="Итог чека" value={value} onChange={(next) => { onChange(next); setValue(next); }} />;
    }
    render(<AppProviders locale="ru"><Total /></AppProviders>);
    const input = screen.getByLabelText("Итог чека");
    expect(input).toHaveValue("4,5");
    fireEvent.change(input, { target: { value: "7," } });
    expect(input).toHaveValue("7,");
    expect(onChange).toHaveBeenLastCalledWith(7);
    fireEvent.change(input, { target: { value: "" } });
    expect(onChange).toHaveBeenLastCalledWith(null);
  });
});

describe("DateField", () => {
  function Dates() {
    const [value, setValue] = useState("");
    return <DateField label="Дата с" value={value} onChange={setValue} />;
  }

  it("is a row that shows «Любая дата», picks a day in a sheet and resets it", () => {
    render(<AppProviders locale="ru"><Dates /></AppProviders>);
    expect(row("Дата с")).toHaveTextContent("Любая дата");
    setDate("Дата с", "2026-08-01");
    expect(row("Дата с")).toHaveTextContent("1 авг. 2026 г.");

    fireEvent.click(row("Дата с"));
    fireEvent.click(within(dialog("Дата с")).getByRole("button", { name: "Сбросить" }));
    expect(row("Дата с")).toHaveTextContent("Любая дата");
  });
});

describe("SelectRow", () => {
  function Brands() {
    const [value, setValue] = useState("");
    return (
      <SelectRow
        label="Бренд"
        valueText={value || "Любой бренд"}
        value={value}
        onChange={setValue}
        searchable
        freeText={(text) => ({ value: text, label: `Бренд содержит «${text}»` })}
        groups={[{ options: [{ value: "", label: "Любой бренд" }] }, { title: "Бренды", options: [{ value: "Łaciate", label: "Łaciate" }, { value: "Lavazza", label: "Lavazza" }] }]}
      />
    );
  }

  it("marks the current choice, filters ignoring Polish letters and offers the typed text", () => {
    render(<AppProviders locale="ru"><Brands /></AppProviders>);
    pick("Бренд", "Łaciate");
    expect(row("Бренд")).toHaveTextContent("Łaciate");

    fireEvent.click(row("Бренд"));
    const picker = dialog("Бренд");
    expect(within(picker).getByRole("button", { name: /^Łaciate/ })).toHaveAttribute("aria-current", "true");
    fireEvent.change(within(picker).getByLabelText("Поиск"), { target: { value: "laci" } });
    expect(within(picker).queryByRole("button", { name: /^Lavazza/ })).not.toBeInTheDocument();
    fireEvent.click(within(picker).getByRole("button", { name: "Бренд содержит «laci»" }));
    expect(row("Бренд")).toHaveTextContent("laci");
  });
});
