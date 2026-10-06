import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useLocation } from "react-router";
import { Surface } from "../../design-system/components";
import type { HelpTopicText } from "./helpText";
import { helpGroupIds, helpTopics, isHelpTopicId, type HelpTopic } from "./topics";
import styles from "./Help.module.css";

const contentsId = "help-contents";
const topicsId = "help-topics";

function normalize(value: string) {
  return value.toLocaleLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

export function HelpPage() {
  const { t } = useTranslation();
  const location = useLocation();
  const [query, setQuery] = useState("");

  const texts = useMemo(
    () => new Map(helpTopics.map((topic) => [topic.id, t(`help.topics.${topic.id}`, { returnObjects: true }) as unknown as HelpTopicText])),
    [t]
  );

  const needle = normalize(query.trim());
  const visible = useMemo(() => helpTopics.filter((topic) => {
    if (!needle) return true;
    const text = texts.get(topic.id)!;
    return normalize([text.title, text.summary, ...text.steps, ...text.see, ...text.trouble].join(" ")).includes(needle);
  }), [needle, texts]);

  // A contextual link lands here as /help#topic. The router does not scroll to
  // hashes, so the topic is brought into view and its heading takes focus.
  const hash = decodeURIComponent(location.hash.replace(/^#/, ""));
  useEffect(() => {
    if (!isHelpTopicId(hash)) return;
    setQuery("");
    const frame = requestAnimationFrame(() => {
      const heading = document.getElementById(`${hash}-title`);
      heading?.scrollIntoView?.({ block: "start" });
      heading?.focus({ preventScroll: true });
    });
    return () => cancelAnimationFrame(frame);
  }, [hash, location.key]);

  const groups = helpGroupIds
    .map((group) => ({ group, topics: visible.filter((topic) => topic.group === group) }))
    .filter((entry) => entry.topics.length > 0);

  return (
    <div className={styles.page}>
      <a className={styles.skipLink} href={`#${topicsId}`}>{t("help.ui.skipToTopics")}</a>
      <header className={styles.hero}>
        <p className={styles.eyebrow}>{t("help.ui.eyebrow")}</p>
        <h1>{t("help.ui.title")}</h1>
        <p className={styles.intro}>{t("help.ui.intro")}</p>
      </header>

      <div className={styles.layout}>
        <div className={styles.sidebar}>
          <div className={styles.search} role="search">
            <label htmlFor="help-search">{t("help.ui.searchLabel")}</label>
            <input
              id="help-search"
              type="search"
              value={query}
              placeholder={t("help.ui.searchPlaceholder")}
              onChange={(event) => setQuery(event.target.value)}
            />
          </div>
          <nav id={contentsId} className={styles.contents} aria-label={t("help.ui.contentsLabel")}>
            {groups.map(({ group, topics }) => (
              <div key={group}>
                <p className={styles.contentsGroup}>{t(`help.groups.${group}`)}</p>
                <ul>
                  {topics.map((topic) => <li key={topic.id}><a href={`#${topic.id}`}>{texts.get(topic.id)!.title}</a></li>)}
                </ul>
              </div>
            ))}
          </nav>
        </div>

        <div id={topicsId} className={styles.topics} tabIndex={-1}>
          {groups.length === 0 && (
            <Surface className={styles.empty} role="status">
              <p>{t("help.ui.noResults")}</p>
              <button type="button" onClick={() => setQuery("")}>{t("help.ui.resetSearch")}</button>
            </Surface>
          )}
          {groups.map(({ group, topics }) => (
            <section key={group} className={styles.group} aria-labelledby={`help-group-${group}`}>
              <h2 id={`help-group-${group}`}>{t(`help.groups.${group}`)}</h2>
              {topics.map((topic) => <TopicCard key={topic.id} topic={topic} text={texts.get(topic.id)!} />)}
            </section>
          ))}
        </div>
      </div>
    </div>
  );
}

function TopicCard({ topic, text }: { topic: HelpTopic; text: HelpTopicText }) {
  const { t } = useTranslation();
  return (
    <Surface as="article" id={topic.id} className={styles.topic} aria-labelledby={`${topic.id}-title`}>
      <h3 id={`${topic.id}-title`} tabIndex={-1}>{text.title}</h3>
      <p className={styles.summary}>{text.summary}</p>
      <TopicList title={t("help.ui.stepsTitle")} items={text.steps} ordered />
      <TopicList title={t("help.ui.seeTitle")} items={text.see} />
      <TopicList title={t("help.ui.troubleTitle")} items={text.trouble} />
      <div className={styles.topicFooter}>
        {topic.links.length > 0 && (
          <div className={styles.deepLinks}>
            <span className={styles.deepLinksLabel}>{t("help.ui.linksTitle")}:</span>
            {topic.links.map((link) => <Link key={link.to} to={link.to}>{t(`help.links.${link.id}`)}</Link>)}
          </div>
        )}
        <a className={styles.backLink} href={`#${contentsId}`}>{t("help.ui.backToContents")}</a>
      </div>
    </Surface>
  );
}

function TopicList({ title, items, ordered = false }: { title: string; items: string[]; ordered?: boolean }) {
  if (items.length === 0) return null;
  const List = ordered ? "ol" : "ul";
  return (
    <div className={styles.block}>
      <h4>{title}</h4>
      <List>{items.map((item) => <li key={item}>{item}</li>)}</List>
    </div>
  );
}
