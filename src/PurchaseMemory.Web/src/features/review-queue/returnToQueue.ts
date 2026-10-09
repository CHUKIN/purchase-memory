/**
 * A receipt opened from «Нужно проверить» goes back there after saving, with
 * the queue's filters as they were. The address travels in router state
 * (`<Link state>`), so the receipt URL stays clean and shareable; anything
 * that is not a queue address is ignored and the caller keeps its default.
 */
export const reviewQueuePath = "/needs-attention";

export interface ReviewQueueReturnState {
  returnTo: string;
}

export function reviewQueueReturnState(search: string): ReviewQueueReturnState {
  return { returnTo: `${reviewQueuePath}${search}` };
}

export function reviewQueueReturnPath(state: unknown): string | null {
  if (typeof state !== "object" || state === null || !("returnTo" in state)) return null;
  const returnTo = (state as { returnTo: unknown }).returnTo;
  if (typeof returnTo !== "string") return null;
  return returnTo === reviewQueuePath || returnTo.startsWith(`${reviewQueuePath}?`) ? returnTo : null;
}
