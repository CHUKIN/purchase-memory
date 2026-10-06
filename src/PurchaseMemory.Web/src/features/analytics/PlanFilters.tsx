import { SlidersHorizontal, X } from "lucide-react";
import { useId, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import type { AnalyticsDiscountFilter, AnalyticsGroupBy, AnalyticsMetric, AnalyticsPlan, ProductCategory, ProductTag } from "../../api/types";
import { Button } from "../../design-system/components";
import { planChips } from "./analyticsText";
import styles from "./PlanFilters.module.css";

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
 * The server's interpretation as chips. Removing a chip or editing the form
 * re-runs the plan directly, so correcting a misunderstanding never costs a
 * model call.
 */
export function PlanFilters({ plan, categories, merchants, tags = [], productNames, locale, onChange, busy = false }: PlanFiltersProps) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const chips = planChips(t, locale, plan, categories, productNames, tags);
  const formId = useId();

  return (
    <section className={styles.root} aria-labelledby={`${formId}-title`}>
      <div className={styles.header}>
        <h3 id={`${formId}-title`} className={styles.title}>{t("analytics.filters.title")}</h3>
        <Button
          intent="quiet"
          leftSection={<SlidersHorizontal aria-hidden="true" size={16} />}
          aria-expanded={editing}
          aria-controls={`${formId}-form`}
          onClick={() => setEditing((value) => !value)}
          disabled={busy}
        >
          {editing ? t("analytics.filters.close") : t("analytics.filters.edit")}
        </Button>
      </div>

      <ul className={styles.chips} aria-label={t("analytics.filters.chipsLabel")}>
        {chips.map((chip) => (
          <li key={chip.key} className={styles.chip} data-kind={chip.kind}>
            <span className={styles.chipLabel} title={chip.label}>{chip.label}</span>
            {chip.without && (
              <button
                type="button"
                className={styles.remove}
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
          id={`${formId}-form`}
          plan={plan}
          merchants={merchants}
          tags={tags}
          busy={busy}
          onApply={(next) => {
            setEditing(false);
            onChange(next);
          }}
        />
      )}
    </section>
  );
}

function PlanEditor({ id, plan, merchants, tags, busy, onApply }: {
  id: string;
  plan: AnalyticsPlan;
  merchants: string[];
  tags: ProductTag[];
  busy: boolean;
  onApply: (plan: AnalyticsPlan) => void;
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

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onApply({
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
  };

  // Stores the plan already names stay selectable even if they are rare.
  const merchantOptions = Array.from(new Set([...plan.merchants, ...merchants]));
  const showTags = tags.length > 0 || planTagIds.length > 0 || plan.groupBy === "tag";
  const tagOptions = [
    ...tags,
    // A tag the plan names but the list no longer has (just deleted) stays visible rather than vanishing.
    ...planTagIds.filter((tagId) => !tags.some((entry) => entry.id === tagId)).map((tagId) => ({ id: tagId, name: t("analytics.chips.unknownTag") }))
  ];

  return (
    <form id={id} className={styles.editor} onSubmit={submit}>
      <label className={styles.field}>
        <span>{t("analytics.filters.metric")}</span>
        <select value={metric} onChange={(event) => setMetric(event.target.value as AnalyticsMetric)}>
          {metrics.map((value) => <option key={value} value={value}>{t(`analytics.metrics.${value}`)}</option>)}
        </select>
      </label>
      <label className={styles.field}>
        <span>{t("analytics.filters.from")}</span>
        <input type="date" value={from} max={to || undefined} onChange={(event) => setFrom(event.target.value)} />
      </label>
      <label className={styles.field}>
        <span>{t("analytics.filters.to")}</span>
        <input type="date" value={to} min={from || undefined} onChange={(event) => setTo(event.target.value)} />
      </label>
      <label className={styles.field}>
        <span>{t("analytics.filters.merchant")}</span>
        <select value={merchant} onChange={(event) => setMerchant(event.target.value)}>
          <option value="">{t("analytics.chips.allMerchants")}</option>
          {merchantOptions.map((value) => <option key={value} value={value}>{value}</option>)}
        </select>
      </label>
      {showTags && (
        <label className={styles.field}>
          <span>{t("analytics.filters.tag")}</span>
          <select value={tag ?? planTagIds[0] ?? ""} onChange={(event) => setTag(event.target.value)}>
            <option value="">{t("analytics.filters.allTags")}</option>
            {tagOptions.map((entry) => <option key={entry.id} value={entry.id}>{entry.name}</option>)}
          </select>
        </label>
      )}
      <label className={styles.field}>
        <span>{t("analytics.filters.groupBy")}</span>
        <select value={groupBy} onChange={(event) => setGroupBy(event.target.value as AnalyticsGroupBy)}>
          {groupings
            .filter((value) => value !== "tag" || showTags)
            .map((value) => <option key={value} value={value}>{t(`analytics.groupBy.${value}`)}</option>)}
        </select>
      </label>
      <label className={styles.field}>
        <span>{t("analytics.filters.discount")}</span>
        <select value={discount} onChange={(event) => setDiscount(event.target.value as AnalyticsDiscountFilter)}>
          {discounts.map((value) => <option key={value} value={value}>{t(`analytics.discount.${value}`)}</option>)}
        </select>
      </label>
      <div className={styles.actions}>
        <Button type="submit" loading={busy}>{t("analytics.filters.apply")}</Button>
      </div>
    </form>
  );
}
