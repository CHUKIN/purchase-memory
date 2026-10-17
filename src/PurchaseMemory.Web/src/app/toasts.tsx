import { IonToast } from "@ionic/react";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useReturnState } from "./backNavigation";

export interface ToastMessage {
  /** Same id twice is shown once (a receipt finishing is reported once). */
  id: string;
  message: string;
  header?: string;
  tone?: "success" | "danger" | "neutral";
  /** A button that opens a screen, e.g. «Открыть чек». */
  action?: { label: string; to: string };
}

interface ToastContextValue {
  show: (toast: ToastMessage) => void;
  /** The router registers its navigate here so toast buttons stay inside the SPA. */
  setNavigator: (navigate: ((to: string) => void) | null) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

const durationMs = 6_000;

/**
 * Short confirmations and background news («Чек готов к проверке») as iOS
 * toasts at the top. One toast at a time; the next waits until the current
 * one is dismissed or times out.
 */
export function ToastProvider({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const [queue, setQueue] = useState<ToastMessage[]>([]);
  const seen = useRef(new Set<string>());
  const navigator = useRef<((to: string) => void) | null>(null);

  const show = useCallback((toast: ToastMessage) => {
    if (seen.current.has(toast.id)) return;
    seen.current.add(toast.id);
    setQueue((current) => [...current, toast]);
  }, []);
  const setNavigator = useCallback((navigate: ((to: string) => void) | null) => { navigator.current = navigate; }, []);
  const value = useMemo(() => ({ show, setNavigator }), [show, setNavigator]);
  const current = queue[0] ?? null;
  const next = () => setQueue((items) => items.slice(1));

  return (
    <ToastContext.Provider value={value}>
      {children}
      {current && (
        <IonToast
          key={current.id}
          isOpen
          position="top"
          duration={durationMs}
          header={current.header}
          message={current.message}
          color={current.tone === "danger" ? "danger" : current.tone === "success" ? "success" : undefined}
          swipeGesture="vertical"
          onDidDismiss={next}
          buttons={[
            ...(current.action
              ? [{
                text: current.action.label,
                handler: () => {
                  const to = current.action!.to;
                  if (navigator.current) navigator.current(to);
                  else window.location.assign(to);
                }
              }]
              : []),
            { text: t("ui.close"), role: "cancel" }
          ]}
        />
      )}
    </ToastContext.Provider>
  );
}

export function useToast(): (toast: ToastMessage) => void {
  return useContext(ToastContext)?.show ?? noop;
}

/** Rendered once inside the router, so «Открыть чек» in a toast is an in-app navigation. */
export function ToastNavigationBridge() {
  const context = useContext(ToastContext);
  const navigate = useNavigate();
  // The screen the toast was tapped on is where «Назад» from the receipt leads.
  const returnState = useReturnState();
  const returnStateRef = useRef(returnState);
  returnStateRef.current = returnState;
  useEffect(() => {
    context?.setNavigator((to) => navigate(to, { state: returnStateRef.current }));
    return () => context?.setNavigator(null);
  }, [context, navigate]);
  return null;
}

function noop() {}
