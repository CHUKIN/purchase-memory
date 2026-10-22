import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { dialog } from "../../test/ionicHelpers";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import type { EditableLine } from "../receipts/LinesEditor";
import { carouselSteps, stepLinePosition } from "./carouselSteps";
import { LineCarousel } from "./LineCarousel";
import { firstProblemIndex, lineProblem, totalsCheck } from "./lineChecks";

const box = { x: 0.1, y: 0.3, width: 0.8, height: 0.04 };
const baseLines: EditableLine[] = [
  { name: "KAWA", quantity: 1, unit: "szt", unitPrice: 15, lineTotal: 15, kind: "product", boundingBox: box },
  { name: "MLEKO 2%", quantity: 2, unit: "szt", unitPrice: 3.49, lineTotal: 6.89, kind: "product", boundingBox: box },
  { name: "RABAT MLEKO", quantity: null, unit: null, unitPrice: null, lineTotal: -1, kind: "discount", boundingBox: null },
  { name: "CHLEB", quantity: 1, unit: null, unitPrice: 4.5, lineTotal: 4.5, kind: "product", boundingBox: null }
];
const photo: LineViewerPhoto = { photo: { url: "blob:receipt", croppable: true }, failed: false };

function Harness({ lines: initialLines = baseLines, receiptTotal = 25.48, start, shownPhoto = photo, restorable = false, onFinish }: {
  lines?: EditableLine[];
  receiptTotal?: number | null;
  start?: number;
  shownPhoto?: LineViewerPhoto | null;
  /** «Убрать» removes at once and offers «Вернуть», like the receipt draft. */
  restorable?: boolean;
  onFinish?: () => void;
}) {
  const [lines, setLines] = useState(initialLines);
  const [index, setIndex] = useState(start ?? stepLinePosition(initialLines, firstProblemIndex(initialLines)));
  const [checked, setChecked] = useState<number[]>([]);
  return (
    <AppProviders locale="ru">
      <p data-testid="checked">{[...checked].sort().join(",")}</p>
      <p data-testid="names">{lines.map((line) => line.name).join("|")}</p>
      <LineCarousel
        lines={lines}
        index={index}
        onIndexChange={setIndex}
        onChange={(position, patch) => setLines((current) => current.map((line, other) => other === position ? { ...line, ...patch } : line))}
        onRemove={(position) => {
          setLines((current) => current.filter((_, other) => other !== position));
          setIndex((current) => current > position ? current - 1 : Math.max(0, Math.min(current, lines.length - 2)));
        }}
        onRestore={restorable ? (position, line) => {
          setLines((current) => [...current.slice(0, position), line, ...current.slice(position)]);
          setIndex((current) => current >= position ? current + 1 : current);
        } : undefined}
        isChecked={(position) => checked.includes(position)}
        onApprove={(position, related) => setChecked((current) => [...current, position, ...related])}
        onUnapprove={(position, related) => setChecked((current) => current.filter((other) => other !== position && !related.includes(other)))}
        onFinish={onFinish}
        currency="PLN"
        receiptTotal={receiptTotal}
        photo={shownPhoto}
      />
    </AppProviders>
  );
}

const position = () => screen.getByText(/^(Товар \d+ из \d+|Скидка.*)$/, { selector: ".pm-carousel-count" }).textContent;
const shownName = () => (screen.getByLabelText(/^Название$/) as HTMLInputElement).value;
const card = () => screen.getByTestId("line-carousel-card");

function swipe(element: HTMLElement, dx: number, dy = 0) {
  fireEvent.pointerDown(element, { pointerId: 1, clientX: 200, clientY: 300 });
  fireEvent.pointerMove(element, { pointerId: 1, clientX: 200 + dx / 2, clientY: 300 + dy / 2 });
  fireEvent.pointerUp(element, { pointerId: 1, clientX: 200 + dx, clientY: 300 + dy });
}

