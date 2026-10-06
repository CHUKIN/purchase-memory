import { useTranslation } from "react-i18next";
import { Link } from "react-router";

export function ReceiptDuplicateNotice({ receiptId, onStartOver }: { receiptId: string; onStartOver: () => void }) {
  const { t } = useTranslation();
  return <div className="notice" role="status"><div><strong>{t("receiptCapture.duplicateTitle")}</strong><p>{t("receiptCapture.duplicateBody")}</p><Link className="secondary-button link-button" to={`/receipts/${receiptId}`}>{t("receiptCapture.openExisting")}</Link><button className="text-button" onClick={onStartOver}>{t("receiptCapture.startOver")}</button></div></div>;
}
