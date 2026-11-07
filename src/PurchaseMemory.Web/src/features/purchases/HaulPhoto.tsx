import type { TFunction } from "i18next";
import { IonButton, IonItem, IonLabel, IonList } from "@ionic/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Camera, Check, RotateCw, Undo2 } from "lucide-react";
import { useEffect, useMemo, useRef, useState, type ChangeEvent } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, getHaulPhotoStatus, proposeHaulPhoto, saveHaulPhoto } from "../../api/client";
import type {
  HaulPhotoBox,
  HaulPhotoItem,
  HaulPhotoPair,
  HaulPhotoSaveResult,
  HaulPhotoSaveStatus,
  PhotoMatchConfidence,
  PurchaseDetail,
  PurchaseDetailItem
} from "../../api/types";
import { Footnote, Notice, SectionHeader, Sheet } from "../../design-system/components";
import { HelpLink } from "../help/HelpLink";
import { PhotoDecodeError, photoForHaul } from "../product-scan/compressImage";
import type { PickedPhoto } from "../product-scan/PhotoSearch";
import { Searching } from "../product-scan/ScanResults";
import { productMediaQueryKey } from "../product-scan/useProductMedia";
import { isDiscount } from "../receipts/discountLines";
import "../product-scan/productScan.css";
import "./haulPhoto.css";

export const haulPhotoStatusQueryKey = ["haul-photo", "status"] as const;

/** A table photo takes the model longer than one package; only after this long does the «server waking» note show. */
export const slowHaulPhotoMs = 20_000;

/** Whether to show «Сфотографировать покупки»: the server's switch. Any failure hides the button. */
export function useHaulPhotoEnabled(): boolean {
  const status = useQuery({
    queryKey: haulPhotoStatusQueryKey,
    queryFn: getHaulPhotoStatus,
    staleTime: 5 * 60_000,
    retry: false
  });
  return status.data?.enabled === true;
}

/**
 * «Сфотографировать покупки» on a saved purchase: a row that opens the phone
 * camera, and the sheet that pairs the photographed items with the lines.
 * Renders nothing while the server has the feature switched off.
 */
export function HaulPhotoAction({ purchase }: { purchase: PurchaseDetail }) {
  const { t } = useTranslation();
  const enabled = useHaulPhotoEnabled();
  const input = useRef<HTMLInputElement>(null);
  const photoCount = useRef(0);
  const [photo, setPhoto] = useState<PickedPhoto | null>(null);
  if (!enabled) return null;

  const takePhoto = () => input.current?.click();
  const onPicked = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    // The same file picked twice must still fire «change».
    event.target.value = "";
    if (file) setPhoto({ id: ++photoCount.current, blob: file });
  };

  return (
    <>
      <IonList inset>
        <IonItem button detail lines="none" onClick={takePhoto}>
          <Camera slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel className="pm-wrap">
            <h3>{t("haulPhoto.button")}</h3>
            <p>{t("haulPhoto.buttonNote")}</p>
          </IonLabel>
        </IonItem>
      </IonList>
      {/* The rear camera on a phone, the file picker elsewhere; one photo. */}
      <input
        ref={input}
        type="file"
        accept="image/*"
        capture="environment"
        hidden
        data-testid="haul-photo-input"
        onChange={onPicked}
      />
      <HaulPhotoSheet photo={photo} purchase={purchase} onClose={() => setPhoto(null)} onRetake={takePhoto} />
    </>
  );
}

type Decision = { status: "pending" | "confirmed" | "skipped"; lineId: string | null };

interface Proposed {
  photoId: number;
  /** The exact bytes sent: saving sends them again, so the boxes still fit. */
  blob: Blob;
  url: string;
  width: number;
  height: number;
  items: HaulPhotoItem[];
}

/**
 * The flow for one photo: recognising → one row per found item (its cut-out,
 * the proposed line, «Верно» / another line / «Пропустить») → «Сохранить
 * фото» → what was saved. The recognition is a paid call, so it is a
 * mutation sent once per photo id: a focus or a session refresh that
 * invalidates every query never repeats it; only «Попробовать ещё раз» or a
 * new photo does.
 */
