import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { recentPurchases } from "../mockData";
import { PurchaseCard } from "./PurchaseCard";

describe("PurchaseCard", () => {
  it("labels attention state and expands item details", () => {
    render(<AppProviders locale="en"><PurchaseCard purchase={recentPurchases[1]} /></AppProviders>);
    expect(screen.getByText("Needs attention")).toBeInTheDocument();
    const toggle = screen.getByRole("button", { name: "Show items" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(toggle);
    expect(screen.getByText("Kawa ziarnista Bellarom")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Hide items" })).toHaveAttribute("aria-expanded", "true");
  });
});
