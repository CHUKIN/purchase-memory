import { IonButton, IonItem, IonLabel, IonSpinner } from "@ionic/react";
import { Check, Hash, Sparkles, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ApiError } from "../../api/client";
import type { TagSuggestion, TagSuggestionsResponse } from "../../api/types";

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

/** Focuses an Ionic control (its native button lives in shadow DOM) or a plain element. */
function focusControl(element: Element | null | undefined) {
  const target = (element?.shadowRoot?.querySelector("button") ?? element) as HTMLElement | null | undefined;
  target?.focus();
}

/**
 * «Предложить теги»: the AI picks from the household's own tags and the person
 * accepts or dismisses each hint. Nothing is applied without a tap on
 * «Добавить», and new tags are never invented — that stays a person's job.
 * Rendered as rows inside the tags list.
 */
export function TagSuggestions({ onSuggest, onAccept, currentTagIds, remainingSlots = Number.POSITIVE_INFINITY, initialState }: TagSuggestionsProps) {
  const { t } = useTranslation();
  const [state, setState] = useState<SuggestionState>(initialState ?? { kind: "idle" });
  const [accepting, setAccepting] = useState(false);
  const [acceptError, setAcceptError] = useState(false);
  // Where focus goes after «Добавить»/«Скрыть» removes the focused row: the
  // next suggestion, or the «Предложить» row when none are left.
  const [focusTarget, setFocusTarget] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // A tag added meanwhile (by hand or by accepting) is no longer a suggestion.
  const visible = state.kind === "shown"
    ? state.suggestions.filter((suggestion) => !currentTagIds.includes(suggestion.tagId))
    : [];

  useEffect(() => {
    // Wait until the accept has settled: a still-disabled button cannot take focus.
    if (focusTarget === null || accepting) return;
    const container = containerRef.current;
    const row = container?.querySelector(`[data-suggestion="${CSS.escape(focusTarget)}"]`);
    focusControl(row
      ? row.querySelector("[data-accept]:not([disabled])") ?? row.querySelector("[data-dismiss]")
      : container?.querySelector("[data-suggest]"));
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
  const message = loading
    ? t("tags.suggesting")
    : state.kind === "empty"
      ? t("tags.suggestionsEmpty")
      : state.kind === "unavailable"
        ? t("tags.suggestionsUnavailable")
        : state.kind === "rateLimited"
          ? state.retryAfterSeconds
            ? t("tags.suggestionsRateLimited", { minutes: Math.max(1, Math.ceil(state.retryAfterSeconds / 60)) })
            : t("tags.suggestionsRateLimitedLater")
          : null;

  return (
    <div ref={containerRef}>
      <IonItem button detail={false} data-suggest disabled={loading || accepting} lines={visible.length > 0 || message ? "full" : "none"} onClick={() => void suggest()}>
        {loading ? <IonSpinner slot="start" name="crescent" aria-hidden="true" /> : <Sparkles slot="start" size={20} className="pm-row-icon" aria-hidden="true" />}
        <IonLabel color="primary">{hasResult ? t("tags.suggestAgain") : t("tags.suggest")}</IonLabel>
      </IonItem>

      <div aria-live="polite">
        {message && <IonItem lines={visible.length > 0 ? "full" : "none"}><IonLabel className="pm-wrap" color="medium"><p>{message}</p></IonLabel></IonItem>}
        {visible.length > 0 && <p className="visually-hidden">{t("tags.suggestionsFound", { count: visible.length })}</p>}
      </div>

      {visible.length > 0 && (
        <section aria-label={t("tags.suggestionsTitle")}>
          <div role="list" aria-label={t("tags.suggestionsListLabel")}>
            {visible.map((suggestion) => (
              <IonItem key={suggestion.tagId} role="listitem" data-suggestion={suggestion.tagId}>
                <IonLabel className="pm-wrap">
                  <span className="pm-chip pm-ai" title={suggestion.name}>
                    <Hash aria-hidden="true" size={11} strokeWidth={2.5} />
                    <span>{suggestion.name}</span>
                  </span>
                  {suggestion.reason && <p>{suggestion.reason}</p>}
                </IonLabel>
                <IonButton
                  slot="end"
                  size="small"
                  fill="outline"
                  data-accept
                  disabled={accepting || remainingSlots < 1}
                  aria-label={t("tags.acceptLabel", { name: suggestion.name })}
                  onClick={() => void accept([suggestion.tagId])}
                >
                  <Check aria-hidden="true" size={16} />&nbsp;{t("tags.accept")}
                </IonButton>
                <IonButton
                  slot="end"
                  size="small"
                  fill="clear"
                  color="medium"
                  data-dismiss
                  disabled={accepting}
                  aria-label={t("tags.dismissLabel", { name: suggestion.name })}
                  onClick={() => dismiss(suggestion.tagId)}
                >
                  <X aria-hidden="true" size={18} />
                </IonButton>
              </IonItem>
            ))}
          </div>
          {visible.length > 1 && (
            <IonItem button detail={false} disabled={accepting || visible.length > remainingSlots} onClick={() => void accept(visible.map((suggestion) => suggestion.tagId))}>
              <Check slot="start" size={20} className="pm-row-icon" aria-hidden="true" />
              <IonLabel color="primary">{t("tags.acceptAll")}</IonLabel>
            </IonItem>
          )}
          <IonItem lines="none">
            <IonLabel className="pm-wrap" color="medium">
              <p>{t("tags.suggestionsNote")}</p>
              {acceptError && <p className="pm-danger-text" role="alert">{t("tags.acceptError")}</p>}
            </IonLabel>
          </IonItem>
        </section>
      )}
    </div>
  );
}
