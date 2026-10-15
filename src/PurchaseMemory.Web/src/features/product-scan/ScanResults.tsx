import type { TFunction } from "i18next";
import { IonButton, IonItem, IonLabel, IonList, IonSearchbar, IonSpinner } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { Bookmark, PackagePlus, Link2, Meh, ScanBarcode, Store, ThumbsDown, ThumbsUp, TrendingDown } from "lucide-react";
import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, getProduct, getProductCatalog, listProductImpressions } from "../../api/client";
import type { ImpressionRating, ProductDetail, ProductPricePoint } from "../../api/types";
import { Footnote, Notice, SectionHeader } from "../../design-system/components";
import { useDebounced } from "../filters/useDebounced";
import { impressionsQueryKey, ratingLabel } from "../impressions/impressionText";
import { formatDate, formatMoney } from "../products/productText";
import { formatBarcode } from "./barcode";
import { lookupOpenFoodFacts } from "./openFoodFacts";
import { AvoidBanner } from "./ProductMediaSection";
import { ProductThumb } from "./ProductPhoto";
import { useLinkBarcode, useProductMedia, useRememberBarcode, useRememberedBarcode } from "./useProductMedia";
import "./productScan.css";

/* ---------- a known barcode ---------- */

export function FoundResult({ productId, code, locale, onOpenProduct, onScanAgain }: {
  productId: string;
  code: string;
  locale: string;
  onOpenProduct: () => void;
  onScanAgain: () => void;
}) {
  const { t } = useTranslation();
  const product = useQuery({
    queryKey: ["products", "detail", productId],
    queryFn: () => getProduct(productId),
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 2
  });
  const impressions = useQuery({ queryKey: [...impressionsQueryKey, "product", productId], queryFn: () => listProductImpressions(productId) });
  const media = useProductMedia(productId);

  if (product.isPending) return <Searching code={code} />;
  if (product.isError || !product.data) {
    const gone = product.error instanceof ApiError && product.error.status === 404;
    return (
      <>
        <Notice tone="danger" role="alert"><p>{gone ? t("productScan.productGone") : t("productScan.productLoadFailed")}</p></Notice>
        <ResultButtons onScanAgain={onScanAgain} />
      </>
    );
  }

  const detail = product.data;
  const mine = impressions.data?.find((impression) => impression.isMine) ?? null;
  const prices = priceFacts(detail);

  return (
    <>
      <div className="pm-scan-found-head">
        <ProductThumb productId={productId} slot="" />
        <div>
          <h3>{detail.name}</h3>
          <p>{[detail.brand, formatBarcode(code)].filter(Boolean).join(" · ")}</p>
        </div>
      </div>
      {media.data?.avoid && <AvoidBanner avoid={media.data.avoid} locale={locale} />}
      <IonList inset>
        <IonItem>
          <Store slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel className="pm-wrap">
            <p>{t("productScan.lastPurchase")}</p>
            <h3>{prices.last ? pointText(t, locale, prices.currency, prices.last) : t("productScan.noPurchases")}</h3>
          </IonLabel>
        </IonItem>
        {prices.cheapest && (
          <IonItem>
            <TrendingDown slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            <IonLabel className="pm-wrap">
              <p>{t("productScan.cheapest")}</p>
              <h3>{pointText(t, locale, prices.currency, prices.cheapest)}</h3>
            </IonLabel>
          </IonItem>
        )}
        <IonItem lines="none">
          <RatingIcon rating={mine?.rating ?? null} />
          <IonLabel className="pm-wrap">
            <p>{t("productScan.yourRating")}</p>
            <h3>{mine ? ratingLabel(t, mine.rating) : impressions.isPending ? "…" : t("productScan.noRating")}</h3>
            {mine?.note && <p>«{mine.note}»</p>}
          </IonLabel>
        </IonItem>
      </IonList>
      <ResultButtons onScanAgain={onScanAgain}>
        <IonButton expand="block" shape="round" onClick={onOpenProduct}>{t("productScan.openProduct")}</IonButton>
      </ResultButtons>
    </>
  );
}

