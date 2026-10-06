import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PackageSearch } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import {
  ApiError,
  classifyProduct,
  createCategory,
  createTag,
  deleteCategory,
  deleteTag,
  getProduct,
  listCategories,
  listTags,
  renameCategory,
  renameTag,
  setProductTags,
  type ClassifyProductRequest
} from "../../api/client";
import { CategoryManager } from "../categories/CategoryManager";
import { brandsQueryKey, categoriesQueryKey } from "../categories/categoryQueries";
import { EmptyState, SkeletonCard, Surface } from "../../design-system/components";
import { ProductImpressionsSection } from "../impressions/ImpressionSections";
import { TagManager } from "../tags/TagManager";
import { tagsQueryKey } from "../tags/tagQueries";
import { ProductCatalogSection } from "./ProductCatalogSection";
import { ProductCard } from "./ProductCard";
import { HelpLink } from "../help/HelpLink";
import styles from "./ProductsPage.module.css";

export function ProductsPage() {
  const { t } = useTranslation();

  return (
    <div className={styles.page}>
      <header className={styles.hero}>
        <p className={styles.eyebrow}>{t("products.eyebrow")}</p>
        <h1>{t("products.pageTitle")}</h1>
        <p className={styles.intro}>{t("products.pageIntro")}</p>
        <HelpLink topic="products" />
      </header>
      <ProductCatalogSection />
      <CategoryManagerSection />
      <TagManagerSection />
    </div>
  );
}

function TagManagerSection() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  // Renames and deletions change chips and filter results everywhere.
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: tagsQueryKey }),
    queryClient.invalidateQueries({ queryKey: ["products"] }),
    queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] })
  ]);

  if (tagsQuery.isError) {
    return <Surface className={styles.notice} role="alert">{t("tags.loadError")}</Surface>;
  }

  if (!tagsQuery.data) return null;

  return (
    <TagManager
      tags={tagsQuery.data}
      onCreate={(name) => createTag(name).then(refresh)}
      onRename={(tagId, name) => renameTag(tagId, name).then(refresh)}
      onDelete={(tagId) => deleteTag(tagId).then(refresh)}
    />
  );
}

function CategoryManagerSection() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  // Renames and deletions change labels and product placement everywhere.
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
    queryClient.invalidateQueries({ queryKey: ["products"] })
  ]);

  if (categoriesQuery.isError) {
    return <Surface className={styles.notice} role="alert">{t("categories.loadError")}</Surface>;
  }

  if (!categoriesQuery.data) return null;

  return (
    <CategoryManager
      categories={categoriesQuery.data}
      onCreate={(name, parentId) => createCategory(name, parentId).then(refresh)}
      onRename={(categoryId, name) => renameCategory(categoryId, name).then(refresh)}
      onDelete={(categoryId) => deleteCategory(categoryId).then(refresh)}
    />
  );
}

export function ProductDetailPage() {
  const { t, i18n } = useTranslation();
  const { productId = "" } = useParams();
  const locale = i18n.resolvedLanguage === "en" ? "en-GB" : "ru-RU";

  const productQuery = useQuery({
    queryKey: ["products", "detail", productId],
    queryFn: () => getProduct(productId),
    // A stale link is an answer, not a network problem, so it must not be retried
    // into a spinner that never resolves.
    retry: (failureCount, error) => !(error instanceof ApiError && error.status === 404) && failureCount < 2
  });

  const notFound = productQuery.error instanceof ApiError && productQuery.error.status === 404;
  const queryClient = useQueryClient();
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const classify = useMutation({
    mutationFn: (change: ClassifyProductRequest) => classifyProduct(productId, change),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: ["products"] }),
      queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
      // A brand change reshapes the filter list and which purchases match.
      queryClient.invalidateQueries({ queryKey: brandsQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] })
    ])
  });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  const saveTags = useMutation({
    mutationFn: (tagIds: string[]) => setProductTags(productId, tagIds),
    onSuccess: () => Promise.all([
      queryClient.invalidateQueries({ queryKey: ["products"] }),
      queryClient.invalidateQueries({ queryKey: tagsQueryKey }),
      queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] })
    ])
  });
  const createTagForProduct = async (name: string) => {
    const tag = await createTag(name);
    await queryClient.invalidateQueries({ queryKey: tagsQueryKey });
    return tag;
  };

  return (
    <div className={styles.page}>
      {productQuery.isPending && (
        <div aria-busy="true">
          <span className="visually-hidden">{t("products.detailLoading")}</span>
          <div aria-hidden="true"><SkeletonCard /></div>
        </div>
      )}

      {notFound && (
        <Surface className={styles.surface}>
          <EmptyState
            icon={<PackageSearch />}
            title={t("products.notFoundTitle")}
            description={t("products.notFoundDescription")}
            headingLevel={1}
            action={<Link className={styles.homeLink} to="/products">{t("products.backToCatalog")}</Link>}
          />
        </Surface>
      )}

      {productQuery.isError && !notFound && (
        <Surface className={styles.notice} role="alert">{t("products.detailLoadError")}</Surface>
      )}

      {productQuery.data && (
        <ProductCard
          product={productQuery.data}
          locale={locale}
          categories={categoriesQuery.data}
          onClassify={classify.mutateAsync}
          allTags={tagsQuery.data}
          onSetTags={saveTags.mutateAsync}
          onCreateTag={createTagForProduct}
        />
      )}

      {productQuery.data && tagsQuery.isError && (
        <Surface className={styles.notice} role="alert">{t("tags.loadError")}</Surface>
      )}

      {productQuery.data && <HelpLink topic="categories" />}

      {productQuery.data && <ProductImpressionsSection productId={productQuery.data.id} locale={locale} />}
    </div>
  );
}