export function HaulPhotoSheet({ photo, purchase, onClose, onRetake }: {
  photo: PickedPhoto | null;
  purchase: PurchaseDetail;
  onClose: () => void;
  onRetake: () => void;
}) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const lines = useMemo(() => purchase.items.filter((item) => !isDiscount(item)), [purchase.items]);
  const [decisions, setDecisions] = useState<{ photoId: number; byIndex: Record<number, Decision> } | null>(null);

  const recognise = useMutation({
    mutationFn: async (picked: PickedPhoto): Promise<Proposed> => {
      const shrunk = await photoForHaul(picked.blob);
      const proposal = await proposeHaulPhoto(purchase.id, shrunk.blob);
      return {
        photoId: picked.id,
        blob: shrunk.blob,
        url: URL.createObjectURL(shrunk.blob),
        width: shrunk.width,
        height: shrunk.height,
        items: proposal.items
      };
    },
    onSuccess: (result) => setDecisions({
      photoId: result.photoId,
      byIndex: Object.fromEntries(result.items.map((item) => [item.index, { status: "pending", lineId: item.lineId }]))
    }),
    onError: (error) => {
      if (error instanceof ApiError && error.code === "haulPhotoDisabled") {
        // Switched off meanwhile: the button disappears on the next look.
        void queryClient.invalidateQueries({ queryKey: haulPhotoStatusQueryKey });
      }
    }
  });
  const { mutate: recogniseOnce, reset: resetRecognition } = recognise;

  const save = useMutation({
    mutationFn: ({ proposed, pairs }: { proposed: Proposed; pairs: HaulPhotoPair[] }) => saveHaulPhoto(purchase.id, proposed.blob, pairs),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: productMediaQueryKey }),
        queryClient.invalidateQueries({ queryKey: ["products"] })
      ]);
    }
  });
  const { reset: resetSave } = save;

  // Once per photo, also when React runs effects twice.
  const recognisedFor = useRef<number | null>(null);
  useEffect(() => {
    if (!photo || recognisedFor.current === photo.id) return;
    recognisedFor.current = photo.id;
    resetSave();
    recogniseOnce(photo);
  }, [photo, recogniseOnce, resetSave]);

  // The cut-outs show the shrunk photo through an object URL; free it with the photo.
  const proposedUrl = recognise.data?.url;
  useEffect(() => () => { if (proposedUrl) URL.revokeObjectURL(proposedUrl); }, [proposedUrl]);

  const close = () => {
    recognisedFor.current = null;
    resetRecognition();
    resetSave();
    setDecisions(null);
    onClose();
  };

  const proposed = recognise.isSuccess && recognise.data.photoId === photo?.id ? recognise.data : null;
  const byIndex = proposed && decisions?.photoId === proposed.photoId ? decisions.byIndex : {};
  const decide = (index: number, decision: Decision) =>
    proposed && setDecisions({ photoId: proposed.photoId, byIndex: { ...byIndex, [index]: decision } });
  const withProduct = (lineId: string | null) => lines.find((line) => line.id === lineId && line.productId);
  const pairs: HaulPhotoPair[] = proposed
    ? proposed.items.flatMap((item) => {
      const decision = byIndex[item.index];
      return decision?.status === "confirmed" && decision.lineId && withProduct(decision.lineId)
        ? [{ lineId: decision.lineId, ...item.box }]
        : [];
    })
    : [];

  let title = t("haulPhoto.title");
  let body;
  if (save.isSuccess) {
    title = t("haulPhoto.doneTitle");
    body = <SaveSummary result={save.data} lines={lines} onClose={close} />;
  } else if (recognise.isError) {
    const { message, retry } = recognitionFailure(t, recognise.error);
    body = (
      <>
        <Notice tone="danger" role="alert"><p>{message}</p></Notice>
        <div className="pm-scan-buttons">
          {retry && photo && (
            <IonButton expand="block" shape="round" onClick={() => recogniseOnce(photo)}>
              <RotateCw size={20} aria-hidden="true" />&nbsp;{t("haulPhoto.retry")}
            </IonButton>
          )}
          <IonButton expand="block" shape="round" fill="outline" onClick={onRetake}>
            <Camera size={20} aria-hidden="true" />&nbsp;{t("haulPhoto.retake")}
          </IonButton>
        </div>
      </>
    );
  } else if (!proposed) {
    body = <Searching code={null} label={t("haulPhoto.recognising")} slowAfterMs={slowHaulPhotoMs} />;
  } else if (proposed.items.length === 0) {
    body = (
      <>
        <p className="pm-home-intro">{t("haulPhoto.nothingFound")}</p>
        <div className="pm-scan-buttons">
          <IonButton expand="block" shape="round" onClick={onRetake}>
            <Camera size={20} aria-hidden="true" />&nbsp;{t("haulPhoto.retake")}
          </IonButton>
        </div>
      </>
    );
  } else {
    body = (
      <>
        <p className="pm-home-intro">{t("haulPhoto.intro")}</p>
        <ul className="pm-haul-list" aria-label={t("haulPhoto.title")}>
          {proposed.items.map((item, position) => (
            <HaulItemRow
              key={item.index}
              number={position + 1}
              item={item}
              proposed={proposed}
              lines={lines}
              decision={byIndex[item.index] ?? { status: "pending", lineId: item.lineId }}
              onDecide={(decision) => decide(item.index, decision)}
            />
          ))}
        </ul>
        <Footnote>{t("haulPhoto.approximate")}</Footnote>
        {save.isError && <Notice tone="danger" role="alert"><p>{t("haulPhoto.saveFailed")}</p></Notice>}
        <div className="pm-scan-buttons">
          <IonButton
            expand="block"
            shape="round"
            disabled={pairs.length === 0 || save.isPending}
            onClick={() => save.mutate({ proposed, pairs })}
          >
            {save.isPending ? t("haulPhoto.saving") : t("haulPhoto.save", { count: pairs.length })}
          </IonButton>
          <IonButton expand="block" shape="round" fill="clear" onClick={onRetake} disabled={save.isPending}>
            <Camera size={20} aria-hidden="true" />&nbsp;{t("haulPhoto.retake")}
          </IonButton>
        </div>
      </>
    );
  }

  return (
    <Sheet
      isOpen={photo !== null}
      onClose={close}
      title={title}
      help={<HelpLink topic="haul-photo" />}
      end={<IonButton strong onClick={close}>{t("ui.close")}</IonButton>}
      variant="full"
    >
      {body}
    </Sheet>
  );
}

