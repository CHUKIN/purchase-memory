import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { SettingsSheet } from "../../app/SettingsSheet";
import { dialog } from "../../test/ionicHelpers";
import {
  defaultLineLocatorMode,
  lineLocatorStorageKey,
  readLineLocatorMode,
  writeLineLocatorMode,
  type LineLocatorMode
} from "./lineLocatorMode";
import { contextWindow } from "./photoBox";
import { frameStyle } from "./ReceiptPhotoFocus";
import { ReceiptLineViewer, type LocatableLine, type LineViewerPhoto } from "./ReceiptLineViewer";

const lines: LocatableLine[] = [
  { name: "MLEKO 3,2%", quantity: 1, unit: "szt", unitPrice: 4.49, lineTotal: 4.49, kind: "product", boundingBox: { x: 0.1, y: 0.2, width: 0.8, height: 0.03 } },
  { name: "CHLEB ŻYTNI", quantity: 2, unit: "szt", unitPrice: 3.5, lineTotal: 7, kind: "product", boundingBox: null },
  { name: "OPUST", quantity: null, unit: null, unitPrice: null, lineTotal: -1, kind: "discount", boundingBox: { x: 0.1, y: 0.5, width: 0.8, height: 0.03 } }
];

const singlePhoto: LineViewerPhoto = { photo: { url: "blob:receipt", croppable: true }, failed: false };

function renderViewer({ openIndex = 0, mode = "tap" as LineLocatorMode | "stored", photo = singlePhoto, onClose = () => undefined } = {}) {
  return render(
    <AppProviders locale="ru">
      <MemoryRouter>
        <ReceiptLineViewer lines={lines} openIndex={openIndex} onClose={onClose} photo={photo} currency="PLN" mode={mode === "stored" ? undefined : mode} />
      </MemoryRouter>
    </AppProviders>
  );
}

/** jsdom never loads images: report the photo's natural size as a browser would. */
function loadPhoto(image: HTMLElement, width = 600, height = 1200) {
  Object.defineProperty(image, "naturalWidth", { configurable: true, value: width });
  Object.defineProperty(image, "naturalHeight", { configurable: true, value: height });
  fireEvent.load(image);
}

const highlightedFrame = () => document.querySelector(".pm-locator-photo");
const shownName = () => within(dialog()).getByRole("heading", { level: 3 }).textContent;

afterEach(() => vi.restoreAllMocks());

describe("line locator setting", () => {
  it("defaults to tap and remembers the choice on this device", () => {
    expect(defaultLineLocatorMode).toBe("tap");
    expect(readLineLocatorMode()).toBe("tap");

    writeLineLocatorMode("swipe");
    expect(window.localStorage.getItem(lineLocatorStorageKey)).toBe("swipe");
    expect(readLineLocatorMode()).toBe("swipe");
  });

  it("ignores an unknown stored value", () => {
    window.localStorage.setItem(lineLocatorStorageKey, "zoom");
    expect(readLineLocatorMode()).toBe("tap");
  });

  it("keeps the choice for this visit when storage is blocked", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("blocked"); });
    writeLineLocatorMode("swipe");
    expect(readLineLocatorMode()).toBe("swipe");
  });

  it("is switched in the settings sheet", () => {
    render(
      <AppProviders locale="ru">
        <MemoryRouter>
          <SettingsSheet isOpen onClose={() => undefined} recognition={null} />
        </MemoryRouter>
      </AppProviders>
    );
    const segment = screen.getByRole("tablist", { name: "Как показывать строки на фото чека" });
    expect(within(segment).getByRole("tab", { name: "Нажатие" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText(/обведена рамкой/)).toBeInTheDocument();

    fireEvent.click(within(segment).getByRole("tab", { name: "Пролистывание" }));
    expect(within(segment).getByRole("tab", { name: "Пролистывание" })).toHaveAttribute("aria-selected", "true");
    expect(window.localStorage.getItem(lineLocatorStorageKey)).toBe("swipe");
    expect(screen.getByText(/листать строки по одной/)).toBeInTheDocument();
  });
});

