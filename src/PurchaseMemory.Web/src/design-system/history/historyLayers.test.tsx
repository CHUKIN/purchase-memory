import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { BrowserRouter, Route, Routes, useNavigate, useSearchParams } from "react-router";
import { beforeEach, describe, expect, it } from "vitest";
import { backFromLayer, HistoryLayerScope, leaveScreenWithLayers, onLayerEntry, settleReloadedLayerEntry, useHistoryLayer } from "./historyLayers";

/*
 * The mechanism on jsdom's real window history: entries are pushed and
 * stepped back over, and popstate arrives asynchronously, as in a browser.
 */

const depth = () => (window.history.state as { pmLayer?: number } | null)?.pmLayer ?? 0;

function Layer({ name, initiallyOpen = false, onBack, children }: {
  name: string;
  initiallyOpen?: boolean;
  onBack?: (close: () => void) => boolean | void | Promise<boolean | void>;
  children?: ReactNode;
}) {
  const [open, setOpen] = useState(initiallyOpen);
  const layer = useHistoryLayer(open, () => onBack ? onBack(() => setOpen(false)) : setOpen(false));
  return (
    <div>
      <button type="button" onClick={() => setOpen(true)}>{`open ${name}`}</button>
      {open && (
        <section aria-label={name}>
          <button type="button" onClick={() => setOpen(false)}>{`close ${name}`}</button>
          <HistoryLayerScope value={layer}>{children}</HistoryLayerScope>
        </section>
      )}
    </div>
  );
}

const shown = (name: string) => screen.queryByRole("region", { name });

