import { discountLinks, isDiscount } from "../receipts/discountLines";
import type { EditableLine } from "../receipts/LinesEditor";

/**
 * One card of the line carousel. A product is a card with the discounts that
 * reduce it inside; a discount that belongs to no product (one on the whole
 * receipt, or one before any product) keeps a card of its own, in receipt
 * order, so it stays reachable.
 */
export interface CarouselStep {
  /** The line the card shows (its position in the lines). */
  position: number;
  kind: "product" | "discount";
  /** Positions of the discounts shown inside a product's card. */
  discounts: number[];
  /** For a discount card: it is on the whole receipt by design. */
  appliesToReceipt: boolean;
}

export function carouselSteps(lines: EditableLine[]): CarouselStep[] {
  const links = discountLinks(lines.map((line) => ({ kind: line.kind ?? "product", name: line.name, lineTotal: line.lineTotal })));
  const steps: CarouselStep[] = [];
  const byPosition = new Map<number, CarouselStep>();
  lines.forEach((line, position) => {
    const link = links[position];
    if (isDiscount({ kind: line.kind ?? "product" }) && link.target !== null && byPosition.has(link.target)) {
      byPosition.get(link.target)!.discounts.push(position);
      return;
    }
    const step: CarouselStep = {
      position,
      kind: isDiscount({ kind: line.kind ?? "product" }) ? "discount" : "product",
      discounts: [],
      appliesToReceipt: link.appliesToReceipt
    };
    steps.push(step);
    byPosition.set(position, step);
  });
  return steps;
}

/** The card a line is on: its own, or its product's for a discount inside one. 0 for an unknown line. */
export function stepIndexOf(steps: CarouselStep[], position: number): number {
  const index = steps.findIndex((step) => step.position === position || step.discounts.includes(position));
  return index < 0 ? 0 : index;
}

/** The line a card shows for `position`: the product for a discount inside its card, else the line itself. */
export function stepLinePosition(lines: EditableLine[], position: number): number {
  const steps = carouselSteps(lines);
  return steps.length === 0 ? 0 : steps[stepIndexOf(steps, position)].position;
}

/** «Товар N»: how many product cards there are up to and including the one for `position`. */
export function productNumberOf(lines: EditableLine[], position: number): number {
  const steps = carouselSteps(lines);
  return steps.slice(0, stepIndexOf(steps, position) + 1).filter((step) => step.kind === "product").length;
}
