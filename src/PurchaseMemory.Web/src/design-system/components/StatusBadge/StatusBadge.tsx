import { Badge } from "@mantine/core";
import { AlertTriangle, Check, CircleCheck, Clock3, Copy, LoaderCircle, Upload } from "lucide-react";
import { useTranslation } from "react-i18next";
import styles from "./StatusBadge.module.css";

export type PurchaseStatus = "uploaded" | "processing" | "ready" | "needsAttention" | "verified" | "possibleDuplicate" | "failed";

const statusMeta = {
  uploaded: { tone: "neutral", Icon: Upload },
  processing: { tone: "progress", Icon: LoaderCircle },
  ready: { tone: "success", Icon: CircleCheck },
  needsAttention: { tone: "warning", Icon: AlertTriangle },
  verified: { tone: "success", Icon: Check },
  possibleDuplicate: { tone: "warning", Icon: Copy },
  failed: { tone: "danger", Icon: Clock3 }
} as const;

export function StatusBadge({ status }: { status: PurchaseStatus }) {
  const { t } = useTranslation();
  const { tone, Icon } = statusMeta[status];
  return (
    <Badge
      variant="light"
      className={`${styles.badge} ${styles[tone]}`}
      leftSection={<Icon aria-hidden="true" size={13} strokeWidth={2.25} className={status === "processing" ? styles.spinning : undefined} />}
    >
      {t(`status.${status}`)}
    </Badge>
  );
}
