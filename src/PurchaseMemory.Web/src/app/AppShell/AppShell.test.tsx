import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../AppProviders";
import { AppShell } from "./AppShell";

function renderShell(path = "/") {
  return render(
    <AppProviders locale="ru">
      <MemoryRouter initialEntries={[path]}>
        <AppShell recognition={null}><h1>Content</h1></AppShell>
      </MemoryRouter>
    </AppProviders>
  );
}

describe("AppShell navigation", () => {
  it("announces a neutral checking state while recognition status is pending", () => {
    renderShell();
    expect(screen.getAllByText("Проверяем распознавание")).toHaveLength(2);
    expect(screen.queryByText("Распознавание недоступно")).not.toBeInTheDocument();
  });

  it("keeps four compact mobile destinations and reaches review from Home", () => {
    renderShell();
    const mobile = screen.getByRole("navigation", { name: "Основная навигация для телефона" });
    expect(within(mobile).getAllByRole("link")).toHaveLength(4);
    for (const name of ["Главная", "Покупки", "Товары", "Аналитика"]) {
      expect(within(mobile).getByRole("link", { name })).toBeInTheDocument();
    }
    expect(within(mobile).queryByRole("link", { name: "Нужно проверить" })).not.toBeInTheDocument();
  });

  it("marks the current section active in both navigation layouts", () => {
    renderShell("/purchases");
    const desktop = screen.getByRole("navigation", { name: "Основная навигация для компьютера" });
    const mobile = screen.getByRole("navigation", { name: "Основная навигация для телефона" });
    expect(within(desktop).getByRole("link", { name: "Покупки" })).toHaveAttribute("aria-current", "page");
    expect(within(mobile).getByRole("link", { name: "Покупки" })).toHaveAttribute("aria-current", "page");
    expect(within(desktop).getByRole("link", { name: "Главная" })).not.toHaveAttribute("aria-current");
  });
});