describe("line checks", () => {
  it("flags a missing price, a missing sum and quantity × price ≠ sum, but not a discount with its amount", () => {
    expect(lineProblem(baseLines[0])).toBeNull();
    expect(lineProblem(baseLines[1])).toEqual({ kind: "arithmetic", expected: 6.98 });
    expect(lineProblem(baseLines[2])).toBeNull();
    expect(lineProblem({ ...baseLines[0], unitPrice: null })).toEqual({ kind: "missingPrice" });
    expect(lineProblem({ ...baseLines[0], lineTotal: null })).toEqual({ kind: "missingTotal" });
    // Weighed goods round to the cent: 1.165 kg × 38.25 = 44.56125.
    expect(lineProblem({ quantity: 1.165, unitPrice: 38.25, lineTotal: 44.56, kind: "product" })).toBeNull();
  });

  it("compares the lines with the receipt total within a cent", () => {
    expect(totalsCheck(baseLines, 25.39)).toMatchObject({ linesTotal: 25.39, matches: true, difference: 0 });
    expect(totalsCheck(baseLines, 30)).toMatchObject({ matches: false, difference: -4.61 });
    expect(totalsCheck([{ lineTotal: null }, { lineTotal: 2 }], null)).toMatchObject({ linesTotal: 2, missing: 1, matches: false, difference: null });
  });
});

describe("carousel steps", () => {
  it("puts a product's discount in its card and gives a discount on no product a card of its own", () => {
    const steps = carouselSteps([
      { name: "RABAT NA PARAGON", quantity: null, unit: null, unitPrice: null, lineTotal: -2, kind: "discount" },
      ...baseLines
    ]);
    expect(steps.map((step) => [step.position, step.kind, step.discounts])).toEqual([
      [0, "discount", []],
      [1, "product", []],
      [2, "product", [3]],
      [4, "product", []]
    ]);
    expect(steps[0].appliesToReceipt).toBe(true);
    expect(stepLinePosition(baseLines, 2)).toBe(1);
    expect(stepLinePosition(baseLines, 3)).toBe(3);
  });
});

