import { IonButton, IonButtons, IonContent, IonHeader, IonItem, IonLabel, IonList, IonModal, IonNote, IonSearchbar, IonTitle, IonToolbar } from "@ionic/react";
import { Check } from "lucide-react";
import { Fragment, useId, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { SectionHeader } from "../Blocks/Blocks";

export interface PickerOption {
  value: string;
  label: string;
  /** Grey text on the right, e.g. a product count. */
  detail?: string;
}

export interface PickerGroup {
  /** Section header; omitted for the first, unnamed block («Все», «Без категории»). */
  title?: string;
  options: PickerOption[];
}

export interface PickerModalProps {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  groups: PickerGroup[];
  value: string;
  onChoose: (value: string) => void;
  /** Adds a searchbar that filters the rows by label. */
  searchable?: boolean;
  searchPlaceholder?: string;
  /**
   * With a search typed in, an extra first row that uses the typed text
   * itself, e.g. «Искать бренд „mleko“» for a free-text filter.
   */
  freeText?: (query: string) => PickerOption | null;
}

function normalize(value: string) {
  return value.toLocaleLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

/**
 * Choosing one value from a list, the iOS Settings way: a full-screen list
 * with section headers and a checkmark on the current row. Tapping a row
 * chooses it and closes the list.
 */
export function PickerModal({ isOpen, onClose, title, groups, value, onChoose, searchable = false, searchPlaceholder, freeText }: PickerModalProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const [query, setQuery] = useState("");
  const needle = normalize(query.trim());
  const visible = useMemo(() => needle === ""
    ? groups
    : groups
      .map((group) => ({ ...group, options: group.options.filter((option) => normalize(option.label).includes(needle)) }))
      .filter((group) => group.options.length > 0), [groups, needle]);
  const typed = needle !== "" && freeText ? freeText(query.trim()) : null;

  const choose = (next: string) => {
    onChoose(next);
    onClose();
  };

  const row = (option: PickerOption) => {
    const selected = option.value === value;
    return (
      <IonItem key={option.value || "\u0000"} button detail={false} onClick={() => choose(option.value)} aria-current={selected || undefined}>
        <IonLabel className="pm-wrap">{option.label}</IonLabel>
        {option.detail && <IonNote slot="end">{option.detail}</IonNote>}
        {selected && <Check slot="end" size={20} className="pm-check" aria-label={t("ui.selected")} />}
      </IonItem>
    );
  };

  return (
    <IonModal isOpen={isOpen} onDidDismiss={() => { setQuery(""); onClose(); }} aria-labelledby={titleId}>
      <IonHeader>
        <IonToolbar>
          <IonButtons slot="start"><IonButton onClick={onClose}>{t("ui.cancel")}</IonButton></IonButtons>
          <IonTitle><h2 id={titleId} className="pm-title-heading">{title}</h2></IonTitle>
        </IonToolbar>
        {searchable && (
          <IonToolbar>
            <IonSearchbar value={query} placeholder={searchPlaceholder ?? t("ui.search")} onIonInput={(event) => setQuery(event.detail.value ?? "")} />
          </IonToolbar>
        )}
      </IonHeader>
      <IonContent color="light">
        <div className="pm-page-body">
          {typed && <IonList inset>{row(typed)}</IonList>}
          {visible.map((group, index) => (
            <Fragment key={group.title ?? `group-${index}`}>
              {group.title && <SectionHeader level={3}>{group.title}</SectionHeader>}
              <IonList inset className={group.title ? "pm-after-header" : undefined}>
                {group.options.map(row)}
              </IonList>
            </Fragment>
          ))}
          {visible.length === 0 && !typed && <p className="pm-footnote">{t("ui.nothingFound")}</p>}
        </div>
      </IonContent>
    </IonModal>
  );
}

export interface SelectRowProps extends Omit<PickerModalProps, "isOpen" | "onClose" | "title" | "onChoose"> {
  /** Row label, also the picker's title unless `title` is given. */
  label: string;
  title?: string;
  /** What the row shows on the right for the current value. */
  valueText: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}

/** A list row «Категория … Все ›» that opens a `PickerModal`. */
export function SelectRow({ label, title, valueText, onChange, disabled = false, ...picker }: SelectRowProps) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <IonItem button detail disabled={disabled} onClick={() => setOpen(true)}>
        <IonLabel>{label}</IonLabel>
        <IonNote slot="end" className="pm-pick-value">{valueText}</IonNote>
      </IonItem>
      <PickerModal {...picker} isOpen={open} onClose={() => setOpen(false)} title={title ?? label} onChoose={onChange} />
    </>
  );
}
