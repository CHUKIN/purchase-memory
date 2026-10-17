import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, describe, expect, it } from "vitest";
import { ItemLink, Page } from "../design-system/components";
import { AppProviders } from "./AppProviders";
import { backLabelKey, readReturnState, useBackTarget, useReturnState } from "./backNavigation";

function ProductScreen() {
  const back = useBackTarget({ to: "/products", labelKey: "common.products" });
  return <Page title="Kawa" back={back}><p>product</p></Page>;
}

function Where() {
  const location = useLocation();
  return <p data-testid="where">{`${location.pathname}${location.search}`}</p>;
}

function Origin() {
  const state = useReturnState();
  return <ItemLink to="/products/p1" state={state}>Kawa</ItemLink>;
}

type Entry = string | { pathname: string; search?: string; state?: unknown };

function renderAt(entries: Entry[], initialIndex = entries.length - 1) {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={entries} initialIndex={initialIndex}>
        <Routes>
          <Route path="/products/:productId" element={<ProductScreen />} />
          <Route path="*" element={<><Origin /><Where /></>} />
        </Routes>
      </MemoryRouter>
    </AppProviders>
  );
}

const backLink = () => screen.getByRole("link", { name: /Товары|Нужно проверить|Главная|Покупки|Назад/ });

afterEach(() => window.history.replaceState(null, ""));

describe("back navigation", () => {
  it("names and opens the screen that opened this one", async () => {
    renderAt(["/needs-attention?tag=kid"]);
    fireEvent.click(screen.getByRole("link", { name: "Kawa" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Kawa" })).toBeInTheDocument();
    expect(backLink()).toHaveAccessibleName("Нужно проверить");
    fireEvent.click(backLink());
    expect(screen.getByTestId("where")).toHaveTextContent("/needs-attention?tag=kid");
  });

  it("uses the fixed parent screen for a deep link or reload without an origin", async () => {
    renderAt(["/products/p1"]);

    expect(await screen.findByRole("heading", { level: 1, name: "Kawa" })).toBeInTheDocument();
    expect(backLink()).toHaveAccessibleName("Товары");
    fireEvent.click(backLink());
    expect(screen.getByTestId("where")).toHaveTextContent(/^\/products$/);
  });

  it("does not claim a parent screen when history would lead elsewhere", async () => {
    // Opened from Home, but without an origin in state: the label says «Товары» and the tap goes there, not back to Home.
    renderAt(["/", "/products/p1"]);

    expect(await screen.findByRole("heading", { level: 1, name: "Kawa" })).toBeInTheDocument();
    fireEvent.click(backLink());
    expect(screen.getByTestId("where")).toHaveTextContent(/^\/products$/);
  });

  it("says «Назад» when the origin is another detail screen", async () => {
    renderAt([{ pathname: "/products/p1", state: { returnTo: "/purchases/x1" } }]);

    expect(await screen.findByRole("heading", { level: 1, name: "Kawa" })).toBeInTheDocument();
    expect(backLink()).toHaveAccessibleName("Назад");
    fireEvent.click(backLink());
    expect(screen.getByTestId("where")).toHaveTextContent("/purchases/x1");
  });

  it("steps back through history to the origin entry when its position is known", async () => {
    // The origin is two entries back (the detail screen pushed one more entry in between).
    window.history.replaceState({ idx: 4 }, "");
    renderAt([
      "/needs-attention?tag=kid",
      "/products/p1",
      { pathname: "/products/p1", search: "?x=1", state: { returnTo: "/needs-attention?tag=kid", returnIndex: 2 } }
    ]);

    expect(await screen.findByRole("heading", { level: 1, name: "Kawa" })).toBeInTheDocument();
    expect(backLink()).toHaveAccessibleName("Нужно проверить");
    fireEvent.click(backLink());
    expect(screen.getByTestId("where")).toHaveTextContent("/needs-attention?tag=kid");
  });

  it("ignores an origin that is not an in-app path", async () => {
    renderAt([{ pathname: "/products/p1", state: { returnTo: "//example.com/needs-attention" } }]);

    expect(await screen.findByRole("heading", { level: 1, name: "Kawa" })).toBeInTheDocument();
    expect(backLink()).toHaveAccessibleName("Товары");
  });
});

describe("readReturnState", () => {
  it("accepts in-app paths only", () => {
    expect(readReturnState({ returnTo: "/purchases?q=milk", returnIndex: 3 })).toEqual({ returnTo: "/purchases?q=milk", returnIndex: 3 });
    expect(readReturnState({ returnTo: "/purchases", returnIndex: -1 })).toEqual({ returnTo: "/purchases" });
    expect(readReturnState({ returnTo: "https://example.com" })).toBeNull();
    expect(readReturnState({ returnTo: "/\\example.com" })).toBeNull();
    expect(readReturnState(null)).toBeNull();
  });

  it("maps list screens to their names and anything else to «Назад»", () => {
    expect(backLabelKey("/")).toBe("common.home");
    expect(backLabelKey("/purchases?tag=kid")).toBe("common.purchases");
    expect(backLabelKey("/needs-attention")).toBe("common.review");
    expect(backLabelKey("/receipts/r1")).toBe("ui.back");
    expect(backLabelKey("/help#products")).toBe("help.ui.title");
  });
});
