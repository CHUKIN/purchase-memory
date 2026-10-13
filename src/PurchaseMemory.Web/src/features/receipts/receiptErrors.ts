import type { TFunction } from "i18next";
import { ApiError } from "../../api/client";

/** A failed receipt request as one sentence; a rate limit says when to try again. */
export function messageOf(t: TFunction, error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.status === 429) {
    return t("auth.rateLimited", { minutes: Math.max(1, Math.ceil((error.retryAfterSeconds ?? 60) / 60)) });
  }
  return error instanceof Error ? error.message : fallback;
}
