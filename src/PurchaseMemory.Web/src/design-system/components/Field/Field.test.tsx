import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../../../app/AppProviders";
import { Field } from "./Field";

describe("Field", () => {
  it("connects error state to the accessible input", () => {
    render(<AppProviders><Field label="Store" error="Enter a store" /></AppProviders>);
    const input = screen.getByRole("textbox", { name: "Store" });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("Enter a store")).toBeInTheDocument();
  });

  it("keeps Mantine's success message off the native input attributes", () => {
    render(<AppProviders><Field label="Store" success="Saved" /></AppProviders>);
    const input = screen.getByRole("textbox", { name: "Store" });
    expect(input).not.toHaveAttribute("success");
    expect(screen.getByText("Saved")).toBeInTheDocument();
  });
});