function RatingIcon({ rating }: { rating: ImpressionRating | null }) {
  if (rating === "like") return <ThumbsUp slot="start" size={22} className="pm-row-icon" aria-hidden="true" />;
  if (rating === "dislike") return <ThumbsDown slot="start" size={22} className="pm-row-icon pm-danger-icon" aria-hidden="true" />;
  return <Meh slot="start" size={22} className="pm-row-icon pm-muted-icon" aria-hidden="true" />;
}

/** Last purchase and the cheapest one, in the unit the product is usually bought in. */
function priceFacts(product: ProductDetail): { currency: string | null; last: ProductPricePoint | null; cheapest: ProductPricePoint | null } {
  const series = product.priceSeries[0];
  if (!series) return { currency: null, last: null, cheapest: null };
  const cheapest = series.points.reduce<ProductPricePoint | null>((best, point) => (
    best === null || point.unitPrice < best.unitPrice || (point.unitPrice === best.unitPrice && point.purchasedAt > best.purchasedAt) ? point : best
  ), null);
  return {
    currency: series.currency,
    last: series.latestPoint,
    // Bought once, or never cheaper than last time: the second row would repeat the first.
    cheapest: cheapest && cheapest.unitPrice < series.latestPoint.unitPrice ? cheapest : null
  };
}

function pointText(t: TFunction, locale: string, currency: string | null, point: ProductPricePoint): string {
  return [formatDate(locale, point.purchasedAt), point.merchantName ?? t("products.unknownStore"), formatMoney(locale, currency, point.unitPrice)].join(" · ");
}

/* ---------- an unknown barcode ---------- */

