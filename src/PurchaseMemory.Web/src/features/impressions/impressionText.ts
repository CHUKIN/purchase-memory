import type { TFunction } from "i18next";
import type { Impression, ImpressionRating } from "../../api/types";

export const ratings: readonly ImpressionRating[] = ["dislike", "neutral", "like"];

export const impressionsQueryKey = ["impressions"] as const;

export function ratingLabel(t: TFunction, rating: ImpressionRating): string {
  return t(`impressions.rating.${rating}`);
}

/** «Вы» for the viewer, otherwise the member's email (or a neutral word when there is none). */
export function authorLabel(t: TFunction, impression: Pick<Impression, "isMine" | "authorEmail">): string {
  if (impression.isMine) return t("impressions.you");
  return impression.authorEmail ?? t("impressions.someone");
}

export interface AuthorHistory {
  authorUserId: string;
  label: string;
  isMine: boolean;
  /** Newest first, as the API returns them. */
  impressions: Impression[];
}

/** One group per household member, the viewer first, then by most recent opinion. */
export function groupByAuthor(t: TFunction, impressions: Impression[]): AuthorHistory[] {
  const groups = new Map<string, AuthorHistory>();
  for (const impression of impressions) {
    let group = groups.get(impression.authorUserId);
    if (!group) {
      group = { authorUserId: impression.authorUserId, label: authorLabel(t, impression), isMine: impression.isMine, impressions: [] };
      groups.set(impression.authorUserId, group);
    }
    group.impressions.push(impression);
  }
  return [...groups.values()].sort((left, right) => Number(right.isMine) - Number(left.isMine));
}

export interface StoreOpinion {
  store: string | null;
  counts: Record<ImpressionRating, number>;
  /** Each member's latest rating in this store. */
  latestByAuthor: { authorUserId: string; label: string; rating: ImpressionRating }[];
}

/**
 * Opinions per store, so «in Lidl we liked it, in Biedronka not» is visible at
 * a glance. Stores are ordered by how many impressions they have.
 */
export function compareByStore(t: TFunction, impressions: Impression[]): StoreOpinion[] {
  const stores = new Map<string, StoreOpinion>();
  for (const impression of impressions) {
    const key = impression.merchantName?.trim() || "";
    let store = stores.get(key);
    if (!store) {
      store = { store: key || null, counts: { dislike: 0, neutral: 0, like: 0 }, latestByAuthor: [] };
      stores.set(key, store);
    }
    store.counts[impression.rating] += 1;
    // Input is newest first, so the first impression seen per author is the latest.
    if (!store.latestByAuthor.some((entry) => entry.authorUserId === impression.authorUserId)) {
      store.latestByAuthor.push({ authorUserId: impression.authorUserId, label: authorLabel(t, impression), rating: impression.rating });
    }
  }
  const total = (store: StoreOpinion) => store.counts.dislike + store.counts.neutral + store.counts.like;
  return [...stores.values()].sort((left, right) => total(right) - total(left));
}
