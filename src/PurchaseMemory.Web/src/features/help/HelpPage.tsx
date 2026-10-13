import { IonButton, IonItem, IonLabel, IonList, IonSearchbar } from "@ionic/react";
import { Fragment, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation } from "react-router";
import { ItemLink, Page, SectionHeader } from "../../design-system/components";
import type { HelpTopicText } from "./helpText";
import { helpGroupIds, helpTopics, isHelpTopicId, type HelpTopic } from "./topics";

const contentsId = "help-contents";
const topicsId = "help-topics";

function normalize(value: string) {
  return value.toLocaleLowerCase().normalize("NFD").replace(/\p{Diacritic}/gu, "");
}

/** Scrolls inside Ionic's content to an element and gives its heading focus. */
function revealTopic(id: string) {
  const heading = document.getElementById(`${id}-title`);
  heading?.scrollIntoView?.({ block: "start" });
  heading?.focus({ preventScroll: true });
}

/**
 * «Подсказки»: a search, the contents as grouped rows, then every topic as a
 * card. A «Как это работает» link lands here as `/help#topic`, which scrolls
 * to that topic and focuses its heading.
 */
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

  // The router does not scroll to hashes, so the topic is brought into view here.
  const hash = decodeURIComponent(location.hash.replace(/^#/, ""));
  useEffect(() => {
    if (!isHelpTopicId(hash)) return;
    setQuery("");
    const frame = requestAnimationFrame(() => revealTopic(hash));
    return () => cancelAnimationFrame(frame);
  }, [hash, location.key]);

  const groups = helpGroupIds
    .map((group) => ({ group, topics: visible.filter((topic) => topic.group === group) }))
    .filter((entry) => entry.topics.length > 0);

  return (
    <Page
      title={t("help.ui.title")}
      back={{ to: "/", label: t("ui.back"), preferHistory: true }}
      search={(
        <IonSearchbar
          value={query}
          placeholder={t("help.ui.searchPlaceholder")}
          aria-label={t("help.ui.searchLabel")}
          onIonInput={(event) => setQuery(event.detail.value ?? "")}
        />
      )}
    >
      <a className="visually-hidden pm-skip-link" href={`#${topicsId}`}>{t("help.ui.skipToTopics")}</a>
      <p className="pm-home-intro">{t("help.ui.intro")}</p>

      <nav id={contentsId} aria-label={t("help.ui.contentsLabel")}>
        {groups.map(({ group, topics }) => (
          <Fragment key={group}>
            <SectionHeader>{t(`help.groups.${group}`)}</SectionHeader>
            <IonList inset className="pm-after-header">
              {topics.map((topic) => (
                <IonItem
                  key={topic.id}
                  href={`#${topic.id}`}
                  detail
                  onClick={(event) => {
                    // Same address again (a second tap) still scrolls.
                    if (location.hash === `#${topic.id}`) { event.preventDefault(); revealTopic(topic.id); }
                  }}
                >
                  <IonLabel className="pm-wrap">{texts.get(topic.id)!.title}</IonLabel>
                </IonItem>
              ))}
            </IonList>
          </Fragment>
        ))}
      </nav>

      <div id={topicsId} tabIndex={-1}>
        {groups.length === 0 && (
          <div className="pm-empty" role="status">
            <p>{t("help.ui.noResults")}</p>
            <IonButton fill="clear" onClick={() => setQuery("")}>{t("help.ui.resetSearch")}</IonButton>
          </div>
        )}
        {groups.map(({ group, topics }) => (
          <section key={group} aria-labelledby={`help-group-${group}`}>
            <h2 id={`help-group-${group}`} className="visually-hidden">{t(`help.groups.${group}`)}</h2>
            {topics.map((topic) => <TopicCard key={topic.id} topic={topic} text={texts.get(topic.id)!} />)}
          </section>
        ))}
      </div>
    </Page>
  );
}

function TopicCard({ topic, text }: { topic: HelpTopic; text: HelpTopicText }) {
  const { t } = useTranslation();
  return (
    <article id={topic.id} className="pm-help-topic" aria-labelledby={`${topic.id}-title`}>
      <h3 id={`${topic.id}-title`} tabIndex={-1}>{text.title}</h3>
      <p className="pm-help-summary">{text.summary}</p>
      <TopicList title={t("help.ui.stepsTitle")} items={text.steps} ordered />
      <TopicList title={t("help.ui.seeTitle")} items={text.see} />
      <TopicList title={t("help.ui.troubleTitle")} items={text.trouble} />
      {topic.links.length > 0 && (
        <>
          <h4>{t("help.ui.linksTitle")}</h4>
          <IonList lines="full" className="pm-help-links">
            {topic.links.map((link) => (
              <ItemLink key={link.to} to={link.to}><IonLabel>{t(`help.links.${link.id}`)}</IonLabel></ItemLink>
            ))}
          </IonList>
        </>
      )}
      <a className="pm-help-back" href={`#${contentsId}`}>{t("help.ui.backToContents")}</a>
    </article>
  );
}

function TopicList({ title, items, ordered = false }: { title: string; items: string[]; ordered?: boolean }) {
  if (items.length === 0) return null;
  const List = ordered ? "ol" : "ul";
  return (
    <>
      <h4>{title}</h4>
      <List>{items.map((item) => <li key={item}>{item}</li>)}</List>
    </>
  );
}
