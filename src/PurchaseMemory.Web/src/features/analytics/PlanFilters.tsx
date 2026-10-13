import { IonButton, IonItem, IonLabel, IonList, IonSelect, IonSelectOption } from "@ionic/react";
import { SlidersHorizontal, X } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsDiscountFilter, AnalyticsGroupBy, AnalyticsMetric, AnalyticsPlan, ProductCategory, ProductTag } from "../../api/types";
import { ActionButton, DateField, SectionHeader, SelectRow, Sheet } from "../../design-system/components";
import { planChips } from "./analyticsText";

const metrics: AnalyticsMetric[] = ["spend", "quantity", "purchaseCount", "averageUnitPrice", "minimumUnitPrice", "maximumUnitPrice", "exists", "top"];
const groupings: AnalyticsGroupBy[] = ["none", "month", "week", "merchant", "category", "product", "brand", "tag"];
const discounts: AnalyticsDiscountFilter[] = ["any", "discountedOnly", "notDiscounted"];

export interface PlanFiltersProps {
  plan: AnalyticsPlan;
  categories: ProductCategory[];
  merchants: string[];
  /** The household's tags: names for tag chips and the options of the tag field. */
  tags?: ProductTag[];
  productNames: Map<string, string>;
  locale: string;
  /** Re-runs the edited plan without asking the model. */
  onChange: (plan: AnalyticsPlan) => void;
  busy?: boolean;
}

/**
 * The server's interpretation as chips. Removing a chip or editing the plan
 * in the sheet re-runs it directly, so correcting a misunderstanding never
 * costs a model call.
 */
export function PlanFilters({ plan, categories, merchants, tags = [], productNames, locale, onChange, busy = false }: PlanFiltersProps) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const chips = planChips(t, locale, plan, categories, productNames, tags);

  return (
    <section aria-labelledby="analytics-filters-title">
      <SectionHeader
        id="analytics-filters-title"
        level={3}
        action={(
          <IonButton fill="clear" size="small" disabled={busy} onClick={() => setEditing(true)}>
            <SlidersHorizontal aria-hidden="true" size={16} />&nbsp;{t("analytics.filters.edit")}
          </IonButton>
        )}
      >
        {t("analytics.filters.title")}
      </SectionHeader>

      <ul className="pm-chips pm-padded" aria-label={t("analytics.filters.chipsLabel")}>
        {chips.map((chip) => (
          <li key={chip.key} className="pm-chip pm-plan-chip" data-kind={chip.kind}>
            <span title={chip.label}>{chip.label}</span>
            {chip.without && (
              <button
                type="button"
                className="pm-chip-remove"
                onClick={() => onChange(chip.without!)}
                disabled={busy}
                aria-label={t("analytics.filters.remove", { filter: chip.label })}
              >
                <X aria-hidden="true" size={14} />
              </button>
            )}
          </li>
        ))}
      </ul>

      {editing && (
        <PlanEditor
          plan={plan}
          merchants={merchants}
          tags={tags}
          busy={busy}
          onClose={() => setEditing(false)}
          onApply={(next) => {
            setEditing(false);
            onChange(next);
          }}
        />
      )}
    </section>
  );
}

