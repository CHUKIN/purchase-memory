import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { getPurchaseFeed, listBrands, listCategories, listTags } from "../../api/client";
import type { PurchaseReviewFilter } from "../../api/types";
import { brandsQueryKey, categoriesQueryKey } from "../categories/categoryQueries";
import { knownCategoryFilter, knownTagFilter } from "../filters/categoryFilter";
import { tagsQueryKey } from "../tags/tagQueries";
import { useDebounced } from "../filters/useDebounced";
import { useUrlFilters } from "../filters/useUrlFilters";
import { PurchaseFeed, emptyPurchaseFeedFilters, type PurchaseFeedFilters } from "./PurchaseFeed";

const pageSize = 20;
const statuses: PurchaseReviewFilter[] = ["all", "needsAttention", "reviewed"];

// A hand-edited or stale link must not put the page into a state the controls cannot show.
const sanitize = (filters: PurchaseFeedFilters): PurchaseFeedFilters => ({
  ...filters,
  status: statuses.includes(filters.status) ? filters.status : "all"
});

/**
 * Connects the presentational feed to `GET /api/purchases`. Filters live in
 * the URL (`query`, `from`, `to`, `status`, `category`, `brand`, `tag`). The free-text
 * and brand fields are debounced; dates, status, category and tag are deliberate
 * single actions and should feel immediate.
 */
export function PurchaseFeedSection() {
  const { t } = useTranslation();
  const [filters, setFilters] = useUrlFilters(emptyPurchaseFeedFilters, sanitize);
  const debouncedQuery = useDebounced(filters.query.trim());
  const debouncedBrand = useDebounced(filters.brand.trim());

  // The feed works without the tree and the brand list; it only loses those filters.
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const brandsQuery = useQuery({ queryKey: brandsQueryKey, queryFn: listBrands });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  const category = knownCategoryFilter(filters.category, categoriesQuery.data);
  // Without the tag list a tag from the link cannot be checked; a stale one
  // would turn the whole page into an error, so the filter is dropped instead.
  const tag = tagsQuery.isError ? "" : knownTagFilter(filters.tag, tagsQuery.data);

  const feedQuery = useInfiniteQuery({
    queryKey: ["purchases", "feed", debouncedQuery, filters.from, filters.to, filters.status, category, debouncedBrand, tag],
    initialPageParam: 1,
    queryFn: ({ pageParam }) => getPurchaseFeed({
      query: debouncedQuery,
      from: filters.from || undefined,
      to: filters.to || undefined,
      status: filters.status,
      category: category || undefined,
      brand: debouncedBrand || undefined,
      tag: tag || undefined,
      page: pageParam,
      pageSize
    }),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
    // A tag from a link waits for the tag list, so a deleted tag is dropped
    // before the first request instead of costing a 400.
    enabled: filters.tag === "" || !tagsQuery.isPending
  });

  const pages = feedQuery.data?.pages ?? [];
  const purchases = pages.flatMap((page) => page.items);
  const lastPage = pages.at(-1);

  return (
    <PurchaseFeed
      filters={{ ...filters, category, tag }}
      onFiltersChange={setFilters}
      categories={categoriesQuery.data ?? []}
      brands={brandsQuery.data ?? []}
      tags={tagsQuery.data ?? []}
      purchases={purchases}
      totalCount={lastPage?.totalCount ?? 0}
      loading={feedQuery.isPending}
      error={feedQuery.isError ? t("feed.loadError") : null}
      hasMore={feedQuery.hasNextPage}
      loadingMore={feedQuery.isFetchingNextPage}
      onLoadMore={() => void feedQuery.fetchNextPage()}
      truncated={lastPage?.truncated ?? false}
    />
  );
}
