import { Check, Hash, Sparkles, X } from "lucide-react";
import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { TagSuggestion, TagSuggestionsResponse } from "../../api/types";
import { Button, IconButton } from "../../design-system/components";
import styles from "./Tags.module.css";

export interface TagSuggestionsProps {
  /** Asks the server which existing household tags fit; never applies anything. */
  onSuggest: () => Promise<TagSuggestionsResponse>;
  /** Adds these tags to the product (the usual product-tags save). */
  onAccept: (tagIds: string[]) => Promise<unknown>;
  /** Tags the product already has; a suggestion for one of them disappears. */
  currentTagIds: string[];
  /** How many more tags the product may take; accepting past the limit is disabled. */
  remainingSlots?: number;
  /** Starts with these results shown; for stories and tests. */
  initialState?: SuggestionState;
}

const SuggestButton = "\u0000suggest";

export type SuggestionState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "shown"; suggestions: TagSuggestion[] }
  | { kind: "empty" }
  | { kind: "unavailable" }
  | { kind: "rateLimited"; retryAfterSeconds: number | null };

/**
 * «Предложить теги»: the AI picks from the household's own tags and the person
 * accepts or dismisses each hint. Nothing is applied without a press on
 * «Добавить», and new tags are never invented — that stays a person's job.
 */
