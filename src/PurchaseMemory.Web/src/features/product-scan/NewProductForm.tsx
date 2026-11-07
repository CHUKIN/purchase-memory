import { IonButton, IonInput, IonItem, IonList } from "@ionic/react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { ActionButton } from "../../design-system/components";
import "./productScan.css";

/** The name (prefilled from Open Food Facts when it knew the code) and an optional brand. */
export function NewProductForm({ initialName, initialBrand, busy, onCreate, onCancel }: {
  initialName: string;
  initialBrand: string;
  busy: boolean;
  onCreate: (name: string, brand: string) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState(initialName);
  const [brand, setBrand] = useState(initialBrand);
  const clean = name.trim();
  return (
    <form onSubmit={(event) => { event.preventDefault(); if (clean) onCreate(clean, brand); }}>
      <IonList inset>
        <IonItem>
          <IonInput
            label={t("productScan.createNameLabel")}
            labelPlacement="stacked"
            value={name}
            maxlength={500}
            placeholder={t("productScan.createNamePlaceholder")}
            onIonInput={(event) => setName(String(event.detail.value ?? ""))}
          />
        </IonItem>
        <IonItem lines="none">
          <IonInput
            label={t("productScan.createBrandLabel")}
            labelPlacement="stacked"
            value={brand}
            maxlength={100}
            onIonInput={(event) => setBrand(String(event.detail.value ?? ""))}
          />
        </IonItem>
      </IonList>
      <div className="pm-scan-buttons">
        <ActionButton expand="block" shape="round" type="submit" loading={busy} disabled={!clean}>{t("productScan.createSubmit")}</ActionButton>
        <IonButton expand="block" shape="round" fill="clear" disabled={busy} onClick={onCancel}>{t("ui.cancel")}</IonButton>
      </div>
    </form>
  );
}
