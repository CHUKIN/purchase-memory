import { IonButton, IonLabel, IonList } from "@ionic/react";
import { useTranslation } from "react-i18next";
import { ItemLink, Notice } from "../../design-system/components";

export function ReceiptDuplicateNotice({ receiptId, onStartOver }: { receiptId: string; onStartOver: () => void }) {
  const { t } = useTranslation();
  return (
    <>
      <Notice tone="warning" role="status">
        <p><strong>{t("receiptCapture.duplicateTitle")}</strong></p>
        <p>{t("receiptCapture.duplicateBody")}</p>
      </Notice>
      <IonList inset>
        <ItemLink to={`/receipts/${receiptId}`} lines="none"><IonLabel color="primary">{t("receiptCapture.openExisting")}</IonLabel></ItemLink>
      </IonList>
      <div className="ion-padding-horizontal">
        <IonButton expand="block" fill="clear" onClick={onStartOver}>{t("receiptCapture.startOver")}</IonButton>
      </div>
    </>
  );
}