export function TagSuggestions({ onSuggest, onAccept, currentTagIds, remainingSlots = Number.POSITIVE_INFINITY, initialState }: TagSuggestionsProps) {
  const { t } = useTranslation();
  const titleId = useId();
  const [state, setState] = useState<SuggestionState>(initialState ?? { kind: "idle" });
  const [accepting, setAccepting] = useState(false);
  const [acceptError, setAcceptError] = useState(false);
  // Where focus goes after «Добавить»/«Скрыть» removes the focused row: the
  // next suggestion, or the «Предложить» button when none are left.
  const [focusTarget, setFocusTarget] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const rowRefs = useRef(new Map<string, HTMLLIElement>());

  // A tag added meanwhile (by hand or by accepting) is no longer a suggestion.
  const visible = state.kind === "shown"
    ? state.suggestions.filter((suggestion) => !currentTagIds.includes(suggestion.tagId))
    : [];

  useEffect(() => {
    // Wait until the accept has settled: a still-disabled button cannot take focus.
    if (focusTarget === null || accepting) return;
    const row = rowRefs.current.get(focusTarget);
    const target = row
      ? row.querySelector<HTMLButtonElement>("button:not(:disabled)")
      : containerRef.current?.querySelector<HTMLButtonElement>("button[data-suggest]");
    target?.focus();
    setFocusTarget(null);
  }, [focusTarget, accepting]);

  /** The row that takes focus once `removed` disappear: the next one, else the previous, else the button. */
  function nextFocus(removed: string[]): string {
    const index = visible.findIndex((suggestion) => removed.includes(suggestion.tagId));
    const rest = visible.filter((suggestion) => !removed.includes(suggestion.tagId));
    if (rest.length === 0) return SuggestButton;
    return rest[Math.min(Math.max(index, 0), rest.length - 1)].tagId;
  }

  async function suggest() {
    setState({ kind: "loading" });
    setAcceptError(false);
    try {
      const result = await onSuggest();
      const fresh = result.suggestions.filter((suggestion) => !currentTagIds.includes(suggestion.tagId));
      setState(fresh.length > 0 ? { kind: "shown", suggestions: fresh } : { kind: "empty" });
    } catch (failure) {
      setState(failure instanceof ApiError && failure.status === 429
        ? { kind: "rateLimited", retryAfterSeconds: failure.retryAfterSeconds }
        : { kind: "unavailable" });
    }
  }

  function dismiss(tagId: string) {
    if (state.kind !== "shown") return;
    const rest = state.suggestions.filter((suggestion) => suggestion.tagId !== tagId);
    setFocusTarget(nextFocus([tagId]));
    setState(rest.length > 0 ? { kind: "shown", suggestions: rest } : { kind: "idle" });
  }

  async function accept(tagIds: string[]) {
    setAccepting(true);
    setAcceptError(false);
    try {
      await onAccept(tagIds);
      setFocusTarget(nextFocus(tagIds));
      if (state.kind === "shown") {
        const rest = state.suggestions.filter((suggestion) => !tagIds.includes(suggestion.tagId));
        setState(rest.length > 0 ? { kind: "shown", suggestions: rest } : { kind: "idle" });
      }
    } catch {
      setAcceptError(true);
    } finally {
      setAccepting(false);
    }
  }

  const loading = state.kind === "loading";
  const hasResult = state.kind !== "idle" && state.kind !== "loading";

  return (
    <div className={styles.suggestions} ref={containerRef}>
      <div>
        <Button
          data-suggest
          intent="secondary"
          loading={loading}
          disabled={accepting}
          leftSection={<Sparkles aria-hidden="true" size={16} />}
          onClick={() => void suggest()}
        >
          {hasResult ? t("tags.suggestAgain") : t("tags.suggest")}
        </Button>
      </div>

      <div aria-live="polite" className={styles.suggestionStatus}>
        {loading && <p className={styles.hint}>{t("tags.suggesting")}</p>}
        {visible.length > 0 && (
          <p className="visually-hidden">{t("tags.suggestionsFound", { count: visible.length })}</p>
        )}
        {state.kind === "empty" && <p className={styles.hint}>{t("tags.suggestionsEmpty")}</p>}
        {state.kind === "unavailable" && <p className={styles.hint}>{t("tags.suggestionsUnavailable")}</p>}
        {state.kind === "rateLimited" && (
          <p className={styles.hint}>
            {state.retryAfterSeconds
              ? t("tags.suggestionsRateLimited", { minutes: Math.max(1, Math.ceil(state.retryAfterSeconds / 60)) })
              : t("tags.suggestionsRateLimitedLater")}
          </p>
        )}
      </div>

      {visible.length > 0 && (
        <section className={styles.suggestionPanel} aria-labelledby={titleId}>
          <p id={titleId} className={styles.suggestionTitle}>
            <Sparkles aria-hidden="true" size={14} />
            {t("tags.suggestionsTitle")}
          </p>
          <ul className={styles.suggestionList} aria-label={t("tags.suggestionsListLabel")}>
            {visible.map((suggestion) => (
              <li
                key={suggestion.tagId}
                className={styles.suggestionItem}
                ref={(element) => {
                  if (element) rowRefs.current.set(suggestion.tagId, element);
                  else rowRefs.current.delete(suggestion.tagId);
                }}
              >
                <div className={styles.suggestionText}>
                  <span className={styles.chip} title={suggestion.name}>
                    <Hash aria-hidden="true" size={12} strokeWidth={2.5} />
                    <span className={styles.chipLabel}>{suggestion.name}</span>
                  </span>
                  {suggestion.reason && <span className={styles.suggestionReason}>{suggestion.reason}</span>}
                </div>
                <div className={styles.suggestionActions}>
                  <IconButton
                    intent="secondary"
                    iconOnly="mobile"
                    icon={<Check />}
                    label={t("tags.acceptLabel", { name: suggestion.name })}
                    text={t("tags.accept")}
                    tooltip={t("tags.accept")}
                    disabled={accepting || remainingSlots < 1}
                    onClick={() => void accept([suggestion.tagId])}
                  />
                  <IconButton
                    icon={<X />}
                    label={t("tags.dismissLabel", { name: suggestion.name })}
                    tooltip={t("tags.dismiss")}
                    disabled={accepting}
                    onClick={() => dismiss(suggestion.tagId)}
                  />
                </div>
              </li>
            ))}
          </ul>
          {visible.length > 1 && (
            <div className={styles.actions}>
              <Button
                intent="quiet"
                loading={accepting}
                disabled={visible.length > remainingSlots}
                leftSection={<Check aria-hidden="true" size={16} />}
                onClick={() => void accept(visible.map((suggestion) => suggestion.tagId))}
              >
                {t("tags.acceptAll")}
              </Button>
            </div>
          )}
          <p className={styles.hint}>{t("tags.suggestionsNote")}</p>
          {acceptError && <p className={styles.error} role="alert">{t("tags.acceptError")}</p>}
        </section>
      )}
    </div>
  );
}