describe("history layers", () => {
  beforeEach(() => {
    window.history.replaceState({ idx: 0 }, "", "/screen");
  });

  it("an open layer is one history step: browser back closes it and stays on the screen", async () => {
    render(<Layer name="sheet" />);
    const before = window.history.length;
    fireEvent.click(screen.getByRole("button", { name: "open sheet" }));
    expect(shown("sheet")).toBeInTheDocument();
    expect(window.history.length).toBe(before + 1);
    expect(depth()).toBe(1);
    expect(window.history.state).toMatchObject({ idx: 1 });
    expect(onLayerEntry()).toBe(true);

    act(() => window.history.back());
    await waitFor(() => expect(shown("sheet")).not.toBeInTheDocument());
    expect(depth()).toBe(0);
    expect(window.location.pathname).toBe("/screen");
  });

  it("closing it on screen steps back over its entry, so no dead step is left", async () => {
    render(<Layer name="sheet" />);
    fireEvent.click(screen.getByRole("button", { name: "open sheet" }));
    expect(depth()).toBe(1);
    fireEvent.click(screen.getByRole("button", { name: "close sheet" }));
    await waitFor(() => expect(depth()).toBe(0));
    expect(window.history.state).toMatchObject({ idx: 0 });
  });

  it("the back chevron closes the top layer only, one step at a time", async () => {
    render(<Layer name="editor"><Layer name="line" /></Layer>);
    fireEvent.click(screen.getByRole("button", { name: "open editor" }));
    fireEvent.click(screen.getByRole("button", { name: "open line" }));
    expect(depth()).toBe(2);

    act(() => backFromLayer(() => { throw new Error("a layer is open"); }));
    await waitFor(() => expect(shown("line")).not.toBeInTheDocument());
    expect(shown("editor")).toBeInTheDocument();
    expect(depth()).toBe(1);

    act(() => backFromLayer(() => { throw new Error("a layer is open"); }));
    await waitFor(() => expect(shown("editor")).not.toBeInTheDocument());
    expect(depth()).toBe(0);
    let left = false;
    backFromLayer(() => { left = true; });
    expect(left).toBe(true);
  });

  it("layers opened together keep their nesting: back closes the inner one first", async () => {
    // A child's effect runs before its parent's, so the inner layer registers first.
    render(<Layer name="sheet" initiallyOpen><Layer name="picker" initiallyOpen /></Layer>);
    await waitFor(() => expect(depth()).toBe(2));

    act(() => window.history.back());
    await waitFor(() => expect(shown("picker")).not.toBeInTheDocument());
    expect(shown("sheet")).toBeInTheDocument();
  });

  it("a layer that refuses to close (a failed save) stays open and gets its step back", async () => {
    let allow = false;
    render(<Layer name="review" onBack={async (close) => { if (!allow) return false; close(); }} />);
    fireEvent.click(screen.getByRole("button", { name: "open review" }));

    act(() => window.history.back());
    await waitFor(() => expect(depth()).toBe(1));
    expect(shown("review")).toBeInTheDocument();

    allow = true;
    act(() => window.history.back());
    await waitFor(() => expect(shown("review")).not.toBeInTheDocument());
    await waitFor(() => expect(depth()).toBe(0));
  });

  it("while a slow close is under way the layer keeps its step: a second back does not leave the screen", async () => {
    let finish: (() => void) | null = null;
    render(<Layer name="review" onBack={(close) => new Promise<void>((resolve) => { finish = () => { close(); resolve(); }; })} />);
    fireEvent.click(screen.getByRole("button", { name: "open review" }));
    await waitFor(() => expect(depth()).toBe(1));

    act(() => window.history.back());
    await waitFor(() => expect(finish).not.toBeNull());
    // The step came back at once, while the save is still on its way.
    await waitFor(() => expect(depth()).toBe(1));
    act(() => window.history.back());
    await waitFor(() => expect(depth()).toBe(1));
    expect(window.location.pathname).toBe("/screen");
    expect(shown("review")).toBeInTheDocument();

    act(() => finish!());
    await waitFor(() => expect(shown("review")).not.toBeInTheDocument());
    await waitFor(() => expect(depth()).toBe(0));
    expect(window.location.pathname).toBe("/screen");
  });

  it("an entry left behind by a reload is stepped back over", async () => {
    window.history.pushState({ idx: 1, pmLayer: 1 }, "", "/screen");
    settleReloadedLayerEntry();
    await waitFor(() => expect(depth()).toBe(0));
    expect(window.history.state).toMatchObject({ idx: 0 });
  });

  it("arriving back on the entry of a layer that is gone shows the screen under it", async () => {
    // The screen was left with its layer open (a link inside the sheet), then «назад».
    window.history.pushState({ idx: 1, pmLayer: 1 }, "", "/screen");
    window.history.pushState({ idx: 2 }, "", "/other");
    render(<Layer name="sheet" />);
    fireEvent.click(screen.getByRole("button", { name: "open sheet" }));
    fireEvent.click(screen.getByRole("button", { name: "close sheet" }));
    await waitFor(() => expect(window.history.state).toMatchObject({ idx: 2 }));

    act(() => window.history.back());
    await waitFor(() => expect(window.history.state).toMatchObject({ idx: 0 }));
    expect(window.location.pathname).toBe("/screen");
  });

  it("leaving the whole screen drops its layers without asking them to close", async () => {
    window.history.replaceState({ idx: 0 }, "", "/before");
    window.history.pushState({ idx: 1 }, "", "/screen");
    let asked = 0;
    render(<Layer name="result" onBack={(close) => { asked += 1; close(); }} />);
    fireEvent.click(screen.getByRole("button", { name: "open result" }));
    await waitFor(() => expect(depth()).toBe(1));

    leaveScreenWithLayers();
    act(() => window.history.go(-2));
    await waitFor(() => expect(window.location.pathname).toBe("/before"));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(asked).toBe(0);
    let left = false;
    backFromLayer(() => { left = true; });
    expect(left).toBe(true);
  });

  it("the chevron right after a layer opened, while an earlier step is still on its way, closes that layer", async () => {
    let screenLeft = false;
    render(<><Layer name="first" /><Layer name="second" /></>);
    fireEvent.click(screen.getByRole("button", { name: "open first" }));
    await waitFor(() => expect(depth()).toBe(1));
    // Closing the first starts a step back; the second opens before it lands.
    fireEvent.click(screen.getByRole("button", { name: "close first" }));
    fireEvent.click(screen.getByRole("button", { name: "open second" }));
    act(() => backFromLayer(() => { screenLeft = true; }));

    await waitFor(() => expect(shown("second")).not.toBeInTheDocument());
    await waitFor(() => expect(depth()).toBe(0));
    expect(screenLeft).toBe(false);
  });

  it("a layer's address is carried only onto its own screen entry, never onto a later one", async () => {
    // Edit view with a line sheet → a link to another screen → back to the
    // purchase by a link → «Исправить» → «Отмена» (one step back): the view
    // must not take the old editor address.
    function Purchase() {
      const [params, setParams] = useSearchParams();
      const navigate = useNavigate();
      const [open, setOpen] = useState(false);
      useHistoryLayer(open, () => setOpen(false));
      if (params.get("edit") !== "1") return <div><h1>view</h1><button type="button" onClick={() => setParams({ edit: "1" })}>edit</button></div>;
      return (
        <div>
          <h1>editor</h1>
          <button type="button" onClick={() => setOpen(true)}>line</button>
          <button type="button" onClick={() => navigate(-1)}>cancel</button>
          {open && <button type="button" onClick={() => { setOpen(false); navigate("/product"); }}>product</button>}
        </div>
      );
    }
    function Product() {
      const navigate = useNavigate();
      return <div><h1>product</h1><button type="button" onClick={() => navigate("/purchase")}>to purchase</button></div>;
    }
    window.history.replaceState(null, "", "/purchase?edit=1");
    render(<BrowserRouter><Routes><Route path="/purchase" element={<Purchase />} /><Route path="/product" element={<Product />} /></Routes></BrowserRouter>);
    fireEvent.click(screen.getByRole("button", { name: "line" }));
    await waitFor(() => expect(depth()).toBe(1));
    fireEvent.click(screen.getByRole("button", { name: "product" }));
    fireEvent.click(await screen.findByRole("button", { name: "to purchase" }));
    fireEvent.click(await screen.findByRole("button", { name: "edit" }));
    fireEvent.click(await screen.findByRole("button", { name: "cancel" }));

    expect(await screen.findByRole("heading", { name: "view" })).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(screen.getByRole("heading", { name: "view" })).toBeInTheDocument();
    expect(`${window.location.pathname}${window.location.search}`).toBe("/purchase");
  });
});