function PlanEditor({ plan, merchants, tags, busy, onApply, onClose }: {
  plan: AnalyticsPlan;
  merchants: string[];
  tags: ProductTag[];
  busy: boolean;
  onApply: (plan: AnalyticsPlan) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [metric, setMetric] = useState(plan.metric);
  const [from, setFrom] = useState(plan.from ?? "");
  const [to, setTo] = useState(plan.to ?? "");
  const [merchant, setMerchant] = useState(plan.merchants[0] ?? "");
  const [groupBy, setGroupBy] = useState(plan.groupBy);
  const [discount, setDiscount] = useState(plan.discount);
  const planTagIds = plan.tagIds ?? [];
  // Null until the field is touched, so a plan with several tags keeps them all when only the period changes.
  const [tag, setTag] = useState<string | null>(null);

  const apply = () => onApply({
    ...plan,
    metric,
    from: from || null,
    to: to || null,
    merchants: merchant ? [merchant] : [],
    tagIds: tag === null ? planTagIds : tag ? [tag] : [],
    groupBy,
    discount,
    limit: metric === "top" ? plan.limit : null
  });

  // Stores the plan already names stay selectable even if they are rare.
  const merchantOptions = Array.from(new Set([...plan.merchants, ...merchants]));
  const showTags = tags.length > 0 || planTagIds.length > 0 || plan.groupBy === "tag";
  const tagOptions = [
    ...tags,
    // A tag the plan names but the list no longer has (just deleted) stays visible rather than vanishing.
    ...planTagIds.filter((tagId) => !tags.some((entry) => entry.id === tagId)).map((tagId) => ({ id: tagId, name: t("analytics.chips.unknownTag") }))
  ];
  const tagValue = tag ?? planTagIds[0] ?? "";

  return (
    <Sheet
      isOpen
      onClose={onClose}
      title={t("analytics.filters.title")}
      variant="full"
      start={<IonButton onClick={onClose}>{t("ui.cancel")}</IonButton>}
      end={<ActionButton strong loading={busy} onClick={apply}>{t("analytics.filters.apply")}</ActionButton>}
    >
      <IonList inset>
        <IonItem>
          <IonSelect label={t("analytics.filters.metric")} interface="action-sheet" cancelText={t("ui.cancel")} value={metric} onIonChange={(event) => setMetric(event.detail.value as AnalyticsMetric)}>
            {metrics.map((value) => <IonSelectOption key={value} value={value}>{t(`analytics.metrics.${value}`)}</IonSelectOption>)}
          </IonSelect>
        </IonItem>
        <IonItem>
          <IonSelect label={t("analytics.filters.groupBy")} interface="action-sheet" cancelText={t("ui.cancel")} value={groupBy} onIonChange={(event) => setGroupBy(event.detail.value as AnalyticsGroupBy)}>
            {groupings
              .filter((value) => value !== "tag" || showTags)
              .map((value) => <IonSelectOption key={value} value={value}>{t(`analytics.groupBy.${value}`)}</IonSelectOption>)}
          </IonSelect>
        </IonItem>
        <IonItem lines="none">
          <IonSelect label={t("analytics.filters.discount")} interface="action-sheet" cancelText={t("ui.cancel")} value={discount} onIonChange={(event) => setDiscount(event.detail.value as AnalyticsDiscountFilter)}>
            {discounts.map((value) => <IonSelectOption key={value} value={value}>{t(`analytics.discount.${value}`)}</IonSelectOption>)}
          </IonSelect>
        </IonItem>
      </IonList>

      <IonList inset>
        <DateField label={t("analytics.filters.from")} value={from} max={to || undefined} onChange={setFrom} />
        <DateField label={t("analytics.filters.to")} value={to} min={from || undefined} onChange={setTo} />
      </IonList>

      <IonList inset>
        <SelectRow
          label={t("analytics.filters.merchant")}
          valueText={merchant || t("analytics.chips.allMerchants")}
          value={merchant}
          onChange={setMerchant}
          searchable={merchantOptions.length > 8}
          groups={[{ options: [{ value: "", label: t("analytics.chips.allMerchants") }, ...merchantOptions.map((value) => ({ value, label: value }))] }]}
        />
        {showTags && (
          <SelectRow
            label={t("analytics.filters.tag")}
            valueText={tagOptions.find((entry) => entry.id === tagValue)?.name ?? t("analytics.filters.allTags")}
            value={tagValue}
            onChange={setTag}
            groups={[{ options: [{ value: "", label: t("analytics.filters.allTags") }, ...tagOptions.map((entry) => ({ value: entry.id, label: entry.name }))] }]}
          />
        )}
      </IonList>
      <IonList inset>
        <IonItem button detail={false} lines="none" disabled={busy} onClick={apply}>
          <IonLabel color="primary">{t("analytics.filters.apply")}</IonLabel>
        </IonItem>
      </IonList>
    </Sheet>
  );
}
