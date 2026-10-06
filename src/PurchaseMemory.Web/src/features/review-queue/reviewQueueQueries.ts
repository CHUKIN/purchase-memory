/** Prefix shared by the queue page and the navigation badge, so one invalidation refreshes both. */
export const reviewQueueQueryKey = ["review-queue"] as const;
export const reviewQueueCountQueryKey = ["review-queue", "count"] as const;
