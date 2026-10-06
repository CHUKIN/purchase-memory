import { AlertTriangle, Home, type LucideIcon } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { EmptyState, Surface } from "../../design-system/components";
import styles from "./PlaceholderPage.module.css";

type PlaceholderKind = "review";

const icons: Record<PlaceholderKind, LucideIcon> = {
  review: AlertTriangle
};

export function PlaceholderPage({ kind }: { kind: PlaceholderKind }) {
  const { t } = useTranslation();
  const Icon = icons[kind];
  const title = t(`placeholder.${kind}Title`);
  const description = t(`placeholder.${kind}Description`);

  return (
    <div className={styles.page}>
      <Surface className={styles.surface}>
        <EmptyState
          icon={<Icon />}
          title={title}
          description={description}
          headingLevel={1}
          action={<Link className={styles.homeLink} to="/"><Home aria-hidden="true" />{t("placeholder.backHome")}</Link>}
        />
      </Surface>
    </div>
  );
}
