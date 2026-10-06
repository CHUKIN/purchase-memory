import { AlertTriangle, CheckCircle2, ChevronDown, SlidersHorizontal } from "lucide-react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { getReceiptAuditTrail } from "../../api/client";
import type {
  BoundingBox,
  PurchaseDraftIssue,
  ReceiptItemSnapshot,
  ReceiptReviewAuditEntry,
  ReceiptSnapshot
} from "../../api/types";
import { EmptyState, StatusBadge, Surface } from "../../design-system/components";
import { isIssueResolved, issueCopy, issueKey } from "../issues/issueText";
import styles from "./IssueReview.module.css";

/** The receipt photo an issue card can point at. */
export interface IssueReviewPhoto {
  url: string;
  /**
   * Bounding boxes describe one image. A receipt photographed in several parts
   * gives no way to tell which part a box belongs to, so cropping is disabled
   * and the first photo is shown whole.
   */
  croppable: boolean;
}

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
 * The default review surface for a receipt that needs attention: one card per
 * detected issue, each scoped to just the fields that issue is about. Anything
 * broader belongs in the full editor, which stays one click away.
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
    <section className={styles.review} aria-labelledby={titleId}>
      <div className={styles.heading}>
        <div>
          <p className={styles.eyebrow}>{t("issues.eyebrow")}</p>
          <h2 id={titleId}>{t("issues.title")}</h2>
          <p className={styles.lead}>
            {unresolved.length === 0 ? t("issues.allResolved") : t("issues.remaining", { count: unresolved.length })}
          </p>
        </div>
        <StatusBadge status={unresolved.length === 0 ? "verified" : "needsAttention"} />
      </div>

      {issues.length === 0 ? (
        <EmptyState
          icon={<CheckCircle2 />}
          title={t("issues.emptyTitle")}
          description={t("issues.emptyDescription")}
        />
      ) : (
        <ul className={styles.list}>
          {issues.map((issue) => (
            <li key={issueKey(issue)}>
              <IssueCard
                issue={issue}
                snapshot={snapshot}
                onSnapshotChange={onSnapshotChange}
                photo={photo}
              />
            </li>
          ))}
        </ul>
      )}

      <div className={styles.actions}>
        <button type="button" className={styles.fullEditor} onClick={onOpenFullEditor}>
          <SlidersHorizontal aria-hidden="true" />
          {t("issues.openFullEditor")}
        </button>
        <button type="button" className={styles.save} disabled={!canSave || busy || unresolved.length > 0} onClick={onSave}>
          {busy ? t("legacyCapture.saving") : t("legacyCapture.savePurchase")}
        </button>
      </div>

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
    <Surface as="section" className={styles.history}>
      <button
        type="button"
        className={styles.historyToggle}
        aria-expanded={open}
        aria-controls={historyId}
        onClick={() => setOpen((current) => !current)}
      >
        <ChevronDown aria-hidden="true" className={styles.historyChevron} data-open={open} />
        {open ? t("review.history.toggleHide") : t("review.history.toggleShow")}
      </button>

      <div className={styles.historyCollapsible} data-open={open}>
        <div id={historyId} className={styles.historyInner}>
          <h3 className={styles.historyTitle}>{t("review.history.title")}</h3>
          {query.isLoading && <p className={styles.detail}>{t("review.history.loading")}</p>}
          {query.isError && <p className={styles.detail}>{t("review.history.loadFailed")}</p>}
          {query.data && query.data.length === 0 && <p className={styles.detail}>{t("review.history.empty")}</p>}
          {query.data && query.data.length > 0 && (
            <ul className={styles.historyList}>
              {query.data.map((entry, index) => (
                <li key={`${entry.occurredAt}-${entry.fieldName}-${index}`} className={styles.historyEntry}>
                  <HistoryEntryLine entry={entry} />
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </Surface>
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
    <div>
      <p className={styles.historyEntrySummary}>{t(summaryKey, { field: entry.fieldName })}</p>
      <p className={styles.historyEntryValues}>
        {entry.oldValue !== null && `${t("review.history.from", { value: entry.oldValue })} `}
        {entry.newValue !== null && t("review.history.to", { value: entry.newValue })}
      </p>
      <p className={styles.historyEntryMeta}>
        {t("review.history.actor", { actor: entry.actor })} · {new Date(entry.occurredAt).toLocaleString()}
      </p>
    </div>
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
    <Surface as="article" className={styles.card} data-resolved={resolved}>
      <div className={styles.cardHeading}>
        <span className={styles.icon} aria-hidden="true">
          {resolved ? <CheckCircle2 /> : <AlertTriangle />}
        </span>
        <div>
          <h3>{copy.title}</h3>
          <p className={styles.detail}>{copy.detail}</p>
        </div>
        {resolved && <span className={styles.resolved}>{t("issues.resolved")}</span>}
      </div>

      {/*
        Once resolved, the suggestion and its inputs no longer matter, so this
        region collapses smoothly instead of the card just losing content.
        The grid-rows trick animates to an auto height without measuring it.
      */}
      <div className={styles.collapsible} data-resolved={resolved}>
        <div className={styles.collapsibleInner}>
          <IssuePhoto issue={issue} photo={photo} />
          <p className={styles.suggestion}>{copy.suggestion}</p>

          {issue.type === "receiptTotalMismatch" && (
            <TotalMismatchFix snapshot={snapshot} onSnapshotChange={onSnapshotChange} />
          )}
          {issue.type === "missingItemPrice" && issue.itemPosition !== null && (
            <MissingPriceFix position={issue.itemPosition} snapshot={snapshot} onSnapshotChange={onSnapshotChange} />
          )}
        </div>
      </div>
    </Surface>
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
  if (!box) {
    return (
      <img className={styles.photoFull} src={photo.url} alt={t("issues.photoFullAlt")} loading="lazy" />
    );
  }

  return (
    <div
      className={styles.crop}
      style={photoRatio === null ? undefined : { aspectRatio: `${(photoRatio * box.width) / box.height}` }}
    >
      <img
        className={styles.cropImage}
        src={photo.url}
        alt={t("issues.photoCropAlt")}
        loading="lazy"
        onLoad={(event) =>
          setPhotoRatio(event.currentTarget.naturalWidth / Math.max(event.currentTarget.naturalHeight, 1))
        }
        style={{
          width: `${100 / box.width}%`,
          left: `${(-100 * box.x) / box.width}%`,
          top: `${(-100 * box.y) / box.height}%`
        }}
      />
    </div>
  );
}

/** Widens a tight row box a little so the fragment keeps some visual context. */
function padBox(box: BoundingBox | null | undefined): BoundingBox | null {
  if (!box) return null;
  if (!isUsableBox(box)) return null;

  const paddingX = 0.02;
  const paddingY = 0.015;
  const x = Math.max(0, box.x - paddingX);
  const y = Math.max(0, box.y - paddingY);
  return {
    x,
    y,
    width: Math.min(1 - x, box.width + (box.x - x) + paddingX),
    height: Math.min(1 - y, box.height + (box.y - y) + paddingY)
  };
}

/**
 * The backend already validates every box, but the UI must not build a broken
 * crop out of a response it did not expect.
 */
function isUsableBox(box: BoundingBox): boolean {
  const values = [box.x, box.y, box.width, box.height];
  return (
    values.every((value) => typeof value === "number" && Number.isFinite(value)) &&
    box.x >= 0 &&
    box.y >= 0 &&
    box.width > 0 &&
    box.height > 0 &&
    box.x + box.width <= 1.001 &&
    box.y + box.height <= 1.001
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
    <div className={styles.fix}>
      <NumberField
        label={t("legacyCapture.total")}
        value={snapshot.receiptTotal}
        onChange={(receiptTotal) => onSnapshotChange({ ...snapshot, receiptTotal })}
      />
      {linesTotal !== null && (
        <button
          type="button"
          className={styles.apply}
          onClick={() => onSnapshotChange({ ...snapshot, receiptTotal: round(linesTotal) })}
        >
          {t("issues.useLinesTotal", { value: linesTotal.toFixed(2) })}
        </button>
      )}
    </div>
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
    <div className={styles.fix}>
      <NumberField
        label={t("legacyCapture.price")}
        value={item.unitPrice}
        onChange={(unitPrice) => patch({ ...mirror(unitPrice), unitPrice })}
      />
      <NumberField
        label={t("legacyCapture.amount")}
        value={item.lineTotal}
        onChange={(lineTotal) => patch({ ...mirror(lineTotal), lineTotal })}
      />
    </div>
  );
}

function NumberField({
  label,
  value,
  onChange
}: {
  label: string;
  value: number | null;
  onChange: (value: number | null) => void;
}) {
  return (
    <label className={styles.field}>
      <span>{label}</span>
      <input
        type="number"
        inputMode="decimal"
        min="0"
        step="0.01"
        value={value ?? ""}
        onChange={(event) => onChange(event.target.value === "" ? null : Number(event.target.value))}
      />
    </label>
  );
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
