import { IonButton, IonItem } from "@ionic/react";
import { ChevronLeft } from "lucide-react";
import type { ComponentProps, MouseEvent } from "react";
import { useHref, useLinkClickHandler, useLocation, useNavigate, type To } from "react-router";
import { afterLayerSteps, backFromLayer, leaveScreenWithLayers, shownLayerSteps } from "../../history/historyLayers";

/*
 * Ionic's router integration (@ionic/react-router) supports React Router 6
 * only, and this app is on React Router 8. So Ionic items and buttons get a
 * real `href` (a genuine link for long-press, new tab and screen readers) and
 * a click handler that hands same-tab clicks to React Router instead of
 * reloading the page.
 */
function useSpaLink(to: To, state?: unknown, replace?: boolean) {
  const href = useHref(to);
  const handle = useLinkClickHandler<HTMLElement>(to, { state, replace });
  return {
    href,
    onClick: (event: MouseEvent<HTMLElement>) => {
      if (!event.defaultPrevented) handle(event as unknown as MouseEvent<HTMLAnchorElement>);
    }
  };
}

type ItemProps = Omit<ComponentProps<typeof IonItem>, "href" | "routerLink" | "button">;

/** A list row that opens another screen. */
export function ItemLink({ to, state, replace, children, detail = true, onClick, ...props }: ItemProps & { to: To; state?: unknown; replace?: boolean }) {
  const link = useSpaLink(to, state, replace);
  return (
    <IonItem {...props} detail={detail} href={link.href} onClick={(event: MouseEvent<HTMLIonItemElement>) => { onClick?.(event); link.onClick(event); }}>
      {children}
    </IonItem>
  );
}

type ButtonProps = Omit<ComponentProps<typeof IonButton>, "href" | "routerLink">;

/** An Ionic button that is really a link to another screen. */
export function ButtonLink({ to, state, children, onClick, ...props }: ButtonProps & { to: To; state?: unknown }) {
  const link = useSpaLink(to, state);
  return (
    <IonButton {...props} href={link.href} onClick={(event: MouseEvent<HTMLIonButtonElement>) => { onClick?.(event); link.onClick(event); }}>
      {children}
    </IonButton>
  );
}

export interface BackTarget {
  /** Where «назад» goes when the screen was opened directly (a link, a reload). */
  to: To;
  /** The previous screen's name: the accessible name of the back chevron. */
  label: string;
  state?: unknown;
  /** Step back through history when there is history inside the app. Only for a generic «Назад» label. */
  preferHistory?: boolean;
  /**
   * The history position of the screen named in `label`. When the current
   * entry sits after it, «назад» steps back exactly there (keeping its
   * scroll); otherwise it opens `to`.
   */
  historyIndex?: number;
}

/**
 * React Router's browser history numbers its entries (`history.state.idx`).
 * It is not public API, so a missing index just means «open the address».
 */
export function browserHistoryIndex(): number | null {
  const state: unknown = typeof window === "undefined" ? null : window.history.state;
  if (typeof state !== "object" || state === null || !("idx" in state)) return null;
  const index = (state as { idx: unknown }).idx;
  return typeof index === "number" ? index : null;
}

/**
 * Goes back as the back chevron does: one step. An open sheet or inner screen
 * (see historyLayers.ts) is that step and closes; otherwise a history step to
 * the named screen when there is one (keeping its scroll), else its address.
 */
export function useBack(target: BackTarget, { leaveLayers = false }: {
  /** Leave the screen itself, its open layers with it (a «Готово» that ends the screen). */
  leaveLayers?: boolean;
} = {}): () => void {
  const navigate = useNavigate();
  const location = useLocation();
  const canGoBack = target.preferHistory === true && location.key !== "default";
  return () => {
    // Runs once pending layer steps are done, so the history index is current.
    const leaveScreen = () => {
      const current = browserHistoryIndex();
      const historyStep = target.historyIndex !== undefined && current !== null && current > target.historyIndex;
      // Layers left together with the screen are not asked to close (they would react on the way out).
      if (leaveLayers && (historyStep || canGoBack)) leaveScreenWithLayers();
      if (historyStep) navigate(target.historyIndex! - current!);
      else if (canGoBack) navigate(-1 - shownLayerSteps());
      else navigate(target.to, { state: target.state });
    };
    if (leaveLayers) afterLayerSteps(leaveScreen);
    else backFromLayer(leaveScreen);
  };
}

export function BackButton({ target }: { target: BackTarget }) {
  const href = useHref(target.to);
  const goBack = useBack(target);
  // Only the chevron shows: a long screen title needs the room once it collapses
  // into the bar. The previous screen's name stays the accessible name.
  return (
    <IonButton
      className="pm-icon-button pm-back-button"
      href={href}
      onClick={(event: MouseEvent<HTMLElement>) => {
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        goBack();
      }}
    >
      <ChevronLeft size={28} aria-hidden="true" className="pm-back-chevron" />
      <span className="visually-hidden">{target.label}</span>
    </IonButton>
  );
}
