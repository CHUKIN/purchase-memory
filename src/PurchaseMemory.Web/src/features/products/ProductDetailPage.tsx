import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PackageSearch } from "lucide-react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useParams } from "react-router";
import {
  ApiError,
  classifyProduct,
  confirmProductNames,
  createTag,
  getProduct,
  listCategories,
  listTags,
  setProductName,
  setProductTags,
  suggestProductNames,
  suggestProductTags,
  type ClassifyProductRequest
} from "../../api/client";
import type { ProductDetail, ProductNameLanguage } from "../../api/types";
import { ButtonLink, EmptyState, LoadingList, Notice, Page } from "../../design-system/components";
import { brandsQueryKey, categoriesQueryKey } from "../categories/categoryQueries";
import { ProductImpressionsSection } from "../impressions/ImpressionSections";
import { tagsQueryKey } from "../tags/tagQueries";
import { ProductCard } from "./ProductCard";
import { displayName } from "./productNameText";
import { ProductMediaSection } from "../product-scan/ProductMediaSection";
import { useBackTarget } from "../../app/backNavigation";
import { useToast } from "../../app/toasts";
import { useConfirmDistinct, useMergeProduct, type MergeSide } from "./productMerge";
import { reviewQueueQueryKey } from "../review-queue/reviewQueueQueries";

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
  // Names show in lists, the purchase lines and the «Ревью» hub too.
  const refreshNames = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: ["products"] }),
    queryClient.invalidateQueries({ queryKey: ["purchases"] }),
    queryClient.invalidateQueries({ queryKey: reviewQueueQueryKey })
  ]);
  const setName = useMutation({
    mutationFn: ({ language, name }: { language: ProductNameLanguage; name: string }) => setProductName(productId, language, name),
    onSuccess: refreshNames
  });
  // A 409 (names changed since shown) also refreshes, so the person sees the new ones.
  const confirmNames = useMutation({
    mutationFn: () => {
      const shown = productQuery.data;
      return confirmProductNames(productId, { namePl: shown?.namePl ?? null, nameRu: shown?.nameRu ?? null });
    },
    onSettled: refreshNames
  });
  const suggestNames = useMutation({ mutationFn: () => suggestProductNames(productId), onSuccess: refreshNames });
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
  const back = useBackTarget({ to: "/products", labelKey: "common.products" });
  const navigate = useNavigate();
  const location = useLocation();
  const showToast = useToast();
  const merge = useMergeProduct();
  const confirmDistinct = useConfirmDistinct();
  const reviewActions = {
    onMergeInto: async (target: MergeSide) => {
      const current = productQuery.data;
      if (!current) return;
      await merge.mutateAsync({ source: { id: current.id, name: current.name }, target });
      showToast({ id: `product-merged-${Date.now()}`, message: t("products.merged", { name: target.name }), tone: "success" });
      // This product is gone: its page is replaced by the target's, so «Назад»
      // never lands on a «not found», and goes where this page would have gone.
      navigate(`/products/${encodeURIComponent(target.id)}`, { replace: true, state: location.state as unknown });
    },
    onConfirmDistinct: () => confirmDistinct.mutateAsync(productId)
  };

  if (productQuery.data) {
    const product = productQuery.data;
    return (
      <ProductCard
        product={productQuery.data}
        locale={locale}
        back={back}
        categories={categoriesQuery.data}
        onClassify={classify.mutateAsync}
        allTags={tagsQuery.data}
        onSetTags={saveTags.mutateAsync}
        onCreateTag={createTagForProduct}
        onSuggestTags={() => suggestProductTags(productId, i18n.resolvedLanguage === "en" ? "en" : "ru")}
        readCurrentTagIds={() => queryClient
          .getQueryData<ProductDetail>(["products", "detail", productId])?.tags?.map((tag) => tag.id)}
        tagsFailed={tagsQuery.isError}
        reviewActions={reviewActions}
        onSetName={(language, name) => setName.mutateAsync({ language, name })}
        onConfirmNames={confirmNames.mutateAsync}
        onSuggestNames={suggestNames.mutateAsync}
        media={(intro) => (
          <ProductMediaSection productId={product.id} productName={displayName(product)} intro={intro} locale={locale} />
        )}
      >
        <ProductImpressionsSection productId={productQuery.data.id} locale={locale} />
      </ProductCard>
    );
  }

  return (
    <Page title={t("products.eyebrow")} back={back}>
      {productQuery.isPending && <LoadingList label={t("products.detailLoading")} rows={3} />}
      {notFound && (
        <EmptyState
          icon={<PackageSearch />}
          title={t("products.notFoundTitle")}
          description={t("products.notFoundDescription")}
          action={<ButtonLink to="/products" fill="clear">{t("products.backToCatalog")}</ButtonLink>}
        />
      )}
      {productQuery.isError && !notFound && <Notice tone="danger" role="alert"><p>{t("products.detailLoadError")}</p></Notice>}
    </Page>
  );
}
