/** Shared cache key: the product page, the catalog filter and the manager read the same tree. */
export const categoriesQueryKey = ["categories"] as const;

/** Brand list for the feed and catalog filters; refreshed when a brand is changed. */
export const brandsQueryKey = ["brands"] as const;
