import { CircleHelp } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { helpPath, type HelpTopicId } from "./topics";
import styles from "./Help.module.css";

/** The small «Как это работает» entry point that opens one help topic. */
export function HelpLink({ topic, className }: { topic: HelpTopicId; className?: string }) {
  const { t } = useTranslation();
  const title = t(`help.topics.${topic}.title`);
  return (
    <Link
      className={[styles.contextLink, className].filter(Boolean).join(" ")}
      to={helpPath(topic)}
      aria-label={t("help.ui.contextLinkFor", { topic: title })}
      data-help-topic={topic}
    >
      <CircleHelp aria-hidden="true" size={16} />
      <span>{t("help.ui.contextLink")}</span>
    </Link>
  );
}
