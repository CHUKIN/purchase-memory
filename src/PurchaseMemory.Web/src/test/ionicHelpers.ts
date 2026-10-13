import { fireEvent, screen, within } from "@testing-library/react";

/*
 * Small steps the Ionic screens need in tests (with src/test/ionicShim.tsx):
 * a picker row opens a dialog with the options, a date row opens a calendar
 * sheet, filters live in a sheet behind the toolbar button.
 */

const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/** A list row whose visible text starts with `label` (a picker or date row: «Категория Все категории»). */
export function row(label: string): HTMLElement {
  return screen.getByRole("button", { name: new RegExp(`^${escape(label)}(\\s|$)`) });
}

export async function findRow(label: string): Promise<HTMLElement> {
  return screen.findByRole("button", { name: new RegExp(`^${escape(label)}(\\s|$)`) });
}

/** The open dialog (sheet, picker, action sheet or alert) with this name, or the last one open. */
export function dialog(name?: string): HTMLElement {
  const dialogs = [...screen.queryAllByRole("dialog"), ...screen.queryAllByRole("alertdialog")];
  if (name) {
    const named = dialogs.filter((element) => element.getAttribute("aria-label") === name || within(element).queryByRole("heading", { name }) !== null);
    if (named.length > 0) return named[named.length - 1];
  }
  if (dialogs.length === 0) throw new Error(`No dialog is open${name ? ` (wanted «${name}»)` : ""}`);
  return dialogs[dialogs.length - 1];
}

/** Opens a picker row and chooses the option whose label is `option` (its grey detail may follow). */
export function pick(label: string, option: string) {
  fireEvent.click(row(label));
  const picker = dialog();
  fireEvent.click(within(picker).getByRole("button", { name: new RegExp(`^${escape(option)}(\\s.*)?$`) }));
}

/** The options a picker row offers, in order, as their visible labels. */
export function pickerOptions(label: string): string[] {
  fireEvent.click(row(label));
  const picker = dialog();
  const options = within(picker).getAllByRole("button").filter((button) => button.textContent !== "Отмена" && button.textContent !== "Cancel");
  const labels = options.map((button) => Array.from(button.children).map((child) => child.textContent ?? "").filter(Boolean).join(" "));
  fireEvent.click(within(picker).getByRole("button", { name: /^(Отмена|Cancel)$/ }));
  return labels;
}

/** Opens a date row, types the value into its calendar and closes the sheet. */
export function setDate(label: string, value: string) {
  fireEvent.click(row(label));
  const sheet = dialog(label);
  fireEvent.change(within(sheet).getByLabelText(label), { target: { value } });
  fireEvent.click(within(sheet).getByRole("button", { name: /^(Готово|Done)$/ }));
}

/** The toolbar «Фильтры» button (its name carries the count when some are set). */
export function openFilters() {
  fireEvent.click(screen.getByRole("button", { name: /^(Фильтры|Filters)/ }));
  return dialog();
}

/** Taps a button in the open action sheet or alert. */
export function chooseAction(name: string | RegExp) {
  fireEvent.click(within(dialog()).getByRole("button", { name }));
}
