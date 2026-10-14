import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { dialog } from "../../test/ionicHelpers";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import { LineReview, type ReviewLine, type ReviewLinePatch, type ReviewStart } from "./LineReview";

const box = { x: 0.1, y: 0.3, width: 0.8, height: 0.04 };
const baseLines: ReviewLine[] = [
  { id: "a", name: "KAWA", quantity: 1, unit: "szt", unitPrice: 15, lineTotal: 15, kind: "product", boundingBox: box, productName: "Kawa" },
  { id: "b", name: "MLEKO 2%", quantity: 2, unit: "szt", unitPrice: 3.49, lineTotal: 6.98, kind: "product", boundingBox: null, pendingMatch: true },
  { id: "c", name: "RABAT MLEKO", quantity: null, unit: null, unitPrice: null, lineTotal: -1, kind: "discount", boundingBox: null },
  { id: "d", name: "CHLEB", quantity: 1, unit: "szt", unitPrice: 4.5, lineTotal: 4.5, kind: "product", boundingBox: box }
];
const photo: LineViewerPhoto = { photo: { url: "blob:receipt", croppable: true }, failed: false };

interface HarnessProps {
  start: ReviewStart | null;
  approved?: string[];
  lines?: ReviewLine[];
  onSaveLine?: (index: number, patch: ReviewLinePatch) => Promise<void>;
  photo?: LineViewerPhoto | null;
}

/** The review with its marks in plain state, like the page keeps them in the sync. */
function Harness({ start: initialStart, approved = [], lines = baseLines, onSaveLine, photo: shownPhoto = photo }: HarnessProps) {
  const [marks, setMarks] = useState(() => new Set(approved));
  const [start, setStart] = useState(initialStart);
  return (
    <AppProviders locale="ru">
      <p data-testid="marks">{[...marks].sort().join(",")}</p>
      <button type="button" onClick={() => setStart((current) => ({ index: null, nonce: (current?.nonce ?? 0) + 1 }))}>reopen</button>
      <LineReview
        start={start}
        onClose={() => setStart(null)}
        lines={lines}
        isApproved={(line) => marks.has(line.id)}
        setApproved={(id, value) => setMarks((current) => {
          const next = new Set(current);
          if (value) next.add(id); else next.delete(id);
          return next;
        })}
        onSaveLine={onSaveLine ?? (async () => undefined)}
        photo={shownPhoto}
        currency="PLN"
      />
    </AppProviders>
  );
}

const card = () => screen.getByTestId("line-review-card");
const cardName = () => within(card()).getByRole("heading", { level: 3 }).textContent;
const marks = () => screen.getByTestId("marks").textContent;

function swipe(dx: number, { dy = 0, pointerId = 1 } = {}) {
  const element = card();
  fireEvent.pointerDown(element, { pointerId, clientX: 200, clientY: 300 });
  fireEvent.pointerMove(element, { pointerId, clientX: 200 + dx / 2, clientY: 300 + dy / 2 });
  fireEvent.pointerMove(element, { pointerId, clientX: 200 + dx, clientY: 300 + dy });
  fireEvent.pointerUp(element, { pointerId, clientX: 200 + dx, clientY: 300 + dy });
}