describe("ReceiptLineViewer in tap mode", () => {
  it("shows the tapped line outlined on its fragment of the photo", () => {
    renderViewer({ openIndex: 2 });

    expect(within(dialog()).getByRole("heading", { level: 2 })).toHaveTextContent("Строка 3 на фото");
    expect(shownName()).toBe("OPUST");
    const image = screen.getByRole("img", { name: "Фрагмент фото чека, рамкой отмечена строка «OPUST»" });
    expect(highlightedFrame()).toHaveAttribute("data-highlighted", "true");
    // The outline waits for the photo's proportions so it is never misplaced.
    expect(document.querySelector(".pm-locator-highlight")).toBeNull();

    loadPhoto(image);
    const highlight = document.querySelector<HTMLElement>(".pm-locator-highlight")!;
    const frame = contextWindow(lines[2].boundingBox!);
    expect(highlight.style.top).toBe(`${(100 * (0.5 - frame.y)) / frame.height}%`);
    expect(highlight.style.height).toBe(`${(100 * 0.03) / frame.height}%`);
    expect(screen.queryByRole("button", { name: "Следующая строка" })).not.toBeInTheDocument();
  });

  it("shows the whole photo without an outline when the line has no box", () => {
    renderViewer({ openIndex: 1 });

    expect(screen.getByRole("img", { name: "Фото чека целиком" })).toBeInTheDocument();
    expect(highlightedFrame()).toHaveAttribute("data-highlighted", "false");
    expect(document.querySelector(".pm-locator-highlight")).toBeNull();
    expect(screen.getByText("Место этой строки на фото не найдено — показан весь чек.")).toBeInTheDocument();
    expect(within(dialog()).getByText(/2 szt ×/)).toBeInTheDocument();
  });

  it("never outlines a line on a multi-photo receipt", () => {
    renderViewer({ openIndex: 0, photo: { photo: { url: "blob:receipt", croppable: false }, failed: false } });

    loadPhoto(screen.getByRole("img", { name: "Фото чека целиком" }));
    expect(document.querySelector(".pm-locator-highlight")).toBeNull();
    expect(screen.getByText(/нескольких фото/)).toBeInTheDocument();
  });

  it("says when the photo is still loading or failed", () => {
    const { unmount } = renderViewer({ photo: { photo: null, failed: false } });
    expect(screen.getByText("Загружаем фото чека…")).toBeInTheDocument();
    unmount();
    renderViewer({ photo: { photo: null, failed: true } });
    expect(screen.getByRole("alert")).toHaveTextContent("Фото чека не загрузилось.");
  });

  it("follows the stored setting when the screen does not override it", () => {
    writeLineLocatorMode("swipe");
    renderViewer({ mode: "stored" });
    expect(within(dialog()).getByRole("heading", { level: 2 })).toHaveTextContent("Строки на фото");
    expect(screen.getByRole("button", { name: "Следующая строка" })).toBeInTheDocument();
  });

  it("closes with «Готово»", () => {
    const onClose = vi.fn();
    renderViewer({ onClose });
    fireEvent.click(within(dialog()).getByRole("button", { name: "Готово" }));
    expect(onClose).toHaveBeenCalled();
  });
});

