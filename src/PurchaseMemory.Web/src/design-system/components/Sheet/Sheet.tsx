import { IonButton, IonButtons, IonContent, IonHeader, IonModal, IonTitle, IonToolbar } from "@ionic/react";
import { useId, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { HistoryLayerScope, useHistoryLayer } from "../../history/historyLayers";

export interface SheetProps {
  isOpen: boolean;
  /** Called however the sheet closes: «Готово», a swipe down, the backdrop, or «назад» (an open sheet is one history step). */
  onClose: () => void;
  title: string;
  /** Toolbar start, e.g. «Сбросить» in a filter sheet. */
  start?: ReactNode;
  /** Replaces the default «Готово» in the toolbar end. */
  end?: ReactNode;
  /** A «?» (HelpLink) next to «Готово». */
  help?: ReactNode;
  /**
   * `sheet`: a bottom sheet that opens at three quarters and can be pulled up.
   * `full`: a full-screen card for longer forms and lists.
   */
  variant?: "sheet" | "full";
  /** The sheet opens at this height; `full` ignores it. */
  initialBreakpoint?: number;
  children: ReactNode;
}

/**
 * A modal sheet with an iOS navigation bar: title in the middle, «Готово» on
 * the right. Content uses the grouped background, so inset lists look the
 * same as on the screens.
 */
export function Sheet({ isOpen, onClose, title, start, end, help, variant = "sheet", initialBreakpoint = 0.75, children }: SheetProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const layer = useHistoryLayer(isOpen, onClose);
  const sheetProps = variant === "sheet" ? { initialBreakpoint, breakpoints: [0, initialBreakpoint, 1] } : {};
  return (
    <IonModal isOpen={isOpen} onDidDismiss={onClose} aria-labelledby={titleId} {...sheetProps}>
      <IonHeader>
        <IonToolbar>
          {start && <IonButtons slot="start">{start}</IonButtons>}
          <IonTitle><h2 id={titleId} className="pm-title-heading">{title}</h2></IonTitle>
          <IonButtons slot="end">{help}{end ?? <IonButton strong onClick={onClose}>{t("ui.done")}</IonButton>}</IonButtons>
        </IonToolbar>
      </IonHeader>
      <IonContent color="light">
        {/* Ionic renders the children only while open, like the shim in tests. */}
        <div className="pm-page-body"><HistoryLayerScope value={layer}>{children}</HistoryLayerScope></div>
      </IonContent>
    </IonModal>
  );
}