describe("LineCarousel", () => {
  it("starts at the first product with a problem, explains the problem and marks it on its dot", () => {
    render(<Harness />);

    // Three products: the discount is inside MLEKO's card, not a card of its own.
    expect(position()).toBe("Товар 2 из 3");
    expect(shownName()).toBe("MLEKO 2%");
    expect(within(card()).getByText(/^2 × 3,49.*= 6,98.*а сумма 6,89.*Проверьте по фото\.$/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Товар 2, нужно проверить" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("button", { name: "Товар 1" })).not.toHaveAttribute("aria-current");
    expect(screen.getAllByRole("button", { name: /^Товар \d/ })).toHaveLength(3);
    // Every control has a name.
    for (const name of ["Предыдущая строка", "Следующая строка", "Удалить", "Верно, дальше", "Увеличить фото", "Убрать скидку RABAT MLEKO"]) {
      expect(screen.getByRole("button", { name })).toBeInTheDocument();
    }
  });

  it("starts at the first product when nothing is wrong", () => {
    render(<Harness lines={[baseLines[0], baseLines[3]]} receiptTotal={19.5} />);
    expect(position()).toBe("Товар 1 из 2");
    expect(screen.queryByText(/Проверьте по фото/)).not.toBeInTheDocument();
  });

  it("moves with the chevrons and the dots, and the chevrons stop at the ends", () => {
    render(<Harness start={0} />);

    expect(screen.getByRole("button", { name: "Предыдущая строка" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Следующая строка" }));
    expect(position()).toBe("Товар 2 из 3");
    fireEvent.click(screen.getByRole("button", { name: "Товар 3" }));
    expect(position()).toBe("Товар 3 из 3");
    expect(shownName()).toBe("CHLEB");
    expect(screen.getByRole("button", { name: "Следующая строка" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Предыдущая строка" }));
    expect(position()).toBe("Товар 2 из 3");
  });

  it("a swipe of 50 px or more on the card or the photo turns the card; shorter, vertical or from a field does nothing", () => {
    render(<Harness start={0} />);

    swipe(card(), -30);
    expect(position()).toBe("Товар 1 из 3");
    swipe(card(), -120, 200);
    expect(position()).toBe("Товар 1 из 3");
    swipe(screen.getByLabelText("Название"), -120);
    expect(position()).toBe("Товар 1 из 3");

    swipe(card(), -60);
    expect(position()).toBe("Товар 2 из 3");
    swipe(screen.getByRole("img", { name: /Фото чека, выделена строка/ }).closest(".pm-carousel-photo") as HTMLElement, -80);
    expect(position()).toBe("Товар 3 из 3");
    swipe(card(), 90);
    expect(position()).toBe("Товар 2 из 3");
  });

  it("a second finger (a pinch) is not a swipe", () => {
    render(<Harness start={0} />);
    const element = card();
    fireEvent.pointerDown(element, { pointerId: 1, clientX: 200, clientY: 300 });
    fireEvent.pointerDown(element, { pointerId: 2, clientX: 260, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 1, clientX: 100, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 2, clientX: 300, clientY: 300 });
    expect(position()).toBe("Товар 1 из 3");
  });

  it("«Верно, дальше» checks the product with the discounts in its card and moves on; «Проверено» takes the marks back", () => {
    render(<Harness start={1} />);

    fireEvent.click(screen.getByRole("button", { name: "Верно, дальше" }));
    expect(screen.getByTestId("checked")).toHaveTextContent("1,2");
    expect(position()).toBe("Товар 3 из 3");
    expect(screen.getByRole("button", { name: "Товар 2, проверен" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Товар 2, проверен" }));
    fireEvent.click(screen.getByRole("button", { name: "Снять отметку «Проверено»" }));
    expect(screen.getByTestId("checked")).toHaveTextContent("");
    expect(screen.getByRole("button", { name: "Верно, дальше" })).toBeInTheDocument();
  });

  it("without a summary the last product's button is just «Верно» and the product stays", () => {
    render(<Harness start={3} />);
    fireEvent.click(screen.getByRole("button", { name: "Верно" }));
    expect(position()).toBe("Товар 3 из 3");
    expect(screen.getByRole("button", { name: "Снять отметку «Проверено»" })).toBeInTheDocument();
  });

  it("with a summary the last product reads «Верно, к итогу» and leads there, also once checked", () => {
    const onFinish = vi.fn();
    render(<Harness start={3} onFinish={onFinish} />);

    fireEvent.click(screen.getByRole("button", { name: "Верно, к итогу" }));
    expect(onFinish).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("checked")).toHaveTextContent("3");
    // Checked: the small ✓ takes the mark back, «К итогу» still leads on.
    expect(screen.getByRole("button", { name: "Снять отметку «Проверено»" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "К итогу" }));
    expect(onFinish).toHaveBeenCalledTimes(2);
  });

  it("edits the product in place and the totals line follows", () => {
    render(<Harness receiptTotal={25.48} />);

    expect(screen.getByText(/^Строки дают 25,39.* из 25,48.* · не хватает 0,09/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Сумма"), { target: { value: "6,98" } });
    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
    expect(screen.queryByText(/Проверьте по фото/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Товар 2" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Название"), { target: { value: "MLEKO 3,2%" } });
    expect(shownName()).toBe("MLEKO 3,2%");
    // A lone comma is not a number yet: the field keeps it, nothing changes.
    fireEvent.change(screen.getByLabelText("Цена"), { target: { value: "," } });
    expect(screen.getByLabelText("Цена")).toHaveValue(",");
    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
  });

  it("shows the quantity with its unit, and a product's discount in its card with what is left to pay; tapping it edits the discount", () => {
    render(<Harness start={1} />);
    expect(screen.getByLabelText("Кол-во, szt")).toHaveValue("2");
    const row = screen.getByRole("button", { name: /^Скидка −1,00.*PLN · к оплате 5,89.*PLN$/ });
    expect(row).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByLabelText("Размер скидки")).not.toBeInTheDocument();

    fireEvent.click(row);
    expect(row).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Название скидки")).toHaveValue("RABAT MLEKO");
    fireEvent.change(screen.getByLabelText("Размер скидки"), { target: { value: "1,5" } });
    expect(screen.getByRole("button", { name: /^Скидка −1,50.*PLN · к оплате 5,39.*PLN$/ })).toBeInTheDocument();
  });

  it("a discount without its amount opens by itself and says so", () => {
    render(<Harness start={1} lines={baseLines.map((line, index) => index === 2 ? { ...line, lineTotal: null } : line)} />);
    expect(screen.getByRole("button", { name: /^Скидка: сумма не распознана/ })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByLabelText("Размер скидки")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("button", { name: "Товар 2, нужно проверить" })).toBeInTheDocument();
  });

  it("«Убрать» in the draft removes the discount at once and «Вернуть» puts it back", () => {
    render(<Harness start={1} restorable />);

    fireEvent.click(screen.getByRole("button", { name: "Убрать скидку RABAT MLEKO" }));
    expect(screen.getByTestId("names")).toHaveTextContent("KAWA|MLEKO 2%|CHLEB");
    expect(position()).toBe("Товар 2 из 3");
    expect(screen.getByText("Скидка убрана")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Вернуть" }));
    expect(screen.getByTestId("names")).toHaveTextContent("KAWA|MLEKO 2%|RABAT MLEKO|CHLEB");
    expect(screen.getByRole("button", { name: /^Скидка −1,00/ })).toBeInTheDocument();
  });

  it("«Убрать» without an undo asks first", () => {
    render(<Harness start={1} />);

    fireEvent.click(screen.getByRole("button", { name: "Убрать скидку RABAT MLEKO" }));
    fireEvent.click(within(dialog("Убрать скидку?")).getByRole("button", { name: "Отмена" }));
    expect(screen.getByTestId("names")).toHaveTextContent("RABAT MLEKO");

    fireEvent.click(screen.getByRole("button", { name: "Убрать скидку RABAT MLEKO" }));
    fireEvent.click(within(dialog("Убрать скидку?")).getByRole("button", { name: "Убрать" }));
    expect(screen.getByTestId("names")).toHaveTextContent("KAWA|MLEKO 2%|CHLEB");
    expect(screen.queryByText("Скидка убрана")).not.toBeInTheDocument();
  });

  it("after «Убрать» and then deleting its product, the next product offers no «Вернуть»", () => {
    const lines: EditableLine[] = [baseLines[1], baseLines[2], baseLines[3]];
    render(<Harness start={0} restorable lines={lines} />);

    fireEvent.click(screen.getByRole("button", { name: "Убрать скидку RABAT MLEKO" }));
    expect(screen.getByText("Скидка убрана")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Удалить" }));
    fireEvent.click(within(dialog("Удалить товар 1?")).getByRole("button", { name: "Удалить" }));

    expect(shownName()).toBe("CHLEB");
    expect(screen.queryByText("Скидка убрана")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Вернуть" })).not.toBeInTheDocument();
  });

  it("while a field is typed in, the card keeps its discount even if it briefly exceeds the product", () => {
    render(<Harness start={0} lines={[baseLines[0], { ...baseLines[2], name: "RABAT KAWA" }]} receiptTotal={14} />);
    fireEvent.click(screen.getByRole("button", { name: /^Скидка −1,00/ }));
    const size = screen.getByLabelText("Размер скидки");

    fireEvent.focus(size);
    fireEvent.change(size, { target: { value: "20" } });
    // Larger than KAWA's 15: by the linking rule a receipt discount, but it stays in the card while typed.
    expect(position()).toBe("Товар 1 из 1");
    expect(screen.getByLabelText("Размер скидки")).toBe(size);

    fireEvent.change(size, { target: { value: "2" } });
    fireEvent.blur(size);
    expect(position()).toBe("Товар 1 из 1");
    expect(screen.getByRole("button", { name: /^Скидка −2,00/ })).toBeInTheDocument();
  });

  it("a discount on the whole receipt keeps a card of its own", () => {
    render(<Harness start={0} lines={[...baseLines, { name: "RABAT NA PARAGON", quantity: null, unit: null, unitPrice: null, lineTotal: -3, kind: "discount" }]} />);
    fireEvent.click(screen.getByRole("button", { name: "Скидка" }));
    expect(position()).toBe("Скидка на весь чек");
    expect(screen.getByLabelText("Название скидки")).toHaveValue("RABAT NA PARAGON");
    expect(screen.getByLabelText("Размер скидки")).toHaveValue("3");
    expect(screen.queryByLabelText("Цена")).not.toBeInTheDocument();
  });

  it("deletes a product only after confirmation", () => {
    render(<Harness start={3} />);

    fireEvent.click(screen.getByRole("button", { name: "Удалить" }));
    fireEvent.click(within(dialog("Удалить товар 3?")).getByRole("button", { name: "Отмена" }));
    expect(position()).toBe("Товар 3 из 3");

    fireEvent.click(screen.getByRole("button", { name: "Удалить" }));
    fireEvent.click(within(dialog("Удалить товар 3?")).getByRole("button", { name: "Удалить" }));
    expect(position()).toBe("Товар 2 из 2");
  });

  it("shows the whole photo with a note when the line has no place on it, and says when there is no photo", () => {
    const { unmount } = render(<Harness start={3} />);
    expect(screen.getByRole("img", { name: "Фото чека целиком" })).toHaveAttribute("src", "blob:receipt");
    expect(screen.getByText("Место строки не найдено — показан весь чек.")).toBeInTheDocument();
    unmount();

    render(<Harness start={0} shownPhoto={null} />);
    expect(screen.getByText("У этой покупки нет фото чека.")).toBeInTheDocument();
  });
});