describe("ReceiptLineViewer in swipe mode", () => {
  it("starts at the tapped line and moves with the buttons", () => {
    renderViewer({ openIndex: 1, mode: "swipe" });

    expect(shownName()).toBe("CHLEB ŻYTNI");
    expect(screen.getByRole("status")).toHaveTextContent("Строка 2 из 3");
    // A line without a box is still part of the walk, shown on the whole photo.
    expect(screen.getByRole("img", { name: "Фото чека целиком" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Следующая строка" }));
    expect(shownName()).toBe("OPUST");
    expect(screen.getByRole("status")).toHaveTextContent("Строка 3 из 3");
    expect(screen.getByRole("button", { name: "Следующая строка" })).toBeDisabled();
    expect(screen.getByRole("img", { name: /отмечена строка «OPUST»/ })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Предыдущая строка" }));
    fireEvent.click(screen.getByRole("button", { name: "Предыдущая строка" }));
    expect(shownName()).toBe("MLEKO 3,2%");
    expect(screen.getByRole("button", { name: "Предыдущая строка" })).toBeDisabled();
  });

  it("moves with the arrow keys", () => {
    renderViewer({ openIndex: 0, mode: "swipe" });
    const next = screen.getByRole("button", { name: "Следующая строка" });

    fireEvent.keyDown(next, { key: "ArrowRight" });
    expect(shownName()).toBe("CHLEB ŻYTNI");
    fireEvent.keyDown(next, { key: "ArrowLeft" });
    fireEvent.keyDown(next, { key: "ArrowLeft" });
    expect(shownName()).toBe("MLEKO 3,2%");
  });

  it("moves with a horizontal swipe on the photo and ignores a vertical one", () => {
    renderViewer({ openIndex: 0, mode: "swipe" });
    const stage = document.querySelector(".pm-locator-stage")!;

    fireEvent.pointerDown(stage, { clientX: 300, clientY: 100 });
    fireEvent.pointerUp(stage, { clientX: 180, clientY: 110 });
    expect(shownName()).toBe("CHLEB ŻYTNI");

    fireEvent.pointerDown(stage, { clientX: 200, clientY: 100 });
    fireEvent.pointerUp(stage, { clientX: 150, clientY: 300 });
    expect(shownName()).toBe("CHLEB ŻYTNI");

    fireEvent.pointerDown(stage, { clientX: 100, clientY: 100 });
    fireEvent.pointerUp(stage, { clientX: 260, clientY: 100 });
    expect(shownName()).toBe("MLEKO 3,2%");
  });

  it("moves focus to the status instead of dropping it when a button becomes disabled", () => {
    renderViewer({ openIndex: 1, mode: "swipe" });
    const next = screen.getByRole("button", { name: "Следующая строка" });
    next.focus();
    fireEvent.click(next);
    expect(next).toBeDisabled();
    expect(document.activeElement).toHaveClass("pm-locator-position");
    expect(dialog()).toContainElement(document.activeElement as HTMLElement);
  });

  it("announces the line name and sum with its position", () => {
    renderViewer({ openIndex: 0, mode: "swipe" });
    fireEvent.click(screen.getByRole("button", { name: "Следующая строка" }));
    expect(screen.getByRole("status")).toHaveTextContent(/^Строка 2 из 3: CHLEB ŻYTNI, 7,00\s?(PLN|zł)$/);
  });

  it("starts again from the tapped line after closing and reopening", () => {
    const view = (openIndex: number | null) => (
      <AppProviders locale="ru">
        <MemoryRouter>
          <ReceiptLineViewer lines={lines} openIndex={openIndex} onClose={() => undefined} photo={singlePhoto} currency="PLN" mode="swipe" />
        </MemoryRouter>
      </AppProviders>
    );
    const { rerender } = render(view(0));
    fireEvent.click(screen.getByRole("button", { name: "Следующая строка" }));
    expect(shownName()).toBe("CHLEB ŻYTNI");
    rerender(view(null));
    rerender(view(0));
    expect(shownName()).toBe("MLEKO 3,2%");
  });

  it("does not react to swipes in tap mode", () => {
    renderViewer({ openIndex: 0, mode: "tap" });
    const stage = document.querySelector(".pm-locator-stage")!;
    fireEvent.pointerDown(stage, { clientX: 300, clientY: 100 });
    fireEvent.pointerUp(stage, { clientX: 100, clientY: 100 });
    expect(shownName()).toBe("MLEKO 3,2%");
  });
});

describe("photo strip geometry", () => {
  it("keeps the window's aspect ratio and narrows instead of shortening", () => {
    // A 1:2 photo, full width, a tenth of its height: the strip is 5:1.
    const style = frameStyle(0.5, { x: 0, y: 0.2, width: 1, height: 0.1 });
    expect(style.aspectRatio).toBe("5");
    expect(style.width).toBe("min(100%, calc(55vh * 5))");
    expect(style).not.toHaveProperty("maxHeight");
  });
});
