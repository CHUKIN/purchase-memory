import type { TFunction } from "i18next";
import { IonButton, IonItem, IonLabel, IonList, IonSearchbar, IonSpinner } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Camera, Link2, PackagePlus, RotateCw } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, createProduct, findProductsByPhoto, getPhotoSearchStatus, getProductCatalog } from "../../api/client";
import type { PhotoMatchConfidence, PhotoSearchMatch } from "../../api/types";
import { Footnote, Notice, SectionHeader, Sheet } from "../../design-system/components";
import { useDebounced } from "../filters/useDebounced";
import { HelpLink } from "../help/HelpLink";
import { formatDate } from "../products/productText";
import { displayName, russianName } from "../products/productNameText";
import { PhotoDecodeError, photoForNaming } from "./compressImage";
import { NewProductForm } from "./NewProductForm";
import { ProductThumb } from "./ProductPhoto";
import { FoundResult, Searching } from "./ScanResults";
import "./productScan.css";

export const photoSearchStatusQueryKey = ["find-by-photo", "status"] as const;

/**
 * A photo search takes a few seconds even on a warm server (the model looks
 * at the photo); only after this long does the «server is waking up» note show.
 */
export const slowPhotoSearchMs = 10_000;

/** Whether to show «Найти по фото»: the server's switch. Any failure hides the button. */
export function usePhotoSearchEnabled(): boolean {
  const status = useQuery({
    queryKey: photoSearchStatusQueryKey,
    queryFn: getPhotoSearchStatus,
    staleTime: 5 * 60_000,
    retry: false
  });
  return status.data?.enabled === true;
}

/** One picked photo; `id` changes with every new photo, so each is searched once. */
export interface PickedPhoto {
  id: number;
  blob: Blob;
}

/**
 * «Найти по фото»: the photo goes to the server once (shrunk like «Название
 * по фото», ~200 KB) and comes back as up to three of the household's own
 * products. The person confirms with «Это он», which opens the same summary
 * as a barcode scan; «Нет в списке» leads to search by name or a new product.
 */
