import { fireEvent, render, screen, within } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { dialog } from "../../test/ionicHelpers";
import type { LineViewerPhoto } from "../receipt-photo/LinePhoto";
import type { EditableLine } from "../receipts/LinesEditor";
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

function Harness({ lines: initialLines = baseLines, receiptTotal = 25.48, start, shownPhoto = photo }: {
  lines?: EditableLine[];
  receiptTotal?: number | null;
  start?: number;
  shownPhoto?: LineViewerPhoto | null;
}) {
  const [lines, setLines] = useState(initialLines);
  const [index, setIndex] = useState(start ?? firstProblemIndex(initialLines));
  const [checked, setChecked] = useState<number[]>([]);
  return (
    <AppProviders locale="ru">
      <p data-testid="checked">{checked.join(",")}</p>
      <LineCarousel
        lines={lines}
        index={index}
        onIndexChange={setIndex}
        onChange={(position, patch) => setLines((current) => current.map((line, other) => other === position ? { ...line, ...patch } : line))}
        onRemove={(position) => {
          setLines((current) => current.filter((_, other) => other !== position));
          setIndex((current) => Math.max(0, Math.min(current, lines.length - 2)));
        }}
        isChecked={(position) => checked.includes(position)}
        onApprove={(position) => setChecked((current) => [...current, position])}
        onUnapprove={(position) => setChecked((current) => current.filter((other) => other !== position))}
        currency="PLN"
        receiptTotal={receiptTotal}
        photo={shownPhoto}
      />
    </AppProviders>
  );
}

const position = () => screen.getByText(/^Строка \d+ из \d+$/).textContent;
const shownName = () => (screen.getByLabelText(/^Название/) as HTMLInputElement).value;
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

