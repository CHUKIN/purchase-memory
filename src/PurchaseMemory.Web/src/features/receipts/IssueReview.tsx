import { IonItem, IonLabel, IonList } from "@ionic/react";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, ChevronDown, SlidersHorizontal } from "lucide-react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { getReceiptAuditTrail } from "../../api/client";
import type {
  PurchaseDraftIssue,
  ReceiptItemSnapshot,
  ReceiptReviewAuditEntry,
  ReceiptSnapshot
} from "../../api/types";
import { DecimalInput, EmptyState, Footnote, PrimaryButton, SectionHeader } from "../../design-system/components";
import { isIssueResolved, issueCopy, issueKey } from "../issues/issueText";
import { imageOffset, padBox, type ReceiptPhoto } from "../receipt-photo/photoBox";

/** The receipt photo an issue card can point at (see `ReceiptPhoto`). */
export type IssueReviewPhoto = ReceiptPhoto;

export interface IssueReviewProps {
  snapshot: ReceiptSnapshot;
  onSnapshotChange: (snapshot: ReceiptSnapshot) => void;
  issues: PurchaseDraftIssue[];
  /** Switches to the full field-by-field editor. */
  onOpenFullEditor: () => void;
  onSave: () => void;
  busy?: boolean;
  canSave?: boolean;
  /** The stored receipt photo, when the receipt still has one. */
  photo?: IssueReviewPhoto | null;
  /**
   * Identifies the receipt whose review audit trail the history section
   * fetches. Omitted in contexts (like some stories) that never load history.
   */
  receiptId?: string;
}

/**
 * The default review surface for a receipt that needs attention: one group per
 * detected issue, each with just the fields that issue is about. Anything
 * broader belongs in the full editor, which stays one tap away.
 */
export function IssueReview({
  snapshot,
  onSnapshotChange,
  issues,
  onOpenFullEditor,
  onSave,
  busy = false,
  canSave = true,
  photo = null,
  receiptId
}: IssueReviewProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const unresolved = issues.filter((issue) => !isIssueResolved(issue, snapshot));

  return (
    <section aria-labelledby={titleId}>
      <SectionHeader id={titleId}>{t("issues.title")}</SectionHeader>
      <Footnote role="status">{unresolved.length === 0 ? t("issues.allResolved") : t("issues.remaining", { count: unresolved.length })}</Footnote>

      {issues.length === 0
        ? <EmptyState icon={<CheckCircle2 />} title={t("issues.emptyTitle")} headingLevel={3} />
        : issues.map((issue) => <IssueCard key={issueKey(issue)} issue={issue} snapshot={snapshot} onSnapshotChange={onSnapshotChange} photo={photo} />)}

      <PrimaryButton disabled={!canSave || unresolved.length > 0} loading={busy} onClick={onSave}>
        {busy ? t("legacyCapture.saving") : t("legacyCapture.savePurchase")}
      </PrimaryButton>
      <IonList inset>
        <IonItem button detail lines="none" onClick={onOpenFullEditor}>
          <SlidersHorizontal slot="start" size={20} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{t("issues.openFullEditor")}</IonLabel>
        </IonItem>
      </IonList>

      {receiptId && <ReviewHistory receiptId={receiptId} />}
    </section>
  );
}

/**
 * A collapsible audit trail of the corrections made during review, fetched
 * lazily so a receipt with no history never pays for the request. It refetches
 * whenever the audit-trail query is invalidated (the same invalidation the
 * review submit already triggers), so a fix just applied shows up here too.
 */
