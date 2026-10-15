import { CircleHelp } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { helpPath, type HelpTopicId } from "./topics";

/**
 * A small «?» that opens one help topic. Icon only; the accessible name keeps
 * the full «Как это работает: <тема>». Sits in a toolbar or a section header.
 */
export function HelpLink({ topic }: { topic: HelpTopicId }) {
  const { t } = useTranslation();
  const title = t(`help.topics.${topic}.title`);
  const label = t("help.ui.contextLinkFor", { topic: title });
  return (
    <Link className="pm-help-link" to={helpPath(topic)} aria-label={label} data-help-topic={topic}>
      <CircleHelp aria-hidden="true" size={22} />
    </Link>
  );
}
