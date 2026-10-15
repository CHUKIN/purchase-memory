/**
 * A compact date for list rows: «24 авг., 19:12». The year is added only
 * when it is not the current one, so recent rows stay one short phrase.
 */
export function formatShortDate(value: string | Date, locale: string, { time = false, now = new Date() }: { time?: boolean; now?: Date } = {}): string {
  const date = value instanceof Date ? value : new Date(value);
  const options: Intl.DateTimeFormatOptions = { day: "numeric", month: "short" };
  if (date.getFullYear() !== now.getFullYear()) options.year = "numeric";
  if (time) Object.assign(options, { hour: "2-digit", minute: "2-digit" });
  return new Intl.DateTimeFormat(locale, options).format(date);
}
