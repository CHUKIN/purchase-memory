import { createContext, useContext, useEffect, useRef } from "react";

/*
 * «Назад» goes back exactly one step, and an opened sheet, modal or inner
 * screen (a receipt's full editor) is a step of its own. Such a layer is
 * plain React state, so while it is open it gets a browser history entry of
 * its own: the same address and router state as the screen under it, marked
 * with its depth (1 for the first layer over the screen, 2 for one opened on
 * top of it, …). Leaving that entry (the browser's or iPhone's back, the
 * header chevron) closes the layer; closing the layer any other way (✕,
 * «Готово», a swipe down, saving) steps back over one entry, so no dead step
 * is left behind.
 *
 * Entries are marked by depth, not by layer, because they are
 * interchangeable: only how many there are matters. The layers themselves
 * are kept in nesting order (a picker opened inside a sheet is above it),
 * whatever order their effects ran in.
 *
 * The entry copies React Router's own state (`usr`, `key`, `idx` + 1), so the
 * router sees the same location and keeps numbering history entries in order
 * (`browserHistoryIndex`, `useBack`). History steps are asynchronous: while
 * one this module started is under way, later opens and closes wait for it.
 *
 * An entry deeper than the layers now open (the screen was left with a layer
 * open, or the page was reloaded) only shows the screen under it, so arriving
 * on one steps back over it. Always back: the direction of a browser step is
 * not reported, and stepping back is right for «назад», by far the common way
 * to arrive there (a browser «вперёд» onto such an entry is undone).
 *
 * A screen may change its own address while a layer is open: filters chosen
 * in a filter sheet replace the entry (`replace: true`). React Router then
 * writes its own state, so `history.replaceState` is wrapped to keep the
 * layer's mark on the entry. When such a layer is left, the address it ended
 * on is carried down to the entry now shown (and the router is told with a
 * popstate), so closing the sheet, by «Готово» or by «назад», keeps the
 * filters and leaves no extra step.
 */

const depthKey = "pmLayer";
/** A popstate normally arrives within a frame; a step that never reports back must not block the queue. */
const travelTimeoutMs = 1000;

type BackResult = boolean | void | Promise<boolean | void>;

/** Stands for one hook instance; children name it as their parent layer. */
export interface HistoryLayerHandle {
  readonly parent: HistoryLayerHandle | null;
}

interface Layer {
  handle: HistoryLayerHandle;
  /** Closes the layer after its entry was left; `false` keeps it open (its entry comes back). */
  onBack: () => BackResult;
}

/** Open layers that have an entry, bottom to top. */
const stack: Layer[] = [];
/** Layers whose owner still shows them. */
const live = new Set<Layer>();
const queue: (() => void)[] = [];
let travelling = false;
let travelTimer: number | undefined;
let listening = false;

interface Entry {
  url: string;
  state: Record<string, unknown>;
  /** Router key of the entry this layer entry was pushed over: only that entry takes its address. */
  baseKey: unknown;
}

/** The address and state of each layer entry, by depth, as last written. */
const entries: (Entry | undefined)[] = [];
/** The depth the browser showed when this module last looked. */
let shownDepth = 0;
/** A popstate this module dispatches itself, to tell the router about a carried address. */
let carrying = false;
/** The next step leaves the screen with its layers: they go without their `onBack`. */
let leavingScreen = false;
let nativeReplaceState: History["replaceState"] | null = null;
let nativePushState: History["pushState"] | null = null;

function stateObject(): Record<string, unknown> {
  const state: unknown = window.history.state;
  return typeof state === "object" && state !== null ? state as Record<string, unknown> : {};
}

function entryDepth(): number {
  const depth = stateObject()[depthKey];
  return typeof depth === "number" && Number.isInteger(depth) && depth > 0 ? depth : 0;
}

function entryIndex(): number | null {
  const index = stateObject().idx;
  return typeof index === "number" ? index : null;
}

function snapshot(baseKey: unknown): Entry {
  return { url: window.location.href, state: stateObject(), baseKey };
}

/** Forgets what was noted about layer entries: the screen they belong to has been left. */
function forgetEntries() {
  entries.length = 0;
  shownDepth = 0;
}

function replaceEntry(state: Record<string, unknown>, url: string) {
  (nativeReplaceState ?? window.history.replaceState.bind(window.history))(state, "", url);
}

