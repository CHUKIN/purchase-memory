import { IonInput, IonItem, IonLabel, IonList } from "@ionic/react";
import { Check } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Footnote } from "../../design-system/components";
import { checkBarcode } from "./barcode";

/**
 * The digits under a barcode, typed by hand: when the camera is not allowed,
 * or a crumpled pack will not read. The check digit catches most typos.
 */
export function ManualBarcodeForm({ submitLabel, onSubmit, busy = false }: {
  submitLabel: string;
  onSubmit: (code: string) => void;
  busy?: boolean;
}) {
  const { t } = useTranslation();
  const [value, setValue] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const result = checkBarcode(value);
    if (!result.ok) {
      setError(result.reason === "length" ? t("productScan.manualLength") : t("productScan.manualChecksum"));
      return;
    }
    setError(null);
    onSubmit(result.code);
  };

  return (
    <>
      <IonList inset>
        <IonItem>
          <IonInput
            label={t("productScan.manualLabel")}
            labelPlacement="stacked"
            placeholder={t("productScan.manualPlaceholder")}
            inputmode="numeric"
            autocomplete="off"
            maxlength={16}
            value={value}
            disabled={busy}
            onIonInput={(event) => { setValue(String(event.detail.value ?? "")); setError(null); }}
            onKeyDown={(event) => { if (event.key === "Enter") submit(); }}
          />
        </IonItem>
        <IonItem button detail={false} lines="none" disabled={busy || value.trim() === ""} onClick={submit}>
          <Check slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel color="primary">{submitLabel}</IonLabel>
        </IonItem>
      </IonList>
      {error ? <Footnote tone="danger" role="alert">{error}</Footnote> : <Footnote>{t("productScan.manualNote")}</Footnote>}
    </>
  );
}
