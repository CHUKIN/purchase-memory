import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { checklistGroups, checklistItemIds } from "./checklistData";
import { testChecklistStorageKey } from "./checklistStorage";
import { TestChecklistPage } from "./TestChecklistPage";

function renderPage() {
  return render(<AppProviders><MemoryRouter><TestChecklistPage /></MemoryRouter></AppProviders>);
}

const total = checklistItemIds.length;

describe("TestChecklistPage", () => {
  beforeEach(() => window.localStorage.clear());

  it("shows every group and item with no ticks at first", () => {
    renderPage();
    expect(screen.getByRole("heading", { level: 1, name: "Что протестировать" })).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(checklistGroups.length);
    expect(screen.getAllByRole("checkbox")).toHaveLength(total);
    expect(screen.getByText("Штрихкоды и фото товаров")).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: /Неизвестный штрихкод/ })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 0 из ${total}`);
  });

  it("counts ticks, keeps them in localStorage and restores them", () => {
    const { unmount } = renderPage();
    fireEvent.click(screen.getByRole("checkbox", { name: /Суммы и скидки/ }));
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 1 из ${total}`);
    expect(JSON.parse(window.localStorage.getItem(testChecklistStorageKey)!)).toEqual(["totalsDiscounts"]);

    unmount();
    renderPage();
    expect(screen.getByRole("checkbox", { name: /Суммы и скидки/ })).toBeChecked();
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 1 из ${total}`);

    fireEvent.click(screen.getByRole("checkbox", { name: /Суммы и скидки/ }));
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 0 из ${total}`);
  });

  it("clears every tick with the reset button", () => {
    window.localStorage.setItem(testChecklistStorageKey, JSON.stringify(["scanHome", "backButton"]));
    renderPage();
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 2 из ${total}`);
    fireEvent.click(screen.getByRole("button", { name: "Сбросить отметки" }));
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 0 из ${total}`);
    expect(screen.getAllByRole("checkbox").some((box) => (box as HTMLInputElement).checked)).toBe(false);
    expect(JSON.parse(window.localStorage.getItem(testChecklistStorageKey)!)).toEqual([]);
  });

  it("works when storage is unavailable", () => {
    const get = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    const set = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked"); });
    renderPage();
    fireEvent.click(screen.getByRole("checkbox", { name: /Кнопки под палец/ }));
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 1 из ${total}`);
    get.mockRestore();
    set.mockRestore();
  });

  it("ignores garbage in storage", () => {
    window.localStorage.setItem(testChecklistStorageKey, "{not json");
    renderPage();
    expect(screen.getByRole("status")).toHaveTextContent(`Отмечено 0 из ${total}`);
  });
});
