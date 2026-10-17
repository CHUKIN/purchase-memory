import { useCallback } from "react";
import { useLocation } from "react-router";

/*
 * «Назад» must land on the exact place a person left: the same scroll
 * position and, on a purchase, the same line. The place is kept in memory per
 * history entry (`location.key`), written the moment a link is tapped. Going
 * back through history reopens the very same entry, so the key finds it; the
 * fallback «open the address» path carries it in router state (`place`).
 */
export interface Place {
  /** Zero-based purchase line that was open (swipe review or line sheet). */
  line?: number;
  /** Scroll offset of the screen's content, in pixels. */
  scroll?: number;
}

const maxPlaces = 200;
const storageKey = "pm.places";

/**
 * Mirrored into sessionStorage, so a reload (an iPhone Home Screen app is
 * reloaded in the background often) keeps the places its history entries
 * point at. Storage may be missing or full: then memory alone serves.
 */
function readStored(): Map<string, Place> {
  try {
    const raw = window.sessionStorage.getItem(storageKey);
    const entries: unknown = raw ? JSON.parse(raw) : [];
    if (!Array.isArray(entries)) return new Map();
    return new Map(entries.filter((entry): entry is [string, Place] => Array.isArray(entry) && typeof entry[0] === "string" && typeof entry[1] === "object" && entry[1] !== null));
  } catch {
    return new Map();
  }
}

const places = readStored();

function writeStored() {
  try {
    window.sessionStorage.setItem(storageKey, JSON.stringify([...places]));
  } catch {
    // Memory still has it.
  }
}

/*
 * The first entry of every page load gets the same router key («default»), so
 * a place is stored under key AND address (path + search): a place left on
 * one purchase must never be found by another purchase loaded later in the
 * same tab.
 */
const slot = (key: string, path: string) => `${key}\n${path}`;

export function savePlace(key: string, path: string, place: Place, merge = false) {
  const id = slot(key, path);
  const next = merge ? { ...places.get(id), ...place } : place;
  places.delete(id);
  places.set(id, next);
  // The oldest entries are the least likely to be gone back to.
  if (places.size > maxPlaces) places.delete(places.keys().next().value as string);
  writeStored();
}

export function loadPlace(key: string, path: string): Place | null {
  return places.get(slot(key, path)) ?? null;
}

export function clearPlaces() {
  places.clear();
  try {
    window.sessionStorage.removeItem(storageKey);
  } catch {
    // Nothing to clear.
  }
}

/**
 * Where this entry was left. The store wins: it is newer than the place a
 * fallback «Назад» put in router state, which is copied in once on arrival
 * and never read again (a tap since then has moved or dropped it).
 */
export function arrivalPlace(key: string, path: string, state: unknown): Place | null {
  const stored = loadPlace(key, path);
  if (stored) return stored;
  const fromState = readPlaceState(state);
  if (fromState) savePlace(key, path, fromState);
  return fromState;
}

/** Reads `place` from router state; anything malformed is ignored. */
export function readPlaceState(state: unknown): Place | null {
  if (typeof state !== "object" || state === null || !("place" in state)) return null;
  const raw = (state as { place: unknown }).place;
  if (typeof raw !== "object" || raw === null) return null;
  const { line, scroll } = raw as { line?: unknown; scroll?: unknown };
  const place: Place = {};
  if (typeof line === "number" && Number.isInteger(line) && line >= 0) place.line = line;
  if (typeof scroll === "number" && Number.isFinite(scroll) && scroll > 0) place.scroll = scroll;
  return place.line === undefined && place.scroll === undefined ? null : place;
}

/** Where this screen was left last time, when a person came back to it; null on a fresh visit. */
export function useArrivalPlace(): Place | null {
  const location = useLocation();
  return arrivalPlace(location.key, `${location.pathname}${location.search}`, location.state);
}

/** Remembers which line is open on this screen, just before leaving it for another one. */
export function useRememberLine(): (line: number) => void {
  const { key, pathname, search } = useLocation();
  return useCallback((line: number) => savePlace(key, `${pathname}${search}`, { line }, true), [key, pathname, search]);
}

/** The scrolling content of the screen in front (not a modal's own content). */
export function pageContent(): HTMLElement | null {
  return document.querySelector<HTMLElement>("div.ion-page > ion-content");
}

function innerScroller(content: HTMLElement): HTMLElement | null {
  return content.shadowRoot?.querySelector<HTMLElement>(".inner-scroll") ?? null;
}

export function currentScroll(): number {
  const content = pageContent();
  return content ? innerScroller(content)?.scrollTop ?? 0 : 0;
}

type ScrollableContent = HTMLElement & { scrollToPoint?: (x: number, y: number, duration?: number) => Promise<void> };

/**
 * Scrolls the screen's content to `top` as soon as it is long enough (lists
 * load after the screen appears). Gives up after `timeoutMs`, or as soon as
 * the person scrolls or touches the screen themselves. Returns a cleanup.
 */
export function restoreScroll(top: number, timeoutMs = 2500): () => void {
  let stopped = false;
  let timer = 0;
  let giveUp = 0;
  const stop = () => {
    stopped = true;
    window.clearInterval(timer);
    window.clearTimeout(giveUp);
    window.removeEventListener("pointerdown", stop, true);
    window.removeEventListener("wheel", stop, true);
    window.removeEventListener("keydown", stop, true);
  };
  const attempt = () => {
    if (stopped) return;
    const content = pageContent() as ScrollableContent | null;
    const scroller = content ? innerScroller(content) : null;
    if (!content || !scroller) return;
    if (scroller.scrollHeight - scroller.clientHeight < top - 1) return;
    if (typeof content.scrollToPoint === "function") void content.scrollToPoint(0, top, 0);
    else scroller.scrollTop = top;
    stop();
  };
  timer = window.setInterval(attempt, 80);
  giveUp = window.setTimeout(stop, timeoutMs);
  window.addEventListener("pointerdown", stop, true);
  window.addEventListener("wheel", stop, true);
  window.addEventListener("keydown", stop, true);
  attempt();
  return stop;
}

/** Router state without the remembered place, for switches on the same screen (view ↔ edit). */
export function withoutPlace(state: unknown): unknown {
  if (typeof state !== "object" || state === null || !("place" in state)) return state;
  const { place: _place, ...rest } = state as { place: unknown };
  return rest;
}
