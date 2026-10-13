import { IonButton, IonSpinner } from "@ionic/react";
import { SlidersHorizontal } from "lucide-react";
import type { ComponentProps, ReactNode } from "react";
import { useTranslation } from "react-i18next";

type IonButtonProps = ComponentProps<typeof IonButton>;

/**
 * An Ionic button that shows a spinner and refuses taps while its action
 * runs. The text stays, so the button keeps its accessible name.
 */
export function ActionButton({ loading = false, disabled, children, ...props }: IonButtonProps & { loading?: boolean; children: ReactNode }) {
  return (
    <IonButton {...props} disabled={disabled || loading} aria-busy={loading || undefined}>
      {loading && <IonSpinner name="crescent" slot="start" aria-hidden="true" />}
      {children}
    </IonButton>
  );
}

/** The screen's one main action: a full-width rounded button under the lists. */
export function PrimaryButton(props: IonButtonProps & { loading?: boolean; children: ReactNode }) {
  return <div className="pm-primary-block"><ActionButton expand="block" shape="round" {...props} /></div>;
}

/** Toolbar «Фильтры» button; the number of filters in use shows as a badge. */
export function FilterButton({ count, onClick, disabled }: { count: number; onClick: () => void; disabled?: boolean }) {
  const { t } = useTranslation();
  return (
    <IonButton className="pm-icon-button" onClick={onClick} disabled={disabled} aria-label={count > 0 ? t("ui.filtersActive", { count }) : t("ui.filters")}>
      <SlidersHorizontal size={22} aria-hidden="true" />
      {count > 0 && <span className="pm-count-badge" aria-hidden="true">{count}</span>}
    </IonButton>
  );
}
