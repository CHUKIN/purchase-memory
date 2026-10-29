import { act, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ItemLink, Page } from "../design-system/components";
import { AppProviders } from "./AppProviders";
import { useBackTarget, useReturnState } from "./backNavigation";
import { arrivalPlace, clearPlaces, loadPlace, readPlaceState, restoreScroll, savePlace } from "./place";
import { PlaceMemory } from "./PlaceMemory";

/** A stand-in for Ionic's `ion-content`: a scroller inside a shadow root, under `div.ion-page`. */
function installContent(initial: { scrollTop: number; scrollHeight: number; clientHeight: number }) {
  document.querySelector("div.ion-page")?.remove();
  const page = document.createElement("div");
  page.className = "ion-page";
  const content = document.createElement("ion-content");
  const scroller = document.createElement("div");
  scroller.className = "inner-scroll";
  content.attachShadow({ mode: "open" }).append(scroller);
  const scrollToPoint = vi.fn(async (_x: number, y: number) => { scroller.scrollTop = y; });
  Object.assign(content, { scrollToPoint });
  const metrics = { ...initial };
  Object.defineProperty(scroller, "scrollHeight", { get: () => metrics.scrollHeight });
  Object.defineProperty(scroller, "clientHeight", { get: () => metrics.clientHeight });
  scroller.scrollTop = metrics.scrollTop;
  page.append(content);
  document.body.append(page);
  return { scroller, scrollToPoint, metrics };
}

afterEach(() => {
  document.querySelector("div.ion-page")?.remove();
  vi.useRealTimers();
  window.history.replaceState(null, "");
});

describe("restoreScroll", () => {
  beforeEach(() => vi.useFakeTimers());

  it("scrolls at once when the content is already long enough", () => {
    const { scrollToPoint } = installContent({ scrollTop: 0, scrollHeight: 2000, clientHeight: 600 });
    restoreScroll(740);
    expect(scrollToPoint).toHaveBeenCalledWith(0, 740, 0);
  });

  it("waits for a list that loads after the screen appears", () => {
    const { scrollToPoint, metrics } = installContent({ scrollTop: 0, scrollHeight: 600, clientHeight: 600 });
    restoreScroll(740);
    vi.advanceTimersByTime(400);
    expect(scrollToPoint).not.toHaveBeenCalled();
    metrics.scrollHeight = 2000;
    vi.advanceTimersByTime(100);
    expect(scrollToPoint).toHaveBeenCalledWith(0, 740, 0);
  });

  it("gives up after a while, and lets go as soon as the person touches the screen", () => {
    const first = installContent({ scrollTop: 0, scrollHeight: 600, clientHeight: 600 });
    restoreScroll(740);
    vi.advanceTimersByTime(5000);
    first.metrics.scrollHeight = 2000;
    vi.advanceTimersByTime(500);
    expect(first.scrollToPoint).not.toHaveBeenCalled();

    const second = installContent({ scrollTop: 0, scrollHeight: 600, clientHeight: 600 });
    restoreScroll(740);
    fireEvent.pointerDown(document.body);
    second.metrics.scrollHeight = 2000;
    vi.advanceTimersByTime(500);
    expect(second.scrollToPoint).not.toHaveBeenCalled();
  });
});

describe("remembered places", () => {
  it("keeps the newest, reads router state defensively", () => {
    clearPlaces();
    savePlace("k1", "/p", { scroll: 10 });
    savePlace("k1", "/p", { line: 3 }, true);
    expect(loadPlace("k1", "/p")).toEqual({ scroll: 10, line: 3 });
    savePlace("k1", "/p", { scroll: 20 });
    expect(loadPlace("k1", "/p")).toEqual({ scroll: 20 });
    expect(readPlaceState({ place: { line: 2, scroll: 120 } })).toEqual({ line: 2, scroll: 120 });
    expect(readPlaceState({ place: { line: -1, scroll: 0 } })).toBeNull();
    expect(readPlaceState({ place: "x" })).toBeNull();
    expect(readPlaceState(null)).toBeNull();
  });
});

function Detail() {
  const back = useBackTarget({ to: "/needs-attention", labelKey: "common.review" });
  return <Page title="Detail" back={back}><p>detail</p></Page>;
}

function Listing() {
  const state = useReturnState();
  return <ItemLink to="/products/p1" state={state}>Open Kawa</ItemLink>;
}

function Where() {
  const location = useLocation();
  return <p data-testid="where">{`${location.pathname}${location.search}`}</p>;
}

