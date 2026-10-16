import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { getProductCatalog, listBrands, listCategories, listTags } from "../../api/client";
import { brandsQueryKey, categoriesQueryKey } from "../categories/categoryQueries";
import { knownCategoryFilter, knownTagFilter } from "../filters/categoryFilter";
import { tagsQueryKey } from "../tags/tagQueries";
import { useDebounced } from "../filters/useDebounced";
import { useUrlFilters } from "../filters/useUrlFilters";
import { ProductCatalog } from "./ProductCatalog";

const pageSize = 30;

interface CatalogFilters {
  query: string;
  category: string;
  brand: string;
  tag: string;
  /** `"1"` shows only products flagged «проверить». */
  review: string;
}

const emptyCatalogFilters: CatalogFilters = { query: "", category: "", brand: "", tag: "", review: "" };

/**
 * Connects the catalog to `GET /api/products`. Filters live in the URL
 * (`query`, `category`, `brand`, `tag`, `review`) exactly like the purchase
 * feed, and the text fields are debounced so typing does not fire a request
 * per keystroke.
 */
export function ProductCatalogSection({ footer }: { footer?: ReactNode }) {
  const { t } = useTranslation();
  const [filters, setFilters] = useUrlFilters(emptyCatalogFilters);
  const debouncedQuery = useDebounced(filters.query.trim());
  const debouncedBrand = useDebounced(filters.brand.trim());

  // The catalog works without the tree; it only loses the chips and the filter.
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const brandsQuery = useQuery({ queryKey: brandsQueryKey, queryFn: listBrands });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  const category = knownCategoryFilter(filters.category, categoriesQuery.data);
  // Without the tag list a tag from the link cannot be checked; a stale one
  // would turn the whole page into an error, so the filter is dropped instead.
  const tag = tagsQuery.isError ? "" : knownTagFilter(filters.tag, tagsQuery.data);
  const review = filters.review === "1";

  const catalogQuery = useInfiniteQuery({
    queryKey: ["products", "catalog", debouncedQuery, category, debouncedBrand, tag, review],
    initialPageParam: 1,
    queryFn: ({ pageParam }) =>
      getProductCatalog({ query: debouncedQuery, category, brand: debouncedBrand, tag, needsReview: review || undefined, page: pageParam, pageSize }),
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.page + 1 : undefined),
    // A tag from a link waits for the tag list, so a deleted tag is dropped
    // before the first request instead of costing a 400.
    enabled: filters.tag === "" || !tagsQuery.isPending
  });

  const pages = catalogQuery.data?.pages ?? [];
  const lastPage = pages.at(-1);

  return (
    <ProductCatalog
      query={filters.query}
      onQueryChange={(query) => setFilters({ query })}
      categories={categoriesQuery.data ?? []}
      categoryFilter={category}
      onCategoryFilterChange={(value) => setFilters({ category: value })}
      brandFilter={filters.brand}
      onBrandFilterChange={(brand) => setFilters({ brand })}
      brands={brandsQuery.data ?? []}
      tags={tagsQuery.data ?? []}
      tagFilter={tag}
      onTagFilterChange={(value) => setFilters({ tag: value })}
      reviewFilter={review}
      onReviewFilterChange={(value) => setFilters({ review: value ? "1" : "" })}
      products={pages.flatMap((page) => page.items)}
      totalCount={lastPage?.totalCount ?? 0}
      needsReviewCount={lastPage?.needsReviewCount ?? 0}
      loading={catalogQuery.isPending}
      error={catalogQuery.isError ? t("products.loadError") : null}
      hasMore={catalogQuery.hasNextPage}
      loadingMore={catalogQuery.isFetchingNextPage}
      onLoadMore={() => void catalogQuery.fetchNextPage()}
      footer={footer}
    />
  );
}
