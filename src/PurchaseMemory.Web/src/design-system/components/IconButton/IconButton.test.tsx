import { fireEvent, render, screen } from "@testing-library/react";
import { Trash2 } from "lucide-react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../../app/AppProviders";
import { IconButton } from "./IconButton";

describe("IconButton", () => {
  it("is named by its label, keeps the icon out of the accessible name and fires once per click", () => {
    const onClick = vi.fn();
    render(<AppProviders locale="ru"><IconButton label="Удалить строку 2" icon={<Trash2 />} intent="danger" onClick={onClick} /></AppProviders>);

    const button = screen.getByRole("button", { name: "Удалить строку 2" });
    expect(button).toHaveAttribute("type", "button");
    expect(button).toHaveAttribute("data-intent", "danger");
    fireEvent.click(button);
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("renders the shorter visible text for wide screens without changing the accessible name", () => {
    render(<AppProviders locale="ru"><IconButton label="Переименовать: акция" text="Переименовать" iconOnly="mobile" icon={<Trash2 />} /></AppProviders>);

    const button = screen.getByRole("button", { name: "Переименовать: акция" });
    expect(button).toHaveTextContent("Переименовать");
    expect(button).toHaveAttribute("data-icon-only", "mobile");
  });

  it("is disabled and busy while loading", () => {
    render(<AppProviders locale="ru"><IconButton label="Удалить" icon={<Trash2 />} loading /></AppProviders>);

    const button = screen.getByRole("button", { name: "Удалить" });
    expect(button).toBeDisabled();
    expect(button).toHaveAttribute("aria-busy", "true");
  });
});
