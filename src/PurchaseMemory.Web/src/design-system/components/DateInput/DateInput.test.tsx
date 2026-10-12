import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../../../app/AppProviders";
import { DateInput } from "./DateInput";

function Labelled({ initial = "" }: { initial?: string }) {
  const [value, setValue] = useState(initial);
  return (
    <label>
      <span>Дата с</span>
      <DateInput value={value} onChange={(event) => setValue(event.target.value)} />
    </label>
  );
}

describe("DateInput", () => {
  it("is a native date field whose empty hint stays out of the label text", () => {
    render(<AppProviders locale="ru"><Labelled /></AppProviders>);
    const input = screen.getByLabelText("Дата с");
    expect(input).toHaveAttribute("type", "date");
    expect(input.parentElement).toHaveAttribute("data-empty-text", "Любая дата");

    fireEvent.change(input, { target: { value: "2026-08-01" } });
    expect(input).toHaveValue("2026-08-01");
    expect(input.parentElement).not.toHaveAttribute("data-empty-text");
  });

  it("supports date and time with its own empty text", () => {
    render(<AppProviders locale="ru"><label>Когда<DateInput type="datetime-local" value="" emptyText="Дата не указана" onChange={() => {}} /></label></AppProviders>);
    const input = screen.getByLabelText("Когда");
    expect(input).toHaveAttribute("type", "datetime-local");
    expect(input.parentElement).toHaveAttribute("data-empty-text", "Дата не указана");
  });

  it("translates the hint", () => {
    render(<AppProviders locale="en"><Labelled /></AppProviders>);
    expect(screen.getByLabelText("Дата с").parentElement).toHaveAttribute("data-empty-text", "Any date");
  });
});