describe("LineReview", () => {
  it("opens at the first unchecked line with the photo, the line's details and the progress", () => {
    render(<Harness start={{ index: null, nonce: 1 }} approved={["a"]} />);

    const review = dialog();
    expect(within(review).getByLabelText("Проверено 1 из 4")).toBeInTheDocument();
    expect(cardName()).toBe("MLEKO 2%");
    expect(within(card()).getByText("Строка 2")).toBeInTheDocument();
    expect(within(card()).getByText(/2 szt × 3,49/)).toBeInTheDocument();
    expect(within(card()).getByText("Не удалось выбрать товар автоматически — выберите сами.")).toBeInTheDocument();
    // Its discount and the price after it, on the same card.
    expect(within(card()).getByText("RABAT MLEKO")).toBeInTheDocument();
    expect(within(card()).getByText("Цена товара после скидки:")).toBeInTheDocument();
    // No box: the whole photo, with a short note.
    expect(within(card()).getByRole("img", { name: "Фото чека целиком" })).toHaveAttribute("src", "blob:receipt");
    expect(within(card()).getByText("Место строки не найдено — показан весь чек.")).toBeInTheDocument();
    // Every icon button has a name.
    for (const name of ["Закрыть проверку", "Предыдущая строка", "Исправить строку", "Строка верна", "Увеличить фото"]) {
      expect(within(review).getByRole("button", { name })).toBeInTheDocument();
    }
  });

  it("swipe right checks the line, sends the card away and shows the next unchecked one", async () => {
    render(<Harness start={{ index: 0, nonce: 1 }} approved={["b"]} />);
    expect(cardName()).toBe("KAWA");

    swipe(160);
    expect(marks()).toBe("a,b");
    expect(card()).toHaveAttribute("data-leaving", "true");
    // «MLEKO 2%» is already checked, so the discount comes next.
    await waitFor(() => expect(cardName()).toBe("RABAT MLEKO"));
    expect(within(card()).getByText("к «MLEKO 2%»")).toBeInTheDocument();
  });

  it("a short or vertical drag does nothing", () => {
    render(<Harness start={{ index: 0, nonce: 1 }} />);
    swipe(30);
    swipe(160, { dy: 400 });
    expect(marks()).toBe("");
    expect(cardName()).toBe("KAWA");
  });

  it("swipe left opens the line's form; saving counts as checked and moves on", async () => {
    const onSaveLine = vi.fn(async () => undefined);
    render(<Harness start={{ index: 0, nonce: 1 }} onSaveLine={onSaveLine} />);

    swipe(-160);
    const form = dialog("Строка 1");
    expect(within(form).getByLabelText("Название")).toHaveValue("KAWA");
    fireEvent.change(within(form).getByLabelText("Цена"), { target: { value: "14,5" } });
    fireEvent.click(within(form).getByRole("button", { name: "Сохранить" }));

    await waitFor(() => expect(onSaveLine).toHaveBeenCalledWith(0, expect.objectContaining({ name: "KAWA", unitPrice: 14.5 })));
    await waitFor(() => expect(cardName()).toBe("MLEKO 2%"));
    expect(screen.queryByRole("heading", { name: "Строка 1" })).not.toBeInTheDocument();
  });

  it("keeps the form open with the error when saving fails, and Cancel goes back to the card", async () => {
    const onSaveLine = vi.fn(async () => { throw new Error("Сервер недоступен"); });
    render(<Harness start={{ index: 0, nonce: 1 }} onSaveLine={onSaveLine} />);

    fireEvent.click(screen.getByRole("button", { name: "Исправить строку" }));
    fireEvent.click(within(dialog("Строка 1")).getByRole("button", { name: "Сохранить" }));
    expect(await within(dialog("Строка 1")).findByRole("alert")).toHaveTextContent("Сервер недоступен");
    fireEvent.click(within(dialog("Строка 1")).getByRole("button", { name: "Отмена" }));
    expect(screen.queryByRole("heading", { name: "Строка 1" })).not.toBeInTheDocument();
    expect(cardName()).toBe("KAWA");
    expect(marks()).toBe("");
  });

  it("works from the keyboard and the buttons: → checks, ← corrects, back returns", async () => {
    render(<Harness start={{ index: 0, nonce: 1 }} />);

    fireEvent.keyDown(card(), { key: "ArrowRight" });
    await waitFor(() => expect(cardName()).toBe("MLEKO 2%"));
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    await waitFor(() => expect(cardName()).toBe("RABAT MLEKO"));
    expect(marks()).toBe("a,b");

    fireEvent.click(screen.getByRole("button", { name: "Предыдущая строка" }));
    expect(cardName()).toBe("MLEKO 2%");
    // A checked line says so, and the mark can be taken off.
    fireEvent.click(within(card()).getByRole("button", { name: "Снять отметку «Проверено»" }));
    expect(marks()).toBe("a");

    fireEvent.keyDown(card(), { key: "ArrowLeft" });
    expect(dialog("Строка 2")).toBeInTheDocument();
  });

  it("shows «all checked» at the end, with a way to check everything again", async () => {
    render(<Harness start={{ index: null, nonce: 1 }} approved={["a", "b", "c"]} />);
    expect(cardName()).toBe("CHLEB");

    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    expect(await screen.findByText("Все строки проверены")).toBeInTheDocument();
    expect(screen.getByLabelText("Проверено 4 из 4")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Проверить ещё раз" }));
    expect(cardName()).toBe("KAWA");
    expect(within(card()).getByText("Проверено")).toBeInTheDocument();
  });

  it("coming back shows only lines nobody has checked yet", async () => {
    render(<Harness start={{ index: null, nonce: 1 }} />);
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    await waitFor(() => expect(cardName()).toBe("MLEKO 2%"));
    fireEvent.click(screen.getByRole("button", { name: "Закрыть проверку" }));
    expect(screen.queryByTestId("line-review-card")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "reopen" }));
    expect(cardName()).toBe("MLEKO 2%");
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    await waitFor(() => expect(cardName()).toBe("RABAT MLEKO"));
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    await waitFor(() => expect(cardName()).toBe("CHLEB"));
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    expect(await screen.findByText("Все строки проверены")).toBeInTheDocument();

    // Opening again when everything is checked goes straight to that state.
    fireEvent.click(screen.getByRole("button", { name: "Закрыть проверку" }));
    fireEvent.click(screen.getByRole("button", { name: "reopen" }));
    expect(screen.getByText("Все строки проверены")).toBeInTheDocument();
  });

  it("does not swipe while the photo is zoomed, nor during a two-finger pinch", async () => {
    render(<Harness start={{ index: 0, nonce: 1 }} />);
    const zoom = screen.getByRole("button", { name: "Увеличить фото" });

    fireEvent.click(zoom);
    expect(screen.getByRole("button", { name: "Уменьшить фото" })).toBeInTheDocument();
    swipe(200);
    expect(marks()).toBe("");
    expect(cardName()).toBe("KAWA");

    fireEvent.click(screen.getByRole("button", { name: "Уменьшить фото" }));
    // A second finger turns the drag into a pinch.
    const element = card();
    fireEvent.pointerDown(element, { pointerId: 1, clientX: 100, clientY: 300 });
    fireEvent.pointerDown(element, { pointerId: 2, clientX: 140, clientY: 300 });
    fireEvent.pointerMove(element, { pointerId: 1, clientX: 300, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 1, clientX: 300, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 2, clientX: 140, clientY: 300 });
    expect(marks()).toBe("");

    // Unzoomed again, the swipe works.
    swipe(200);
    expect(marks()).toBe("a");
  });

  it("zooms the photo with a double tap and keeps the outline inside the zoomed layer", async () => {
    render(<Harness start={{ index: 0, nonce: 1 }} />);
    const image = within(card()).getByRole("img", { name: "Фото чека, рамкой отмечена строка «KAWA»" });
    act(() => {
      Object.defineProperty(image, "naturalWidth", { value: 750, configurable: true });
      Object.defineProperty(image, "naturalHeight", { value: 1000, configurable: true });
      fireEvent.load(image);
    });
    const frame = image.closest(".pm-photo-frame") as HTMLElement;
    const layer = image.parentElement as HTMLElement;
    expect(layer.querySelector(".pm-locator-highlight")).not.toBeNull();

    for (let tap = 0; tap < 2; tap += 1) {
      fireEvent.pointerDown(frame, { pointerId: 5, clientX: 10, clientY: 10 });
      fireEvent.pointerUp(frame, { pointerId: 5, clientX: 10, clientY: 10 });
    }
    expect(frame).toHaveAttribute("data-zoomed", "true");
    expect(layer.style.transform).toContain("scale(2.5)");
    expect(screen.getByRole("button", { name: "Уменьшить фото" })).toBeInTheDocument();
  });

  it("sizes the photo by layout, not by the card's flying transform, line after line", async () => {
    // The space is 335 × 300; a card still flying off (translated, rotated)
    // reports a bigger box. The frame must follow the layout size.
    const isSpace = (element: Element) => element.classList.contains("pm-photo-space");
    const width = vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockImplementation(function (this: HTMLElement) { return isSpace(this) ? 335 : 0; });
    const height = vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockImplementation(function (this: HTMLElement) { return isSpace(this) ? 300 : 0; });
    const rect = vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
      return { x: 0, y: 0, left: 0, top: 0, right: 409, bottom: 345, width: 409, height: 345, toJSON: () => ({}) } as DOMRect;
    });
    try {
      render(<Harness start={{ index: 0, nonce: 1 }} />);
      for (const name of ["KAWA", "MLEKO 2%", "RABAT MLEKO", "CHLEB"]) {
        await waitFor(() => expect(cardName()).toBe(name));
        const image = within(card()).getByRole("img");
        act(() => {
          Object.defineProperty(image, "naturalWidth", { value: 750, configurable: true });
          Object.defineProperty(image, "naturalHeight", { value: 1000, configurable: true });
          fireEvent.load(image);
        });
        const frame = image.closest(".pm-photo-frame") as HTMLElement;
        expect(parseFloat(frame.style.width)).toBeLessThanOrEqual(335);
        expect(parseFloat(frame.style.height)).toBeLessThanOrEqual(300);
        fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
      }
    } finally {
      width.mockRestore();
      height.mockRestore();
      rect.mockRestore();
    }
  });

  it("shows the next card in place after one flies off", async () => {
    render(<Harness start={{ index: 0, nonce: 1 }} />);
    const first = card();
    fireEvent.click(screen.getByRole("button", { name: "Строка верна" }));
    await waitFor(() => expect(cardName()).toBe("MLEKO 2%"));
    // A new element, not the old one coming back from off-screen.
    expect(card()).not.toBe(first);
    expect(card()).toHaveAttribute("data-leaving", "false");
  });

  it("reviews lines without a receipt photo too", () => {
    render(<Harness start={{ index: 3, nonce: 1 }} photo={null} />);
    expect(cardName()).toBe("CHLEB");
    expect(within(card()).getByText("У этой покупки нет фото чека.")).toBeInTheDocument();
  });
});
