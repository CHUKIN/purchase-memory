import { IonItem, IonLabel, IonList } from "@ionic/react";
import { CircleAlert, GitMerge } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { ProductCategory, ProductDetail } from "../../api/types";
import { ActionButton, Footnote, Notice, SectionHeader } from "../../design-system/components";
import { useReturnState } from "../../app/backNavigation";
import { categoryPath } from "../categories/categoryText";
import { HelpLink } from "../help/HelpLink";
import { CatalogSearchModal } from "./CatalogSearchModal";
import { MergeConfirmAlert, type MergeRequest, type MergeSide } from "./productMerge";

export interface ProductReviewActions {
  /** Merges this product into `target`; the page then shows the target. */
  onMergeInto: (target: MergeSide) => Promise<unknown>;
  /** «Это другой товар». */
  onConfirmDistinct: () => Promise<unknown>;
}

/**
 * The «проверить» banner of a flagged product: what it might really be, each
 * with «Да, это он» (merge into it, after a confirmation naming both), and
 * «Это другой товар».
 */
export function ProductReviewBanner({ product, categories, actions }: {
  product: ProductDetail;
  categories: ProductCategory[];
  actions: ProductReviewActions;
}) {
  const { t } = useTranslation();
  const returnState = useReturnState();
  const [request, setRequest] = useState<MergeRequest | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  if (!product.needsReview) return null;
  const candidates = product.reviewCandidates ?? [];
  const self: MergeSide = { id: product.id, name: product.name };

  async function run(key: string, action: () => Promise<unknown>) {
    setBusy(key);
    setError(null);
    try {
      await action();
    } catch {
      setError(t("products.reviewActionFailed"));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section aria-labelledby="product-review-title">
      <SectionHeader id="product-review-title" action={<HelpLink topic="products" />}>{t("products.reviewTitle")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem>
          <CircleAlert slot="start" size={22} className="pm-row-icon pm-warning-icon" aria-hidden="true" />
          <IonLabel className="pm-wrap">
            <p>{candidates.length > 0 ? t("products.reviewText") : t("products.reviewTextNoCandidates")}</p>
          </IonLabel>
        </IonItem>
        {candidates.map((candidate, index) => {
          const details = [
            candidate.brand,
            categoryPath(t, categories, candidate.categoryId),
            t("products.purchaseCount", { count: candidate.purchaseCount })
          ].filter(Boolean).join(" · ");
          return (
            <IonItem key={candidate.productId}>
              <IonLabel className="pm-wrap">
                {index === 0 && <p className="pm-overline" aria-hidden="true">{t("reviewQueue.suggestionsLabel")}</p>}
                <h3><Link className="pm-text-link" to={`/products/${candidate.productId}`} state={returnState}>{candidate.name}</Link></h3>
                <p>{details}</p>
              </IonLabel>
              <ActionButton
                slot="end"
                size="small"
                className="pm-tap-button"
                fill={index === 0 ? "solid" : "outline"}
                loading={busy === candidate.productId}
                disabled={busy !== null && busy !== candidate.productId}
                onClick={() => setRequest({ source: self, target: { id: candidate.productId, name: candidate.name } })}
                aria-label={t("reviewQueue.acceptCandidateFor", { name: product.name, product: candidate.name })}
              >
                {t("reviewQueue.acceptCandidate")}
              </ActionButton>
            </IonItem>
          );
        })}
        <IonItem
          button
          detail={false}
          lines="none"
          disabled={busy !== null}
          onClick={() => void run("distinct", actions.onConfirmDistinct)}
          aria-label={t("reviewQueue.distinctProductFor", { name: product.name })}
        >
          <IonLabel color="primary">{t("reviewQueue.distinctProduct")}</IonLabel>
        </IonItem>
      </IonList>
      {error && <Notice tone="danger" role="alert"><p>{error}</p></Notice>}
      <MergeConfirmAlert
        request={request}
        onCancel={() => setRequest(null)}
        onConfirm={(confirmed) => void run(confirmed.target.id, () => actions.onMergeInto(confirmed.target))}
      />
    </section>
  );
}

/**
 * «Объединить с…» for any product: find the other product in the catalog
 * (this one left out), confirm, merge. There is no undo; help says so.
 */
export function MergeWithSection({ product, actions }: { product: ProductDetail; actions: ProductReviewActions }) {
  const { t } = useTranslation();
  const [pickerOpen, setPickerOpen] = useState(false);
  const [request, setRequest] = useState<MergeRequest | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function merge(confirmed: MergeRequest) {
    setBusy(true);
    setError(null);
    try {
      await actions.onMergeInto(confirmed.target);
    } catch {
      setError(t("products.reviewActionFailed"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-label={t("products.mergeWith")}>
      <IonList inset>
        <IonItem button detail lines="none" disabled={busy} onClick={() => setPickerOpen(true)}>
          <GitMerge slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("products.mergeWith")}</IonLabel>
        </IonItem>
      </IonList>
      <Footnote>{t("products.mergeWithNote")}</Footnote>
      {error && <Notice tone="danger" role="alert"><p>{error}</p></Notice>}
      <CatalogSearchModal
        isOpen={pickerOpen}
        onClose={() => setPickerOpen(false)}
        title={t("products.mergeWithTitle")}
        subject={t("products.mergeWithSubject", { name: product.name })}
        excludeProductId={product.id}
        chooseLabel={(name) => t("products.mergeWithChoose", { name })}
        onChoose={(target) => {
          setPickerOpen(false);
          setRequest({ source: { id: product.id, name: product.name }, target: { id: target.id, name: target.name } });
        }}
      />
      <MergeConfirmAlert request={request} onCancel={() => setRequest(null)} onConfirm={(confirmed) => void merge(confirmed)} />
    </section>
  );
}
