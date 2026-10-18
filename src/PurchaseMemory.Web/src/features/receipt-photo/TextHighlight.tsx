import { useId } from "react";
import type { BoundingBox, TextPolygon } from "../../api/types";
import { imageOffset } from "./photoBox";

/** Height of the drawing in SVG units; the width follows the photo's proportions. */
const UNITS = 1000;

/**
 * The "combo" highlight (owner's choice, plan #1 stage 1.3): the line's printed
 * words filled pale yellow (multiplied over the ink), a thin brown outline
 * around their union and the rest of the photo dimmed lightly. It covers the
 * whole photo, placed like the photo itself, so it zooms and pans with it.
 *
 * Overlapping outlines merge through masks: the fill is one rectangle seen
 * through all outlines, and the edge is every outline stroked twice as thick
 * as wanted with the inside masked away, which leaves only the outer contour.
 */
export function TextHighlight({ shapes, view, photoRatio }: { shapes: TextPolygon[]; view: BoundingBox; photoRatio: number }) {
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, "");
  const width = UNITS * photoRatio;
  const points = shapes.map((shape) => toPoints(shape, width));
  const inside = `${id}-inside`;
  const outside = `${id}-outside`;

  return (
    <svg
      className="pm-text-highlight"
      viewBox={`0 0 ${width} ${UNITS}`}
      preserveAspectRatio="none"
      style={{ ...imageOffset(view), height: `${100 / view.height}%` }}
      aria-hidden="true"
      focusable="false"
    >
      <defs>
        <mask id={inside} maskUnits="userSpaceOnUse" x="0" y="0" width={width} height={UNITS}>
          {points.map((value, index) => <polygon key={index} points={value} fill="#fff" />)}
        </mask>
        <mask id={outside} maskUnits="userSpaceOnUse" x="0" y="0" width={width} height={UNITS}>
          <rect width={width} height={UNITS} fill="#fff" />
          {points.map((value, index) => <polygon key={index} points={value} fill="#000" />)}
        </mask>
      </defs>
      <rect className="pm-text-highlight-dim" width={width} height={UNITS} mask={`url(#${outside})`} />
      <rect className="pm-text-highlight-fill" width={width} height={UNITS} mask={`url(#${inside})`} />
      <g mask={`url(#${outside})`}>
        {points.map((value, index) => <polygon key={index} className="pm-text-highlight-edge" points={value} />)}
      </g>
    </svg>
  );
}

function toPoints(shape: TextPolygon, width: number) {
  const pairs: string[] = [];
  for (let index = 0; index + 1 < shape.length; index += 2) {
    pairs.push(`${round(shape[index] * width)},${round(shape[index + 1] * UNITS)}`);
  }
  return pairs.join(" ");
}

const round = (value: number) => Math.round(value * 10) / 10;