describe("back to the exact scroll position (fallback path)", () => {
  beforeEach(() => clearPlaces());

  it("opening a screen notes the list's scroll; «Назад» by address restores it", async () => {
    const content = installContent({ scrollTop: 0, scrollHeight: 3000, clientHeight: 600 });
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={["/needs-attention?tag=kid"]}>
          <PlaceMemory />
          <Where />
          <Routes>
            <Route path="/products/:productId" element={<Detail />} />
            <Route path="*" element={<Listing />} />
          </Routes>
        </MemoryRouter>
      </AppProviders>
    );

    // The person scrolled the list, then tapped a row.
    content.scroller.scrollTop = 840;
    content.scrollToPoint.mockClear();
    fireEvent.click(screen.getByRole("link", { name: "Open Kawa" }));
    expect(await screen.findByRole("heading", { level: 1, name: "Detail" })).toBeInTheDocument();
    expect(screen.getByTestId("where")).toHaveTextContent("/products/p1");

    // Back (no browser history positions here): opens the address with the place in state.
    content.scroller.scrollTop = 0;
    fireEvent.click(screen.getByRole("link", { name: "Ревью" }));
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByTestId("where")).toHaveTextContent("/needs-attention?tag=kid");
    expect(content.scrollToPoint).toHaveBeenCalledWith(0, 840, 0);
  });

  it("restores a place given in router state once, on entering the screen", () => {
    const content = installContent({ scrollTop: 0, scrollHeight: 3000, clientHeight: 600 });
    render(
      <AppProviders locale="ru">
        <MemoryRouter initialEntries={[{ pathname: "/needs-attention", state: { place: { scroll: 500 } } }]}>
          <PlaceMemory />
          <Routes><Route path="*" element={<Listing />} /></Routes>
        </MemoryRouter>
      </AppProviders>
    );
    expect(content.scrollToPoint).toHaveBeenCalledTimes(1);
  });
});

describe("places across a reload", () => {
  beforeEach(() => clearPlaces());

  it("survive a reload of the page (sessionStorage), and work without storage", async () => {
    savePlace("entry-1", "/p", { scroll: 640, line: 4 });
    vi.resetModules();
    const reloaded = await import("./place");
    expect(reloaded.loadPlace("entry-1", "/p")).toEqual({ scroll: 640, line: 4 });

    const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("quota"); });
    const getItem = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("denied"); });
    try {
      expect(() => savePlace("entry-2", "/p", { scroll: 5 })).not.toThrow();
      expect(loadPlace("entry-2", "/p")).toEqual({ scroll: 5 });
      vi.resetModules();
      const withoutStorage = await import("./place");
      expect(withoutStorage.loadPlace("entry-1", "/p")).toBeNull();
    } finally {
      setItem.mockRestore();
      getItem.mockRestore();
    }
  });
});

describe("a place in router state is only the first word", () => {
  beforeEach(() => clearPlaces());

  it("is copied into the store on arrival; a later tap's place wins over it", () => {
    const state = { place: { line: 3, scroll: 300 } };
    expect(arrivalPlace("k", "/p", state)).toEqual({ line: 3, scroll: 300 });
    expect(loadPlace("k", "/p")).toEqual({ line: 3, scroll: 300 });

    // The person closed the review, scrolled to the top and tapped something: PlaceMemory notes that.
    savePlace("k", "/p", { scroll: 0 });
    expect(arrivalPlace("k", "/p", state)).toEqual({ scroll: 0 });
  });

  it("does not reopen the line again after a reload of an entry whose line was dropped", async () => {
    const state = { place: { line: 3, scroll: 300 } };
    arrivalPlace("k", "/p", state);
    savePlace("k", "/p", { scroll: 120 });
    vi.resetModules();
    const reloaded = await import("./place");
    expect(reloaded.arrivalPlace("k", "/p", state)).toEqual({ scroll: 120 });
  });
});

describe("the «default» key of every page load", () => {
  beforeEach(() => clearPlaces());

  it("never lets a place left on one address show up on another loaded later in the tab", async () => {
    // Load of purchase A: review at line 3, then «Открыть товар» (saved under the entry's «default» key).
    savePlace("default", "/purchases/A", { scroll: 400 });
    savePlace("default", "/purchases/A", { line: 3 }, true);
    // A fresh load of purchase B starts with the same key «default», in the same sessionStorage.
    vi.resetModules();
    const fresh = await import("./place");
    expect(fresh.loadPlace("default", "/purchases/B")).toBeNull();
    expect(fresh.arrivalPlace("default", "/purchases/B", null)).toBeNull();
    // The right address still finds its own place.
    expect(fresh.loadPlace("default", "/purchases/A")).toEqual({ scroll: 400, line: 3 });
  });
});
