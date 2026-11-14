/** One key holds the ids of the ticked items. Ticks never leave this device. */
export const testChecklistStorageKey = "purchase-memory.test-checklist";

// Storage can be missing or throw (private mode, blocked site data); the page
// then works for this visit only.
export function readChecked(): string[] {
  try {
    const raw = window.localStorage.getItem(testChecklistStorageKey);
    const parsed: unknown = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === "string") : [];
  } catch {
    return [];
  }
}

export function writeChecked(ids: string[]) {
  try {
    window.localStorage.setItem(testChecklistStorageKey, JSON.stringify(ids));
  } catch {
    // Nothing to do: the ticks just last for this visit.
  }
}