export function PhotoSearchSheet({ photo, locale, onClose, onRetake, onOpenProduct }: {
  /** The picked photo; the sheet is open while it is set. */
  photo: PickedPhoto | null;
  locale: string;
  onClose: () => void;
  /** «Сфотографировать ещё раз»: opens the camera again. */
  onRetake: () => void;
  onOpenProduct: (productId: string) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  // What the person chose for this photo; reset when another photo comes.
  const [choice, setChoice] = useState<{ photoId: number; chosen: string | null; notListed: boolean } | null>(null);
  const current = choice !== null && choice.photoId === photo?.id ? choice : null;
  const chosen = current?.chosen ?? null;
  const notListed = current?.notListed ?? false;
  const setChosen = (productId: string | null) => photo && setChoice({ photoId: photo.id, chosen: productId, notListed });
  const setNotListed = (value: boolean) => photo && setChoice({ photoId: photo.id, chosen: null, notListed: value });

  // A mutation, not a query: nothing (a focus, a session refresh that
  // invalidates every query) may repeat a paid search on its own. Only a new
  // photo or «Попробовать ещё раз» sends one.
  const search = useMutation({
    mutationFn: async (blob: Blob) => findProductsByPhoto(await photoForNaming(blob)),
    onError: (error) => {
      if (error instanceof ApiError && error.code === "findByPhotoDisabled") {
        // Switched off meanwhile: the button disappears on the next look.
        void queryClient.invalidateQueries({ queryKey: photoSearchStatusQueryKey });
      }
    }
  });
  const { mutate } = search;
  // Once per photo, also when React runs effects twice.
  const searchedFor = useRef<number | null>(null);
  useEffect(() => {
    if (!photo || searchedFor.current === photo.id) return;
    searchedFor.current = photo.id;
    mutate(photo.blob);
  }, [photo, mutate]);
  const state = search.isSuccess && searchedFor.current === photo?.id ? { status: "done" as const, result: search.data }
    : search.isError ? { status: "failed" as const, ...failure(t, search.error) }
    : { status: "searching" as const };

  const matches = state.status === "done" ? state.result.matches : [];
  const title = chosen ? t("productScan.resultFound")
    : state.status === "searching" ? t("productScan.photoSearchTitle")
    : state.status === "failed" ? t("productScan.resultFailed")
    : notListed || matches.length === 0 ? t("productScan.photoNotListedTitle")
    : t("productScan.photoMatchesTitle");

  let body;
  if (chosen) {
    body = (
      <>
        <IonButton fill="clear" size="small" className="pm-photo-back" onClick={() => setChosen(null)}>
          <ArrowLeft size={18} aria-hidden="true" />&nbsp;{t("productScan.photoBackToMatches")}
        </IonButton>
        <FoundResult productId={chosen} code={null} locale={locale} onOpenProduct={() => onOpenProduct(chosen)} onScanAgain={onClose} />
      </>
    );
  } else if (state.status === "searching") {
    body = <Searching code={null} label={t("productScan.photoSearching")} slowAfterMs={slowPhotoSearchMs} />;
  } else if (state.status === "failed") {
    body = (
      <>
        <Notice tone="danger" role="alert"><p>{state.message}</p></Notice>
        <div className="pm-scan-buttons">
          {state.retry && (
            <IonButton expand="block" shape="round" onClick={() => photo && mutate(photo.blob)}>
              <RotateCw size={20} aria-hidden="true" />&nbsp;{t("productScan.retry")}
            </IonButton>
          )}
          <IonButton expand="block" shape="round" fill="outline" onClick={onRetake}>
            <Camera size={20} aria-hidden="true" />&nbsp;{t("productScan.photoRetake")}
          </IonButton>
        </div>
        <NotListed onPick={setChosen} onCreated={onOpenProduct} />
      </>
    );
  } else if (notListed || matches.length === 0) {
    body = (
      <>
        {matches.length === 0 && <p className="pm-home-intro">{t("productScan.photoNoMatch")}</p>}
        {matches.length > 0 && (
          <IonButton fill="clear" size="small" className="pm-photo-back" onClick={() => setNotListed(false)}>
            <ArrowLeft size={18} aria-hidden="true" />&nbsp;{t("productScan.photoBackToMatches")}
          </IonButton>
        )}
        <NotListed onPick={setChosen} onCreated={onOpenProduct} />
        <div className="pm-scan-buttons">
          <IonButton expand="block" shape="round" fill="clear" onClick={onRetake}>
            <Camera size={20} aria-hidden="true" />&nbsp;{t("productScan.photoRetake")}
          </IonButton>
        </div>
      </>
    );
  } else {
    body = (
      <>
        <p className="pm-home-intro">{t("productScan.photoMatchesIntro")}</p>
        <IonList inset>
          {matches.map((match, index) => (
            <IonItem key={match.productId} lines={index === matches.length - 1 ? "none" : undefined}>
              <ProductThumb photoId={match.mainPhotoId} placeholder />
              <IonLabel className="pm-wrap">
                <h3>{displayName(match)}</h3>
                {russianName(match) && <p className="pm-product-name-ru" lang="ru">{russianName(match)}</p>}
                <p>{matchFacts(t, locale, match)}</p>
              </IonLabel>
              <IonButton
                slot="end"
                shape="round"
                size="small"
                aria-label={t("productScan.photoThisOneLabel", { name: displayName(match) })}
                onClick={() => setChosen(match.productId)}
              >
                {t("productScan.photoThisOne")}
              </IonButton>
            </IonItem>
          ))}
        </IonList>
        <Footnote>{t("productScan.photoApproximate")}</Footnote>
        <div className="pm-scan-buttons">
          <IonButton expand="block" shape="round" fill="outline" onClick={() => setNotListed(true)}>{t("productScan.photoNotListed")}</IonButton>
          <IonButton expand="block" shape="round" fill="clear" onClick={onRetake}>
            <Camera size={20} aria-hidden="true" />&nbsp;{t("productScan.photoRetake")}
          </IonButton>
        </div>
      </>
    );
  }

  return (
    <Sheet
      isOpen={photo !== null}
      onClose={onClose}
      title={title}
      help={<HelpLink topic="find-by-photo" />}
      end={<IonButton strong onClick={onClose}>{t("ui.close")}</IonButton>}
      initialBreakpoint={0.75}
    >
      {body}
    </Sheet>
  );
}

/** «Нет в списке»: the person's own search by name, or a new product. */
function NotListed({ onPick, onCreated }: {
  /** A product was found by name: show its summary. */
  onPick: (productId: string) => void;
  /** A new product (or the existing namesake) was made: open its card. */
  onCreated: (productId: string) => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [query, setQuery] = useState("");
  const debounced = useDebounced(query).trim();
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const search = useQuery({
    queryKey: ["products", "catalog", "photo-search", debounced],
    queryFn: () => getProductCatalog({ query: debounced, pageSize: 8 }),
    enabled: debounced.length >= 2
  });
  const found = search.data?.items ?? [];

  async function create(name: string, brand: string) {
    setBusy(true);
    setError(null);
    try {
      const product = await createProduct({ name, brand: brand.trim() || null, origin: "manual" });
      await queryClient.invalidateQueries({ queryKey: ["products"] });
      onCreated(product.id);
    } catch (failure) {
      // A product with this name already exists: that is the one.
      if (failure instanceof ApiError && failure.status === 409 && failure.existingProductId) {
        onCreated(failure.existingProductId);
        return;
      }
      setError(failure instanceof ApiError && failure.status === 400 ? failure.message : t("productScan.createFailed"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <SectionHeader>{t("productScan.photoFindByName")}</SectionHeader>
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
            <IonItem key={product.id} button detail lines={index === found.length - 1 ? "none" : undefined} onClick={() => onPick(product.id)}>
              {product.mainPhotoId
                ? <ProductThumb photoId={product.mainPhotoId} />
                : <Link2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" />}
              <IonLabel className="pm-wrap">
                <h3>{displayName(product)}</h3>
                <p>{[russianName(product), product.brand, t("products.purchaseCount", { count: product.purchaseCount })].filter(Boolean).join(" · ")}</p>
              </IonLabel>
            </IonItem>
          ))}
        </IonList>
      )}

      <SectionHeader>{t("productScan.orNew")}</SectionHeader>
      {error && <Notice tone="danger" role="alert"><p>{error}</p></Notice>}
      {creating
        ? <NewProductForm initialName={debounced} initialBrand="" busy={busy} onCreate={(name, brand) => void create(name, brand)} onCancel={() => setCreating(false)} />
        : (
          <IonList inset>
            <IonItem button detail lines="none" onClick={() => setCreating(true)}>
              <PackagePlus slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
              <IonLabel className="pm-wrap">
                <h3>{t("productScan.createProduct")}</h3>
                <p>{t("productScan.photoCreateNote")}</p>
              </IonLabel>
            </IonItem>
          </IonList>
        )}
    </>
  );
}

function matchFacts(t: TFunction, locale: string, match: PhotoSearchMatch): string {
  return [
    match.brand,
    confidenceText(t, match.confidence),
    match.lastPurchasedAt
      ? t("productScan.photoLastBought", { date: formatDate(locale, match.lastPurchasedAt) })
      : t("productScan.photoNeverBought")
  ].filter(Boolean).join(" · ");
}

function confidenceText(t: TFunction, confidence: PhotoMatchConfidence): string {
  return confidence === "high" ? t("productScan.photoConfidenceHigh")
    : confidence === "medium" ? t("productScan.photoConfidenceMedium")
    : t("productScan.photoConfidenceLow");
}

function failure(t: TFunction, error: unknown): { message: string; retry: boolean } {
  if (error instanceof PhotoDecodeError) return { message: t("productScan.photoUnreadable"), retry: false };
  if (error instanceof ApiError) {
    if (error.code === "findByPhotoDisabled") return { message: t("productScan.photoDisabled"), retry: false };
    if (error.status === 429) return { message: t("productScan.photoTooMany"), retry: true };
    if (error.status === 413 || error.status === 415) return { message: t("productScan.photoUnreadable"), retry: false };
    if (error.code === "findByPhotoUnavailable") return { message: t("productScan.photoUnavailable"), retry: true };
  }
  return { message: t("productScan.photoSearchFailed"), retry: true };
}
