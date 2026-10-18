import type { BoundingBox, TextPolygon } from "../../api/types";

/**
 * Story data only (no app code imports it): a stand-in receipt photo printed
 * a few degrees askew, like a real phone shot, and the word outlines the
 * server would send for its rows. Storybook must not depend on a running API
 * or on owner photos.
 */
const WIDTH = 420;
const HEIGHT = 900;
const ANGLE = -4;
const CENTRE = { x: WIDTH / 2, y: HEIGHT / 2 };
const CHAR = 0.6; // monospace advance, in font sizes

interface Row {
  text: string;
  x: number;
  baseline: number;
  size: number;
}

const rows: Record<string, Row> = {
  shop: { text: "BIEDRONKA", x: 30, baseline: 90, size: 26 },
  milk: { text: "MLEKO 3,2%     4,49", x: 30, baseline: 200, size: 22 },
  bread: { text: "CHLEB ZYTNI    6,99", x: 30, baseline: 260, size: 22 },
  butter: { text: "MASLO EXTRA   17,98", x: 30, baseline: 320, size: 22 },
  discount: { text: "  OPUST       -2,00", x: 30, baseline: 370, size: 22 },
  goudaName: { text: "SER GOUDA PLASTRY", x: 30, baseline: 430, size: 16 },
  goudaPrice: { text: "  MLECZARNIA 250G   9,99", x: 30, baseline: 452, size: 16 },
  total: { text: "SUMA PLN     37,45", x: 30, baseline: 760, size: 28 }
};

export const tiltedReceiptUrl =
  "data:image/svg+xml;utf8," +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}">
       <rect width="${WIDTH}" height="${HEIGHT}" fill="#e9e6df"/>
       <g transform="rotate(${ANGLE} ${CENTRE.x} ${CENTRE.y})">
         <rect x="14" y="30" width="392" height="800" fill="#f8f6f0"/>
         ${Object.values(rows)
           .map((row) => `<text x="${row.x}" y="${row.baseline}" font-family="monospace" font-size="${row.size}" xml:space="preserve">${row.text}</text>`)
           .join("")}
       </g>
     </svg>`
  );

function turn(x: number, y: number): [number, number] {
  const radians = (ANGLE * Math.PI) / 180;
  const dx = x - CENTRE.x;
  const dy = y - CENTRE.y;
  return [
    (CENTRE.x + dx * Math.cos(radians) - dy * Math.sin(radians)) / WIDTH,
    (CENTRE.y + dx * Math.sin(radians) + dy * Math.cos(radians)) / HEIGHT
  ];
}

/** One padded, tilted rectangle from character `from` to `to` (exclusive) of a row. */
function span(row: Row, from: number, to: number): TextPolygon {
  const height = row.size * 1.05;
  const left = row.x + from * CHAR * row.size - 0.3 * height;
  const right = row.x + to * CHAR * row.size + 0.3 * height;
  const top = row.baseline - 0.8 * row.size - 0.1 * height;
  const bottom = row.baseline + 0.25 * row.size + 0.1 * height;
  return [...turn(left, top), ...turn(right, top), ...turn(right, bottom), ...turn(left, bottom)];
}

/** Like the server: every pair of neighbouring words joined into one outline. */
function rowOutlines(row: Row): TextPolygon[] {
  const words = [...row.text.matchAll(/\S+/g)].map((match) => ({ from: match.index, to: match.index + match[0].length }));
  if (words.length === 1) return [span(row, words[0].from, words[0].to)];
  return words.slice(1).map((word, index) => span(row, words[index].from, word.to));
}

/** The outlines of one or more printed rows (a name row plus its price row, for example). */
export function storyOutlines(...names: (keyof typeof rows)[]): TextPolygon[] {
  return names.flatMap((name) => rowOutlines(rows[name]));
}

/** Where recognition would have put the row: a level band (today's fallback frame). */
export function storyBox(name: keyof typeof rows, height = 34): BoundingBox {
  return { x: 0.05, y: (rows[name].baseline - 24) / HEIGHT, width: 0.88, height: height / HEIGHT };
}
