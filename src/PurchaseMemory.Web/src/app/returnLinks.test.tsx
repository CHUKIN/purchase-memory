import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { describe, expect, it } from "vitest";
import { ItemLink, useBack } from "../design-system/components";
import { HelpPage } from "../features/help/HelpPage";
import { useBackTarget, useReturnState } from "./backNavigation";
import { clearPlaces, savePlace } from "./place";
import { AppProviders } from "./AppProviders";
import { ToastNavigationBridge, useToast } from "./toasts";

function Arrived() {
  const location = useLocation();
  return (
    <>
      <p data-testid="where">{`${location.pathname}${location.search}`}</p>
      <p data-testid="state">{JSON.stringify(location.state)}</p>
    </>
  );
}

describe("help topic links", () => {
  it("open their screen with the help page as the way back", () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/help"]}>
          <Routes>
            <Route path="/help" element={<HelpPage />} />
            <Route path="*" element={<Arrived />} />
          </Routes>
        </MemoryRouter>
      </AppProviders>
    );

    const link = screen.getAllByRole("link").find((element) => element.getAttribute("href") === "/needs-attention");
    expect(link).toBeDefined();
    fireEvent.click(link!);
    expect(screen.getByTestId("where")).toHaveTextContent("/needs-attention");
    expect(JSON.parse(screen.getByTestId("state").textContent ?? "null")).toMatchObject({ returnTo: "/help" });
  });
});

function Notifier() {
  const showToast = useToast();
  return (
    <button type="button" onClick={() => showToast({ id: "t1", message: "Чек готов", action: { label: "Открыть чек", to: "/receipts/r1" } })}>
      notify
    </button>
  );
}

describe("toast action", () => {
  it("opens the receipt with the screen it was tapped on as the way back", () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/needs-attention?tag=kid"]}>
          <ToastNavigationBridge />
          <Routes>
            <Route path="/receipts/:receiptId" element={<Arrived />} />
            <Route path="*" element={<Notifier />} />
          </Routes>
        </MemoryRouter>
      </AppProviders>
    );

    fireEvent.click(screen.getByRole("button", { name: "notify" }));
    fireEvent.click(screen.getByRole("button", { name: "Открыть чек" }));
    expect(screen.getByTestId("where")).toHaveTextContent("/receipts/r1");
    expect(JSON.parse(screen.getByTestId("state").textContent ?? "null")).toMatchObject({ returnTo: "/needs-attention?tag=kid" });
  });
});

function Origin() {
  const state = useReturnState();
  return <ItemLink to="/scan" state={state}>open</ItemLink>;
}

function DoneScreen() {
  const goBack = useBack(useBackTarget({ to: "/", labelKey: "common.home" }));
  return <button type="button" onClick={goBack}>Готово</button>;
}

describe("a «Готово» button that goes back like the chevron", () => {
  it("opens the origin by address with its remembered place in state", () => {
    clearPlaces();
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/purchases?tag=kid"]}>
          <Routes>
            <Route path="/scan" element={<DoneScreen />} />
            <Route path="*" element={<><Origin /><Arrived /></>} />
          </Routes>
        </MemoryRouter>
      </AppProviders>
    );
    // The place noted for the origin entry when the link was tapped (the entry key is «default» here).
    savePlace("default", "/purchases?tag=kid", { scroll: 640 });
    fireEvent.click(screen.getByRole("link", { name: "open" }));
    fireEvent.click(screen.getByRole("button", { name: "Готово" }));

    expect(screen.getByTestId("where")).toHaveTextContent("/purchases?tag=kid");
    expect(JSON.parse(screen.getByTestId("state").textContent ?? "null")).toEqual({ place: { scroll: 640 } });
  });
});
