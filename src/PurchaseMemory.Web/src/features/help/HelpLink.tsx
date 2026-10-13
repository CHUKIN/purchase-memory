import { CircleHelp } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { helpPath, type HelpTopicId } from "./topics";

/** The small «Как это работает» link under a list that opens one help topic. */
export function HelpLink({ topic }: { topic: HelpTopicId }) {
  const { t } = useTranslation();
  const title = t(`help.topics.${topic}.title`);
  return (
    <Link
      className="pm-help-link"
      to={helpPath(topic)}
      aria-label={t("help.ui.contextLinkFor", { topic: title })}
      data-help-topic={topic}
    >
      <CircleHelp aria-hidden="true" size={16} />
      <span>{t("help.ui.contextLink")}</span>
    </Link>
  );
}