function listen() {
  if (listening || typeof window === "undefined") return;
  listening = true;
  window.addEventListener("popstate", onPopState);
  // The router replaces the entry with state of its own: the layer's mark stays on it.
  // During a step back this module started, the entry is still the layer's
  // (its layer just closed): the new address is kept to be carried down too.
  const browserHistory = window.history;
  const native = browserHistory.replaceState.bind(browserHistory);
  nativeReplaceState = native;
  browserHistory.replaceState = (data: unknown, unused: string, url?: string | URL | null) => {
    const depth = entryDepth();
    // A replace onto another screen (saving, then on to the list) is leaving, not a change on this one.
    const sameScreen = url === undefined || url === null || new URL(String(url), window.location.href).pathname === window.location.pathname;
    if (!sameScreen) {
      native(data, unused, url);
      forgetEntries();
      return;
    }
    const layerEntry = depth > 0 && (depth <= stack.length || travelling);
    const marked = layerEntry && typeof data === "object" && data !== null && !(depthKey in data);
    native(marked ? { ...(data as Record<string, unknown>), [depthKey]: depth } : data, unused, url);
    if (layerEntry && entries[depth]) entries[depth] = snapshot(entries[depth]!.baseKey);
  };
  // A push this module did not make opens another screen (or another view of
  // this one): what was noted about the layer entries left behind no longer applies.
  const nativePush = browserHistory.pushState.bind(browserHistory);
  nativePushState = nativePush;
  browserHistory.pushState = (data: unknown, unused: string, url?: string | URL | null) => {
    nativePush(data, unused, url);
    forgetEntries();
  };
}

/**
 * After a step down from a layer entry: the address the left layer ended on
 * (a filter chosen in the sheet) becomes the address of the entry now shown.
 */
function landed() {
  const depth = entryDepth();
  const left = depth < shownDepth ? entries[shownDepth] : undefined;
  entries.length = Math.min(entries.length, depth + 1);
  shownDepth = depth;
  // Only onto the entry the layer was opened over (the same screen, never a later one).
  if (!left || left.baseKey !== stateObject().key || left.url === window.location.href) return;
  const state: Record<string, unknown> = { ...left.state };
  const index = entryIndex();
  if (index === null) delete state.idx;
  else state.idx = index;
  if (depth > 0) state[depthKey] = depth;
  else delete state[depthKey];
  replaceEntry(state, left.url);
  if (depth > 0 && entries[depth]) entries[depth] = snapshot(entries[depth]!.baseKey);
  carrying = true;
  try {
    window.dispatchEvent(new PopStateEvent("popstate", { state }));
  } finally {
    carrying = false;
  }
}

function schedule(operation: () => void) {
  if (travelling) queue.push(operation);
  else operation();
}

function flush() {
  while (!travelling && queue.length > 0) queue.shift()!();
}

function travel(delta: number) {
  travelling = true;
  window.clearTimeout(travelTimer);
  travelTimer = window.setTimeout(finishTravel, travelTimeoutMs);
  window.history.go(delta);
}

function finishTravel() {
  travelling = false;
  window.clearTimeout(travelTimer);
  landed();
  flush();
}

function isInside(layer: Layer, ancestor: Layer): boolean {
  for (let handle = layer.handle.parent; handle; handle = handle.parent) {
    if (handle === ancestor.handle) return true;
  }
  return false;
}

/** One more entry, and the layer under the first open layer inside it (a child's effect runs before its parent's). */
function pushEntry(layer: Layer) {
  const baseKey = stateObject().key;
  const state: Record<string, unknown> = { ...stateObject(), [depthKey]: stack.length + 1 };
  const index = entryIndex();
  if (index !== null) state.idx = index + 1;
  (nativePushState ?? window.history.pushState.bind(window.history))(state, "");
  shownDepth = stack.length + 1;
  entries[shownDepth] = snapshot(baseKey);
  const inside = stack.findIndex((other) => isInside(other, layer));
  if (inside < 0) stack.push(layer);
  else stack.splice(inside, 0, layer);
}

/**
 * The layer's entry was left: its owner closes it, or keeps it and gets the
 * entry back. An answer that takes a while (a save on its way) gets the entry
 * back at once, so a second «назад» meanwhile reaches the layer again instead
 * of leaving the screen; closing then steps back over it as usual.
 */
function leave(layer: Layer) {
  const reopen = () => schedule(() => { if (live.has(layer) && !stack.includes(layer)) pushEntry(layer); });
  let result: BackResult;
  try {
    result = layer.onBack();
  } catch {
    result = undefined;
  }
  if (result instanceof Promise) {
    reopen();
    void result.then((value) => { if (value === false) reopen(); }, reopen);
  } else if (result === false) reopen();
}

