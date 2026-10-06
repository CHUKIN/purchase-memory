import { ChevronDown, MapPin, UserRound } from "lucide-react";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, StatusBadge, Surface, type PurchaseStatus } from "../../design-system/components";
import styles from "./PurchaseCard.module.css";

interface PurchaseLine {
  name: string;
  total: number;
}

export interface PurchaseCardData {
  id: string;
  merchant: string;
  location: string;
  purchasedAt: string;
  total: number;
  currency: string;
  itemCount: number;
  status: Extract<PurchaseStatus, "verified" | "needsAttention" | "possibleDuplicate">;
  shopper: string;
  issueItem?: string;
  items: PurchaseLine[];
}

export interface PurchaseCardProps {
  purchase: PurchaseCardData;
  density?: "comfortable" | "compact";
  initiallyExpanded?: boolean;
}

export function PurchaseCard({ purchase, density = "comfortable", initiallyExpanded = false }: PurchaseCardProps) {
  const { t, i18n } = useTranslation();
  const [expanded, setExpanded] = useState(initiallyExpanded);
  const detailsId = useId();
  const locale = i18n.resolvedLanguage === "ru" ? "ru-RU" : "en-GB";
  const date = new Intl.DateTimeFormat(locale, { dateStyle: density === "compact" ? "medium" : "long", timeStyle: "short" }).format(new Date(purchase.purchasedAt));
  const money = (value: number) => new Intl.NumberFormat(locale, { style: "currency", currency: purchase.currency }).format(value);

  return (
    <Surface as="article" density={density} elevated={density === "comfortable"} className={styles.card}>
      <div className={styles.topline}>
        <StatusBadge status={purchase.status} />
        <time dateTime={purchase.purchasedAt}>{date}</time>
      </div>
      <div className={styles.summary}>
        <div>
          <h3>{purchase.merchant}</h3>
          <span><MapPin aria-hidden="true" size={14} /> {purchase.location}</span>
        </div>
        <strong className={styles.total}>{money(purchase.total)}</strong>
      </div>
      <div className={styles.meta}>
        <span><UserRound aria-hidden="true" size={14} /> {t("purchase.boughtBy", { name: purchase.shopper })}</span>
        <span>{purchase.itemCount} {t("common.itemsShort")}</span>
      </div>
      {purchase.status !== "verified" && (
        <p className={styles.issue}>
          {purchase.status === "possibleDuplicate"
            ? t("purchase.duplicateLine")
            : t("purchase.issueLine", { item: purchase.issueItem ?? purchase.items[0]?.name })}
        </p>
      )}
      {expanded && (
        <ul id={detailsId} className={styles.items}>
          {purchase.items.map((item) => <li key={item.name}><span>{item.name}</span><strong>{money(item.total)}</strong></li>)}
        </ul>
      )}
      <Button
        intent="quiet"
        className={styles.expand}
        aria-expanded={expanded}
        aria-controls={detailsId}
        rightSection={<ChevronDown aria-hidden="true" size={16} className={expanded ? styles.rotated : undefined} />}
        onClick={() => setExpanded((value) => !value)}
      >
        {t(expanded ? "purchase.hideItems" : "purchase.showItems")}
      </Button>
    </Surface>
  );
}
