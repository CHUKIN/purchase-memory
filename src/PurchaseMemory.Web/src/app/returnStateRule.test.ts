import { describe, expect, it } from "vitest";

/*
 * The rule behind «Назад everywhere» (see app/backNavigation.ts and place.ts):
 * every link or `navigate()` to a screen that is not a tab root passes the
 * opening screen's `useReturnState()` as router `state`, so the opened screen
 * can name and reopen where the person came from. This reads the source
 * and fails when a new link forgets it. A deliberate exception goes into
 * `allowed`, with the reason. A target that is not a literal (`to={link.to}`,
 * `navigate(to)`) is flagged too, since it can point anywhere: it needs state
 * or an allow-list entry. Not caught: a state object that is passed but is
 * not `useReturnState()` (review reads those), and routes built in other
 * file types.
 */
const sources = import.meta.glob<string>("../**/*.{ts,tsx}", { query: "?raw", import: "default", eager: true });

/** Routes that are not tab roots. `/purchases` and `/products` bare are tab roots; their children are not. */
const detailRoute = /^\/(products\/|purchases\/|receipts\/|scan|needs-attention|help)/;

/** `file :: first characters of the target` → why no return state is needed. */
const allowed: Record<string, string> = {
  "features/receipts/CapturePage.tsx :: `/receipts/${result.receipt.id}": "the capture screen is a one-off flow: from the receipt «Назад» goes Home, not into the camera",
  "app/SettingsSheet.tsx :: helpPath()": "the help screen has its own back button («Назад» through history)",
  "features/help/HelpLink.tsx :: helpPath(topic)": "same: help goes back through history",
  "features/help/HelpWelcomeCard.tsx :: helpPath()": "same: help goes back through history",
  "app/AppShell/AppShell.tsx :: item.to": "the tab bar opens tab roots, which have no back button",
  "design-system/components/Links/Links.tsx :: target.historyIndex": "this is the back button itself",
  "features/purchases/PurchaseDetailPage.tsx :: queueReturnPath": "after saving, back to the queue address the purchase was opened from",
  "features/receipts/ReceiptDetailPage.tsx :: afterSavePath": "after saving, on to the screen that is next, not a detail screen",
};

const files = Object.entries(sources)
  .map(([path, text]) => [path.startsWith("./") ? `app/${path.slice(2)}` : path.replace(/^\.\.\//, ""), text] as const)
  .filter(([path]) => !/\.(test|stories)\.tsx?$/.test(path) && !path.startsWith("test/") && !path.includes("-prototype/") && !path.startsWith("scanner-lab/"));

interface Finding { file: string; target: string; call: string }

function findLinks(file: string, text: string): Finding[] {
  const found: Finding[] = [];
  // An opening tag ends at the first `>` that is not the arrow of `=>`.
  for (const match of text.matchAll(/<(ItemLink|ButtonLink|Link)\b([\s\S]*?)(?<!=)>/g)) {
    const attributes = match[2];
    const to = /\bto=(?:"([^"]*)"|\{([\s\S]*?)\}(?=\s|\/|$))/.exec(attributes);
    if (!to) continue;
    const expression = to[1] ?? to[2] ?? "";
    const literal = to[1] !== undefined || /^\s*[`"'/]/.test(expression);
    const target = expression.replace(/^\s*[`"']/, "");
    if ((literal && !detailRoute.test(target)) || /\bstate=/.test(attributes)) continue;
    found.push({ file, target: expression.trim().slice(0, 60), call: match[0].slice(0, 80) });
  }
  return found;
}

function findNavigates(file: string, text: string): Finding[] {
  const found: Finding[] = [];
  for (const match of text.matchAll(/\bnavigate\(\s*([`"'])(\/[^`"']*)\1?([^)]*)\)/g)) {
    const target = match[2];
    if (!detailRoute.test(target) || /\bstate\b/.test(match[0])) continue;
    found.push({ file, target: `${match[1]}${target}`.slice(0, 60), call: match[0].slice(0, 80) });
  }
  // `navigate(to)`, `navigate(path, …)`: a target held in a variable.
  for (const match of text.matchAll(/\bnavigate\(\s*([A-Za-z_][\w.]*(?:\([^()]*\))?)((?:[^()]|\([^()]*\))*)\)/g)) {
    if (/\bstate\b/.test(match[0])) continue;
    found.push({ file, target: match[1].slice(0, 60), call: match[0].slice(0, 80) });
  }
  return found;
}

const isAllowed = ({ file, target }: Finding) =>
  Object.keys(allowed).some((key) => {
    const [allowedFile, allowedTarget] = key.split(" :: ");
    return allowedFile === file && target.startsWith(allowedTarget);
  });

describe("return state rule", () => {
  it("scans the source files", () => {
    expect(files.length).toBeGreaterThan(40);
  });

  it("every link to a detail screen passes router state", () => {
    const missing = files.flatMap(([file, text]) => findLinks(file, text)).filter((finding) => !isAllowed(finding));
    expect(missing).toEqual([]);
  });

  it("every navigate() to a detail screen passes router state", () => {
    const missing = files.flatMap(([file, text]) => findNavigates(file, text)).filter((finding) => !isAllowed(finding));
    expect(missing).toEqual([]);
  });

  it("notices a forgotten state (the rule itself works)", () => {
    const bad = '<ItemLink to={`/products/${id}`} lines="none"><p>x</p></ItemLink>\n<Link to="/purchases/p1" onClick={() => go()}>y</Link>';
    expect(findLinks("x.tsx", bad)).toHaveLength(2);
    const good = '<ItemLink to={`/products/${id}`} state={returnState}><p>x</p></ItemLink>\n<Link to="/purchases" >all</Link>';
    expect(findLinks("x.tsx", good)).toHaveLength(0);
    expect(findNavigates("x.tsx", "navigate(`/products/${id}`);")).toHaveLength(1);
    expect(findNavigates("x.tsx", "navigate(`/products/${id}`, { state: returnState });")).toHaveLength(0);
    // A target held in a variable can point anywhere: flagged until it carries state.
    expect(findLinks("x.tsx", "<ItemLink key={link.to} to={link.to}><b/></ItemLink>")).toHaveLength(1);
    expect(findLinks("x.tsx", "<ItemLink key={link.to} to={link.to} state={returnState}><b/></ItemLink>")).toHaveLength(0);
    expect(findNavigates("x.tsx", "navigate(to);")).toHaveLength(1);
    expect(findNavigates("x.tsx", "navigate(to, { state: returnState });")).toHaveLength(0);
    expect(findNavigates("x.tsx", "navigate(-1);")).toHaveLength(0);
  });

  it("keeps the allow-list honest: every entry still matches a link in its file", () => {
    const all = files.flatMap(([file, text]) => [...findLinks(file, text), ...findNavigates(file, text)]);
    const unused = Object.keys(allowed).filter((key) => {
      const [file, target] = key.split(" :: ");
      return !all.some((finding) => finding.file === file && finding.target.startsWith(target)) && !files.some(([path, text]) => path === file && text.includes(target.replace(/^[`"']/, "")));
    });
    expect(unused).toEqual([]);
  });
});