export function NotFoundResult({ code, locale, onLinked, onScanAgain }: {
  code: string;
  locale: string;
  /** The code now belongs to this product; the screen shows it as found. */
  onLinked: (productId: string) => void;
  onScanAgain: () => void;
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState("");
  const debounced = useDebounced(query).trim();
  const [message, setMessage] = useState<{ tone: "success" | "danger"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const link = useLinkBarcode();
  const remember = useRememberBarcode();
  const remembered = useRememberedBarcode(code);
  const off = useQuery({
    queryKey: ["open-food-facts", code],
    queryFn: ({ signal }) => lookupOpenFoodFacts(code, signal),
    staleTime: Infinity,
    retry: false
  });
  const search = useQuery({
    queryKey: ["products", "catalog", "scan-link", debounced],
    queryFn: () => getProductCatalog({ query: debounced, pageSize: 8 }),
    enabled: debounced.length >= 2
  });
  const offProduct = off.data?.status === "found" ? off.data.product : null;
  const found = search.data?.items ?? [];

  async function linkTo(productId: string) {
    setBusy(true);
    setMessage(null);
    try {
      const result = await link(productId, code, "scan");
      if (result.ok) onLinked(productId);
      else setMessage({ tone: "danger", text: t("productScan.linkTaken") });
    } catch {
      setMessage({ tone: "danger", text: t("productScan.saveFailed") });
    } finally {
      setBusy(false);
    }
  }

  async function rememberCode(withOff: boolean) {
    setBusy(true);
    setMessage(null);
    try {
      await remember(code, withOff ? offProduct : null);
      setMessage({ tone: "success", text: withOff ? t("productScan.offCreated") : t("productScan.remembered") });
    } catch {
      setMessage({ tone: "danger", text: t("productScan.saveFailed") });
    } finally {
      setBusy(false);
    }
  }

  const rememberedAt = remembered.data?.rememberedAt;

  return (
    <>
      <p className="pm-home-intro">
        {t("productScan.unknownCode")} <span className="pm-media-code-text">{formatBarcode(code)}</span>
      </p>
      {rememberedAt !== undefined && (
        <Footnote role="status">
          {t("productScan.alreadyRemembered", { date: new Intl.DateTimeFormat(locale, { day: "numeric", month: "long" }).format(new Date(rememberedAt)) })}
          {remembered.data?.off?.name ? ` ${[remembered.data.off.brand, remembered.data.off.name].filter(Boolean).join(" · ")}` : ""}
        </Footnote>
      )}
      {message && <Notice tone={message.tone} role={message.tone === "danger" ? "alert" : "status"}><p>{message.text}</p></Notice>}

      <SectionHeader>{t("productScan.linkTitle")}</SectionHeader>
      <IonSearchbar
        value={query}
        placeholder={t("productScan.linkSearchPlaceholder")}
        aria-label={t("productScan.linkSearchLabel")}
        onIonInput={(event) => setQuery(event.detail.value ?? "")}
      />
      {debounced.length < 2 && <Footnote>{t("productScan.linkSearchHint")}</Footnote>}
      {search.isError && <Notice tone="danger" role="alert"><p>{t("productScan.searchFailed")}</p></Notice>}
      {search.isFetching && !search.data && <div className="pm-scan-spin"><IonSpinner name="crescent" aria-label={t("ui.loading")} /></div>}
      {search.data && found.length === 0 && <Footnote>{t("ui.nothingFound")}</Footnote>}
      {found.length > 0 && (
        <IonList inset>
          {found.map((product, index) => (
            <IonItem
              key={product.id}
              button
              detail={false}
              disabled={busy}
              lines={index === found.length - 1 ? "none" : undefined}
              onClick={() => void linkTo(product.id)}
            >
              <ProductThumbOrIcon productId={product.id} />
              <IonLabel className="pm-wrap">
                <h3>{product.name}</h3>
                <p>{[product.brand, t("products.purchaseCount", { count: product.purchaseCount })].filter(Boolean).join(" · ")}</p>
              </IonLabel>
              <span slot="end" className="pm-primary-text" aria-hidden="true">{t("productScan.link")}</span>
              <span className="visually-hidden">{t("productScan.linkTo", { name: product.name })}</span>
            </IonItem>
          ))}
        </IonList>
      )}

      <SectionHeader>{t("productScan.orNew")}</SectionHeader>
      <IonList inset>
        <IonItem button={offProduct !== null} detail={offProduct !== null} disabled={busy || offProduct === null} onClick={offProduct ? () => void rememberCode(true) : undefined}>
          {offProduct?.imageUrl
            ? <img slot="start" className="pm-scan-off-image" src={offProduct.imageUrl} alt="" referrerPolicy="no-referrer" />
            : <PackagePlus slot="start" size={22} className="pm-row-icon" aria-hidden="true" />}
          <IonLabel className="pm-wrap">
            <h3>{t("productScan.offCreate")}</h3>
            <p>
              {off.isPending ? t("productScan.offLoading")
                : offProduct ? [offProduct.name, offProduct.brand, offProduct.quantity].filter(Boolean).join(" · ")
                : off.data?.status === "error" ? t("productScan.offError")
                : t("productScan.offNotFound")}
            </p>
          </IonLabel>
        </IonItem>
        <IonItem button detail lines="none" disabled={busy} onClick={() => void rememberCode(false)}>
          <Bookmark slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel className="pm-wrap">
            <h3>{t("productScan.rememberLater")}</h3>
            <p>{t("productScan.rememberLaterNote")}</p>
          </IonLabel>
        </IonItem>
      </IonList>
      <Footnote>{t("productScan.offHint")}</Footnote>
      <ResultButtons onScanAgain={onScanAgain} />
    </>
  );
}

function ProductThumbOrIcon({ productId }: { productId: string }) {
  const media = useProductMedia(productId);
  return media.data?.mainPhotoId
    ? <ProductThumb productId={productId} />
    : <Link2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" />;
}

/* ---------- shared ---------- */

export function Searching({ code }: { code: string }) {
  const { t } = useTranslation();
  return (
    <div className="pm-scan-spin" role="status" aria-live="polite">
      <IonSpinner name="crescent" aria-hidden="true" />
      <span>{t("productScan.searching", { code: formatBarcode(code) })}</span>
      <span style={{ fontSize: 13 }}>{t("productScan.serverWaking")}</span>
    </div>
  );
}

function ResultButtons({ onScanAgain, children }: { onScanAgain: () => void; children?: ReactNode }) {
  const { t } = useTranslation();
  return (
    <div className="pm-scan-buttons">
      {children}
      <IonButton expand="block" shape="round" fill="clear" onClick={onScanAgain}>
        <ScanBarcode size={20} aria-hidden="true" />{t("productScan.scanAgain")}
      </IonButton>
    </div>
  );
}