function ReviewHistory({ receiptId }: { receiptId: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const historyId = useId();
  const query = useQuery({
    queryKey: ["receipts", receiptId, "auditTrail"],
    queryFn: () => getReceiptAuditTrail(receiptId),
    enabled: open
  });

  return (
    <section>
      <IonList inset>
        <button type="button" className="pm-disclosure" data-open={open} aria-expanded={open} aria-controls={historyId} onClick={() => setOpen((current) => !current)}>
          <span className="pm-disclosure-label">{open ? t("review.history.toggleHide") : t("review.history.toggleShow")}</span>
          <ChevronDown size={20} aria-hidden="true" className="pm-chevron" data-open={open} />
        </button>
        {open && (
          <div id={historyId}>
            <h3 className="visually-hidden">{t("review.history.title")}</h3>
            {query.isLoading && <IonItem lines="none"><IonLabel color="medium">{t("review.history.loading")}</IonLabel></IonItem>}
            {query.isError && <IonItem lines="none"><IonLabel color="medium">{t("review.history.loadFailed")}</IonLabel></IonItem>}
            {query.data && query.data.length === 0 && <IonItem lines="none"><IonLabel color="medium">{t("review.history.empty")}</IonLabel></IonItem>}
            {query.data?.map((entry, index) => (
              <IonItem key={`${entry.occurredAt}-${entry.fieldName}-${index}`} lines={index === query.data.length - 1 ? "none" : undefined}>
                <HistoryEntryLine entry={entry} />
              </IonItem>
            ))}
          </div>
        )}
      </IonList>
    </section>
  );
}

function HistoryEntryLine({ entry }: { entry: ReceiptReviewAuditEntry }) {
  const { t } = useTranslation();
  const summaryKey =
    entry.changeType === "ItemAdded"
      ? "review.history.itemAdded"
      : entry.changeType === "ItemRemoved"
        ? "review.history.itemRemoved"
        : "review.history.modified";

  return (
    <IonLabel className="pm-wrap">
      <h3>{t(summaryKey, { field: entry.fieldName })}</h3>
      <p>
        {entry.oldValue !== null && `${t("review.history.from", { value: entry.oldValue })} `}
        {entry.newValue !== null && t("review.history.to", { value: entry.newValue })}
      </p>
      <p>{t("review.history.actor", { actor: entry.actor })} · {new Date(entry.occurredAt).toLocaleString()}</p>
    </IonLabel>
  );
}

function IssueCard({
  issue,
  snapshot,
  onSnapshotChange,
  photo
}: {
  issue: PurchaseDraftIssue;
  snapshot: ReceiptSnapshot;
  onSnapshotChange: (snapshot: ReceiptSnapshot) => void;
  photo: IssueReviewPhoto | null;
}) {
  const { t } = useTranslation();
  const copy = issueCopy(t, issue);
  const resolved = isIssueResolved(issue, snapshot);

  return (
    <article className="pm-issue" data-resolved={resolved}>
      <IonList inset>
        <IonItem lines={resolved ? "none" : "full"}>
          {resolved
            ? <CheckCircle2 slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
            : <AlertTriangle slot="start" size={22} className="pm-row-icon pm-row-icon-warning" aria-hidden="true" />}
          <IonLabel className="pm-wrap">
            <h3>{copy.title}</h3>
            <p>{copy.detail}</p>
          </IonLabel>
          {resolved && <span className="visually-hidden">{t("issues.resolved")}</span>}
        </IonItem>
        {/* Once resolved, the suggestion and its inputs no longer matter. */}
        {!resolved && (
          <>
            <IssuePhoto issue={issue} photo={photo} />
            {/* The fields below are the fix; only an unknown issue type needs words. */}
            {copy.suggestion && (
              <IonItem lines="full">
                <IonLabel className="pm-wrap" color="medium"><p>{copy.suggestion}</p></IonLabel>
              </IonItem>
            )}
            {issue.type === "receiptTotalMismatch" && (
              <TotalMismatchFix snapshot={snapshot} onSnapshotChange={onSnapshotChange} />
            )}
            {issue.type === "missingItemPrice" && issue.itemPosition !== null && (
              <MissingPriceFix position={issue.itemPosition} snapshot={snapshot} onSnapshotChange={onSnapshotChange} />
            )}
          </>
        )}
      </IonList>
    </article>
  );
}

/**
 * The receipt fragment the issue is about. Recognition rarely returns reliable
 * coordinates, so this degrades in steps: crop, then the whole photo, then
 * nothing at all. It never replaces the text of the card, which stays the
 * accessible source of truth.
 */
function IssuePhoto({ issue, photo }: { issue: PurchaseDraftIssue; photo: IssueReviewPhoto | null }) {
  const { t } = useTranslation();
  // The photo's own aspect ratio is only known once the browser has it, and the
  // crop frame needs it to keep the fragment undistorted.
  const [photoRatio, setPhotoRatio] = useState<number | null>(null);

  if (!photo) return null;

  const box = photo.croppable ? padBox(issue.boundingBox) : null;
  return (
    <IonItem lines="full">
      <div className="pm-issue-photo">
        {!box
          ? <img className="pm-issue-photo-full" src={photo.url} alt={t("issues.photoFullAlt")} loading="lazy" />
          : (
            <div className="pm-crop" style={photoRatio === null ? undefined : { aspectRatio: `${(photoRatio * box.width) / box.height}` }}>
              <img
                className="pm-crop-image"
                src={photo.url}
                alt={t("issues.photoCropAlt")}
                loading="lazy"
                onLoad={(event) => setPhotoRatio(event.currentTarget.naturalWidth / Math.max(event.currentTarget.naturalHeight, 1))}
                style={imageOffset(box)}
              />
            </div>
          )}
      </div>
    </IonItem>
  );
}

function TotalMismatchFix({
  snapshot,
  onSnapshotChange
}: {
  snapshot: ReceiptSnapshot;
  onSnapshotChange: (snapshot: ReceiptSnapshot) => void;
}) {
  const { t } = useTranslation();
  const complete = snapshot.items.length > 0 && snapshot.items.every((item) => item.lineTotal !== null);
  const linesTotal = complete
    ? snapshot.items.reduce((sum, item) => sum + (item.lineTotal ?? 0), 0)
    : null;

  return (
    <>
      <IonItem lines={linesTotal === null ? "none" : "full"}>
        <DecimalInput label={t("legacyCapture.total")} value={snapshot.receiptTotal} onChange={(receiptTotal) => onSnapshotChange({ ...snapshot, receiptTotal })} />
      </IonItem>
      {linesTotal !== null && (
        <IonItem button detail={false} lines="none" onClick={() => onSnapshotChange({ ...snapshot, receiptTotal: round(linesTotal) })}>
          <IonLabel color="primary">{t("issues.useLinesTotal", { value: linesTotal.toFixed(2) })}</IonLabel>
        </IonItem>
      )}
    </>
  );
}

function MissingPriceFix({
  position,
  snapshot,
  onSnapshotChange
}: {
  position: number;
  snapshot: ReceiptSnapshot;
  onSnapshotChange: (snapshot: ReceiptSnapshot) => void;
}) {
  const { t } = useTranslation();
  const item = snapshot.items[position];
  if (!item) return null;

  const patch = (change: Partial<ReceiptItemSnapshot>) =>
    onSnapshotChange({
      ...snapshot,
      items: snapshot.items.map((current, index) => (index === position ? { ...current, ...change } : current))
    });

  // A single-unit line has the same unit price and line total, so filling one
  // side is enough to answer the whole issue in one action.
  const mirror = (value: number | null) =>
    (item.quantity ?? 1) === 1 ? { unitPrice: value, lineTotal: value } : {};

  return (
    <>
      <IonItem>
        <DecimalInput label={t("legacyCapture.price")} value={item.unitPrice} onChange={(unitPrice) => patch({ ...mirror(unitPrice), unitPrice })} />
      </IonItem>
      <IonItem lines="none">
        <DecimalInput label={t("legacyCapture.amount")} value={item.lineTotal} onChange={(lineTotal) => patch({ ...mirror(lineTotal), lineTotal })} />
      </IonItem>
    </>
  );
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
