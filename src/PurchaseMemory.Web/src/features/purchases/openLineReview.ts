import { useState } from "react";
import { useLocation } from "react-router";
import { loadPlace, savePlace } from "../../app/place";

/**
 * Router state flag: open the purchase straight into its line review
 * («Проверить строки» in «Ревью»). It acts once per history entry: the entry's
 * place is noted when it opens, so coming back to the purchase later (from a
 * product, say) does not reopen a review the person already closed; a line
 * remembered on the way out still reopens through the place (see place.ts).
 */
export const openLineReviewKey = "openLineReview";

export function wantsLineReview(state: unknown): boolean {
  return typeof state === "object" && state !== null && (state as Record<string, unknown>)[openLineReviewKey] === true;
}

/** True only on the first arrival at this entry with the flag set. */
export function useOpenLineReviewOnArrival(): boolean {
  const location = useLocation();
  const [open] = useState(() => {
    const path = `${location.pathname}${location.search}`;
    if (!wantsLineReview(location.state) || loadPlace(location.key, path) !== null) return false;
    savePlace(location.key, path, {});
    return true;
  });
  return open;
}