function HaulItemRow({ number, item, proposed, lines, decision, onDecide }: {
  number: number;
  item: HaulPhotoItem;
  proposed: Proposed;
  lines: PurchaseDetailItem[];
  decision: Decision;
  onDecide: (decision: Decision) => void;
}) {
  const { t } = useTranslation();
  const line = lines.find((candidate) => candidate.id === decision.lineId) ?? null;
  const name = line ? lineName(line) : null;
  const settled = decision.status !== "pending";
  const selectId = `haul-line-${item.index}`;
  return (
    <li className="pm-haul-row" data-status={decision.status}>
      <CropThumb box={item.box} proposed={proposed} label={t("haulPhoto.itemLabel", { number })} />
      <div className="pm-haul-body">
        {settled ? (
          <>
            <p className="pm-haul-line">{name ?? "—"}</p>
            <p className="pm-haul-state">
              {decision.status === "confirmed" ? <><Check size={16} aria-hidden="true" /> {t("haulPhoto.confirmed")}</> : t("haulPhoto.skipped")}
            </p>
            <IonButton size="small" fill="clear" onClick={() => onDecide({ ...decision, status: "pending" })}>
              <Undo2 size={16} aria-hidden="true" />&nbsp;{t("haulPhoto.undo")}
            </IonButton>
          </>
        ) : (
          <>
            <label className="visually-hidden" htmlFor={selectId}>{t("haulPhoto.lineLabel", { number })}</label>
            <select
              id={selectId}
              className="pm-haul-select"
              value={decision.lineId ?? ""}
              onChange={(event) => onDecide({ status: event.target.value ? "confirmed" : "pending", lineId: event.target.value || null })}
            >
              <option value="">{t("haulPhoto.chooseLine")}</option>
              {lines.map((candidate) => (
                <option key={candidate.id} value={candidate.id} disabled={!candidate.productId}>
                  {lineName(candidate)}{candidate.productId ? "" : ` — ${t("haulPhoto.noProductSuffix")}`}
                </option>
              ))}
            </select>
            <p className="pm-haul-state">
              {decision.lineId === null
                ? t("haulPhoto.noLine")
                : line && !line.productId ? t("haulPhoto.noProduct")
                : decision.lineId === item.lineId ? confidenceText(t, item.confidence) : null}
            </p>
            <div className="pm-haul-actions">
              <IonButton
                size="small"
                shape="round"
                disabled={!line?.productId}
                aria-label={name ? t("haulPhoto.correctLabel", { name }) : t("haulPhoto.correct")}
                onClick={() => onDecide({ ...decision, status: "confirmed" })}
              >
                {t("haulPhoto.correct")}
              </IonButton>
              <IonButton
                size="small"
                shape="round"
                fill="outline"
                aria-label={t("haulPhoto.skipLabel", { number })}
                onClick={() => onDecide({ ...decision, status: "skipped" })}
              >
                {t("haulPhoto.skip")}
              </IonButton>
            </div>
          </>
        )}
      </div>
    </li>
  );
}

