import { IonItem, IonLabel, IonList, IonSkeletonText } from "@ionic/react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";

/** Small grey caps above an inset list, as in iOS Settings. Optional action on the right. */
export function SectionHeader({ children, action, id, level = 2 }: { children: ReactNode; action?: ReactNode; id?: string; level?: 2 | 3 }) {
  const Heading = level === 2 ? "h2" : "h3";
  return (
    <div className="pm-section-header">
      <Heading id={id}>{children}</Heading>
      {action}
    </div>
  );
}

export type Tone = "neutral" | "warning" | "success" | "danger";

const toneClass: Record<Tone, string> = { neutral: "", warning: "pm-warning", success: "pm-success", danger: "pm-danger" };

/** Grey (or tinted) explanation between grouped lists. */
export function Footnote({ children, tone = "neutral", role, id }: { children: ReactNode; tone?: Tone; role?: "status" | "alert" | "note"; id?: string }) {
  return <p id={id} className={["pm-footnote", toneClass[tone]].filter(Boolean).join(" ")} role={role}>{children}</p>;
}

/** A boxed message: an error with a retry, a saved state, a warning list. */
export function Notice({ children, tone = "neutral", role, label }: { children: ReactNode; tone?: Tone; role?: "status" | "alert"; label?: string }) {
  return <div className={["pm-notice", toneClass[tone]].filter(Boolean).join(" ")} role={role} aria-label={label}>{children}</div>;
}

export interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  description?: string;
  action?: ReactNode;
  headingLevel?: 1 | 2 | 3;
}

/** Nothing to show yet: an icon, a sentence and at most one way forward. */
export function EmptyState({ icon, title, description, action, headingLevel = 2 }: EmptyStateProps) {
  const Heading = headingLevel === 1 ? "h1" : headingLevel === 2 ? "h2" : "h3";
  return (
    <div className="pm-empty">
      {icon && <span aria-hidden="true">{icon}</span>}
      <Heading>{title}</Heading>
      {description && <p>{description}</p>}
      {action}
    </div>
  );
}

/**
 * Placeholder rows while a list loads. Announced once through `label`; the
 * shimmering rows themselves are hidden from assistive technology.
 */
export function LoadingList({ label, rows = 4 }: { label?: string; rows?: number }) {
  const { t } = useTranslation();
  return (
    <div role="status" aria-busy="true" aria-label={label ?? t("ui.loading")}>
      <IonList inset aria-hidden="true">
        {Array.from({ length: rows }, (_, index) => (
          <IonItem key={index} lines={index === rows - 1 ? "none" : undefined}>
            <IonLabel>
              <h2><IonSkeletonText animated style={{ width: "55%" }} /></h2>
              <p><IonSkeletonText animated style={{ width: "35%" }} /></p>
            </IonLabel>
          </IonItem>
        ))}
      </IonList>
    </div>
  );
}

export type PurchaseStatus = "uploaded" | "processing" | "ready" | "needsAttention" | "verified" | "possibleDuplicate" | "failed" | "saved";

const statusTone: Record<PurchaseStatus, string> = {
  uploaded: "",
  processing: "",
  ready: "pm-ok",
  needsAttention: "pm-warn",
  verified: "pm-ok",
  possibleDuplicate: "pm-warn",
  failed: "pm-bad",
  saved: "pm-ok"
};

/** A receipt's or purchase's state as short coloured text, the way iOS lists show status. */
export function StatusText({ status }: { status: PurchaseStatus }) {
  const { t } = useTranslation();
  return <span className={["pm-status", statusTone[status]].filter(Boolean).join(" ")}>{t(`status.${status}`)}</span>;
}
