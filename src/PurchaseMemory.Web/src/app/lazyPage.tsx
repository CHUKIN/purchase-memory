import { IonButton } from "@ionic/react";
import { Component, lazy, type ComponentType, type ErrorInfo, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Notice, Page } from "../design-system/components";

const reloadKey = "purchase-memory.chunk-reload";

/**
 * A screen loaded on first visit. After a deploy, a page that is still open
 * (or the installed PWA) may ask for a chunk the new build no longer has; the
 * service worker has already replaced the cache. One reload fetches the new
 * build; a second failure is shown by `ScreenErrorBoundary` instead of looping.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function lazyPage<T extends ComponentType<any>>(load: () => Promise<{ default: T }>) {
  return lazy<T>(() => load().then(
    (module) => {
      safeSession((storage) => storage.removeItem(reloadKey));
      return module;
    },
    (error: unknown) => {
      const reloaded = safeSession((storage) => storage.getItem(reloadKey)) === "1";
      if (!reloaded) {
        safeSession((storage) => storage.setItem(reloadKey, "1"));
        window.location.reload();
        return new Promise<{ default: T }>(() => undefined);
      }
      throw error;
    }
  ));
}

function safeSession<R>(action: (storage: Storage) => R): R | undefined {
  try {
    return action(window.sessionStorage);
  } catch {
    return undefined;
  }
}

interface BoundaryState {
  failed: boolean;
}

/** Keeps the tab bar usable when one screen fails to load or render. */
export class ScreenErrorBoundary extends Component<{ children: ReactNode; resetKey: string }, BoundaryState> {
  state: BoundaryState = { failed: false };

  static getDerivedStateFromError(): BoundaryState {
    return { failed: true };
  }

  componentDidUpdate(previous: { resetKey: string }) {
    // Another screen gets a fresh chance.
    if (previous.resetKey !== this.props.resetKey && this.state.failed) this.setState({ failed: false });
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    console.error("Screen failed", error, info.componentStack);
  }

  render() {
    return this.state.failed ? <ScreenFailed /> : this.props.children;
  }
}

function ScreenFailed() {
  const { t } = useTranslation();
  return (
    <Page title={t("common.appNameEnglish")}>
      <Notice tone="danger" role="alert">
        <p>{t("ui.screenFailed")}</p>
        <IonButton fill="outline" size="small" onClick={() => window.location.reload()}>{t("ui.reload")}</IonButton>
      </Notice>
    </Page>
  );
}