/**
 * The item's region of the sent photo, drawn with CSS from the same object
 * URL: no second decode, no canvas. The frame keeps the region's shape
 * (clamped for very long or flat items).
 */
function CropThumb({ box, proposed, label }: { box: HaulPhotoBox; proposed: Proposed; label: string }) {
  const ratio = Math.min(2.5, Math.max(0.4, (box.width * proposed.width) / Math.max(1, box.height * proposed.height)));
  const position = (start: number, size: number) => size >= 1 ? 0 : (start / (1 - size)) * 100;
  return (
    <div
      role="img"
      aria-label={label}
      className="pm-haul-thumb"
      style={{
        aspectRatio: String(ratio),
        backgroundImage: `url("${proposed.url}")`,
        backgroundSize: `${100 / box.width}% ${100 / box.height}%`,
        backgroundPosition: `${position(box.x, box.width)}% ${position(box.y, box.height)}%`
      }}
    />
  );
}

function SaveSummary({ result, lines, onClose }: { result: HaulPhotoSaveResult; lines: PurchaseDetailItem[]; onClose: () => void }) {
  const { t } = useTranslation();
  const saved = result.results.filter((entry) => entry.status === "saved").length;
  const repeated = result.results.filter((entry) => entry.status === "alreadySaved").length;
  const notSaved = result.results.filter((entry) => entry.status !== "saved" && entry.status !== "alreadySaved");
  const nameOf = (lineId: string) => {
    const line = lines.find((candidate) => candidate.id === lineId);
    return line ? lineName(line) : "—";
  };
  return (
    <>
      <Notice tone="success" role="status">
        <p>{t("haulPhoto.savedCount", { count: saved })}</p>
        {repeated > 0 && <p>{t("haulPhoto.alreadySavedCount", { count: repeated })}</p>}
      </Notice>
      <Footnote>{t("haulPhoto.mainNote")}</Footnote>
      {notSaved.length > 0 && (
        <>
          <SectionHeader>{t("haulPhoto.notSavedTitle")}</SectionHeader>
          <IonList inset>
            {notSaved.map((entry, index) => (
              <IonItem key={`${entry.lineId}-${index}`} lines={index === notSaved.length - 1 ? "none" : undefined}>
                <IonLabel className="pm-wrap">
                  <h3>{nameOf(entry.lineId)}</h3>
                  <p>{statusText(t, entry.status)}</p>
                </IonLabel>
              </IonItem>
            ))}
          </IonList>
        </>
      )}
      <div className="pm-scan-buttons">
        <IonButton expand="block" shape="round" onClick={onClose}>{t("ui.close")}</IonButton>
      </div>
    </>
  );
}

function lineName(line: PurchaseDetailItem): string {
  return line.productNamePl || line.name;
}

function confidenceText(t: TFunction, confidence: PhotoMatchConfidence): string {
  return confidence === "high" ? t("haulPhoto.confidenceHigh")
    : confidence === "medium" ? t("haulPhoto.confidenceMedium")
    : t("haulPhoto.confidenceLow");
}

function statusText(t: TFunction, status: HaulPhotoSaveStatus): string {
  switch (status) {
    case "noProduct": return t("haulPhoto.statusNoProduct");
    case "lineNotFound": return t("haulPhoto.statusLineNotFound");
    case "invalidBox": return t("haulPhoto.statusInvalidBox");
    case "photoLimit": return t("haulPhoto.statusPhotoLimit");
    case "productGone": return t("haulPhoto.statusProductGone");
    default: return t("haulPhoto.statusFailed");
  }
}

function recognitionFailure(t: TFunction, error: unknown): { message: string; retry: boolean } {
  if (error instanceof PhotoDecodeError) return { message: t("haulPhoto.unreadable"), retry: false };
  if (error instanceof ApiError) {
    if (error.code === "haulPhotoDisabled") return { message: t("haulPhoto.disabled"), retry: false };
    if (error.status === 429) return { message: t("haulPhoto.tooMany"), retry: true };
    if (error.status === 413 || error.status === 415) return { message: t("haulPhoto.unreadable"), retry: false };
    if (error.code === "haulPhotoUnavailable") return { message: t("haulPhoto.unavailable"), retry: true };
  }
  return { message: t("haulPhoto.failed"), retry: true };
}