function onPopState() {
  if (carrying) return;
  if (travelling) {
    finishTravel();
    return;
  }
  if (leavingScreen) {
    // The screen is being left: its layers go with it, nothing is closed or carried.
    leavingScreen = false;
    stack.length = 0;
    forgetEntries();
  }
  const depth = entryDepth();
  if (depth > stack.length) {
    // A layer that is gone: show the screen (or the layers still open) instead.
    travel(stack.length - depth);
    return;
  }
  // Every layer above the entry now shown has been left, the top one first.
  const left = stack.splice(depth).reverse();
  // The address first, so a layer that gets its entry back copies the carried one.
  landed();
  for (const layer of left) leave(layer);
}

function open(layer: Layer) {
  listen();
  live.add(layer);
  schedule(() => { if (live.has(layer) && !stack.includes(layer)) pushEntry(layer); });
}

/** The owner closed the layer itself (or unmounted it): one entry is stepped back over. */
function release(layer: Layer) {
  live.delete(layer);
  schedule(() => {
    const at = stack.indexOf(layer);
    if (at < 0) return;
    // Only while the browser shows the top entry: after a navigation to
    // another screen the entries lie behind it and are stepped over later.
    const shown = entryDepth() === stack.length;
    stack.splice(at, 1);
    if (shown) travel(-1);
  });
}

const ParentLayer = createContext<HistoryLayerHandle | null>(null);

/** Wraps what a layer shows, so layers opened inside it count as above it. */
export const HistoryLayerScope = ParentLayer.Provider;

/**
 * Makes an open layer (a sheet, a modal, an inner screen) a history step:
 * «назад» calls `onBack`, which closes it. `onBack` may return `false` (or a
 * promise of it), for example when unsaved changes could not be saved: the
 * layer then stays open and gets its step back. Returns the layer's handle,
 * for a `HistoryLayerScope` around its content.
 */
export function useHistoryLayer(isOpen: boolean, onBack: () => BackResult): HistoryLayerHandle {
  const parent = useContext(ParentLayer);
  const handleRef = useRef<HistoryLayerHandle | null>(null);
  if (handleRef.current === null || handleRef.current.parent !== parent) handleRef.current = { parent };
  const handle = handleRef.current;
  const onBackRef = useRef(onBack);
  onBackRef.current = onBack;
  useEffect(() => {
    if (!isOpen) return;
    const layer: Layer = { handle, onBack: () => onBackRef.current() };
    open(layer);
    return () => release(layer);
  }, [isOpen, handle]);
  return handle;
}

/**
 * The header back chevron: closes the top layer by the same one history step
 * as the browser's back; with no layer open, `otherwise` runs (the screen's
 * own back).
 */
export function backFromLayer(otherwise: () => void = () => undefined): void {
  // Once pending steps are done: a layer whose entry was still on its way is open by then.
  schedule(() => {
    if (stack.length === 0) otherwise();
    else if (entryDepth() === stack.length) window.history.back();
    // Its entry is not the one shown (it lies behind another screen): only the layer closes.
    else leave(stack.pop()!);
  });
}

/** Runs `step` once the history steps this module started have finished. */
export function afterLayerSteps(step: () => void) {
  schedule(step);
}

/** How many layer entries the browser shows on top of the screen's own entry. */
export function shownLayerSteps(): number {
  return entryDepth();
}

/**
 * The next history step leaves the whole screen («Готово» on a scan result):
 * its open layers are dropped without their `onBack`, so none of them
 * reacts (a camera restarting) on the way out.
 */
export function leaveScreenWithLayers() {
  if (stack.length > 0 && entryDepth() > 0) leavingScreen = true;
}

/** True while the browser shows an open layer's entry: a navigation from here can replace it instead of leaving it behind. */
export function onLayerEntry(): boolean {
  const depth = entryDepth();
  return depth > 0 && depth <= stack.length;
}

/**
 * After a reload with a layer open, the page starts on that layer's entry
 * while the layer itself is gone: step back to the screen's own entry.
 */
export function settleReloadedLayerEntry() {
  listen();
  const depth = entryDepth();
  if (depth > 0) travel(-depth);
}

/** Tests only: forgets every layer and pending step. */
export function resetHistoryLayers() {
  stack.length = 0;
  live.clear();
  queue.length = 0;
  travelling = false;
  window.clearTimeout(travelTimer);
  forgetEntries();
  carrying = false;
  leavingScreen = false;
}