describe("LineCarousel", () => {
  it("starts at the first line with a problem, explains the problem and marks it on its dot", () => {
    render(<Harness />);

    expect(position()).toBe("Строка 2 из 4");
    expect(shownName()).toBe("MLEKO 2%");
    expect(within(card()).getByText(/^2 × 3,49.*= 6,98.*а сумма 6,89.*Проверьте по фото\.$/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Строка 2, нужно проверить" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("button", { name: "Строка 1" })).not.toHaveAttribute("aria-current");
    // Every control has a name.
    for (const name of ["Предыдущая строка", "Следующая строка", "Удалить", "Верно, дальше", "Увеличить фото"]) {
      expect(screen.getByRole("button", { name })).toBeInTheDocument();
    }
  });

  it("starts at the first line when nothing is wrong", () => {
    render(<Harness lines={[baseLines[0], baseLines[3]]} receiptTotal={19.5} />);
    expect(position()).toBe("Строка 1 из 2");
    expect(screen.queryByText(/Проверьте по фото/)).not.toBeInTheDocument();
  });

  it("moves with the chevrons and the dots, and the chevrons stop at the ends", () => {
    render(<Harness start={0} />);

    expect(screen.getByRole("button", { name: "Предыдущая строка" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Следующая строка" }));
    expect(position()).toBe("Строка 2 из 4");
    fireEvent.click(screen.getByRole("button", { name: "Строка 4" }));
    expect(position()).toBe("Строка 4 из 4");
    expect(shownName()).toBe("CHLEB");
    expect(screen.getByRole("button", { name: "Следующая строка" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Предыдущая строка" }));
    expect(position()).toBe("Строка 3 из 4");
  });

  it("a swipe of 50 px or more on the card or the photo turns the line; shorter, vertical or from a field does nothing", () => {
    render(<Harness start={0} />);

    swipe(card(), -30);
    expect(position()).toBe("Строка 1 из 4");
    swipe(card(), -120, 200);
    expect(position()).toBe("Строка 1 из 4");
    swipe(screen.getByLabelText("Название"), -120);
    expect(position()).toBe("Строка 1 из 4");

    swipe(card(), -60);
    expect(position()).toBe("Строка 2 из 4");
    swipe(screen.getByRole("img", { name: /Фото чека, выделена строка/ }).closest(".pm-carousel-photo") as HTMLElement, -80);
    expect(position()).toBe("Строка 3 из 4");
    swipe(card(), 90);
    expect(position()).toBe("Строка 2 из 4");
  });

  it("a second finger (a pinch) is not a swipe", () => {
    render(<Harness start={0} />);
    const element = card();
    fireEvent.pointerDown(element, { pointerId: 1, clientX: 200, clientY: 300 });
    fireEvent.pointerDown(element, { pointerId: 2, clientX: 260, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 1, clientX: 100, clientY: 300 });
    fireEvent.pointerUp(element, { pointerId: 2, clientX: 300, clientY: 300 });
    expect(position()).toBe("Строка 1 из 4");
  });

  it("«Верно, дальше» checks the line and moves on; «Проверено» takes the mark back", () => {
    render(<Harness start={0} />);

    fireEvent.click(screen.getByRole("button", { name: "Верно, дальше" }));
    expect(screen.getByTestId("checked")).toHaveTextContent("0");
    expect(position()).toBe("Строка 2 из 4");
    expect(screen.getByRole("button", { name: "Строка 1, проверена" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Строка 1, проверена" }));
    fireEvent.click(screen.getByRole("button", { name: "Снять отметку «Проверено»" }));
    expect(screen.getByTestId("checked")).toHaveTextContent("");
    expect(screen.getByRole("button", { name: "Верно, дальше" })).toBeInTheDocument();
  });

  it("on the last line the button is just «Верно» and the line stays", () => {
    render(<Harness start={3} />);
    fireEvent.click(screen.getByRole("button", { name: "Верно" }));
    expect(position()).toBe("Строка 4 из 4");
    expect(screen.getByRole("button", { name: "Снять отметку «Проверено»" })).toBeInTheDocument();
  });

  it("edits the line in place and the totals line follows", () => {
    render(<Harness receiptTotal={25.48} />);

    expect(screen.getByText(/^Строки дают 25,39.* из 25,48.* · не хватает 0,09/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Сумма"), { target: { value: "6,98" } });
    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
    expect(screen.queryByText(/Проверьте по фото/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Строка 2" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Название"), { target: { value: "MLEKO 3,2%" } });
    expect(shownName()).toBe("MLEKO 3,2%");
    // A lone comma is not a number yet: the field keeps it, nothing changes.
    fireEvent.change(screen.getByLabelText("Цена"), { target: { value: "," } });
    expect(screen.getByLabelText("Цена")).toHaveValue(",");
    expect(screen.getByText("Сумма строк сходится с итогом чека")).toBeInTheDocument();
  });

  it("shows the quantity with its unit and a discount with its amount only", () => {
    render(<Harness start={0} />);
    expect(screen.getByLabelText("Кол-во, szt")).toHaveValue("1");
    fireEvent.click(screen.getByRole("button", { name: "Строка 3" }));
    expect(screen.getByLabelText("Название скидки")).toHaveValue("RABAT MLEKO");
    expect(screen.getByLabelText("Размер скидки")).toHaveValue("1");
    expect(screen.getByText("Скидка к «MLEKO 2%»")).toBeInTheDocument();
    expect(screen.queryByLabelText("Цена")).not.toBeInTheDocument();
  });

  it("deletes a line only after confirmation", () => {
    render(<Harness start={3} />);

    fireEvent.click(screen.getByRole("button", { name: "Удалить" }));
    fireEvent.click(within(dialog("Удалить строку 4?")).getByRole("button", { name: "Отмена" }));
    expect(position()).toBe("Строка 4 из 4");

    fireEvent.click(screen.getByRole("button", { name: "Удалить" }));
    fireEvent.click(within(dialog("Удалить строку 4?")).getByRole("button", { name: "Удалить" }));
    expect(position()).toBe("Строка 3 из 3");
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
