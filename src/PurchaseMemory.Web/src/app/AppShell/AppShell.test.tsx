import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../AppProviders";
import { AppShell, tabOf } from "./AppShell";

function Where() {
  return <output data-testid="where">{useLocation().pathname}</output>;
}

function renderShell(path = "/", reviewCount: number | null = null) {
  return render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[path]}>
        <AppShell reviewCount={reviewCount}><Where /></AppShell>
      </MemoryRouter>
    </AppProviders>
  );
}

describe("AppShell navigation", () => {
  it("keeps four sections in the tab bar; review is reached from Home", () => {
    renderShell();
    const tabs = screen.getByRole("navigation", { name: "Разделы" });
    expect(within(tabs).getAllByRole("link")).toHaveLength(4);
    for (const [name, href] of [["Главная", "/"], ["Покупки", "/purchases"], ["Товары", "/products"], ["Аналитика", "/analytics"]]) {
      expect(within(tabs).getByRole("link", { name })).toHaveAttribute("href", href);
    }
    expect(within(tabs).queryByRole("link", { name: /Нужно проверить/ })).not.toBeInTheDocument();
  });

  it("marks the current section, also on its detail screens", () => {
    renderShell("/purchases/p1");
    const tabs = screen.getByRole("navigation", { name: "Разделы" });
    expect(within(tabs).getByRole("link", { name: "Покупки" })).toHaveAttribute("aria-current", "page");
    expect(within(tabs).getByRole("link", { name: "Главная" })).not.toHaveAttribute("aria-current");
    expect(tabOf("/needs-attention")).toBe("home");
    expect(tabOf("/purchases/new")).toBe("home");
    expect(tabOf("/products/a")).toBe("products");
  });

  it("navigates inside the app and counts the review queue on Home", () => {
    renderShell("/", 3);
    const tabs = screen.getByRole("navigation", { name: "Разделы" });
    expect(within(tabs).getByRole("link", { name: /Главная.*3 пункта требуют внимания/ })).toBeInTheDocument();
    fireEvent.click(within(tabs).getByRole("link", { name: "Товары" }));
    expect(screen.getByTestId("where")).toHaveTextContent("/products");
  });
});
