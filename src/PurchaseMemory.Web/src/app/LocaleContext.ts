import { createContext, useContext } from "react";
import type { AppLocale } from "../i18n/i18n";

/**
 * The app is Russian only for now (owner decision, 2026-10-13). While false, the
 * RU/EN switch is hidden and the stored preference is ignored. English texts stay
 * in i18n.ts/helpText.ts for a later return: set to true to bring the switch back.
 */
export const LANGUAGE_SWITCH_ENABLED = false;

export const localeStorageKey = "purchase-memory.locale";

export interface LocaleContextValue {
  locale: AppLocale;
  setLocale: (locale: AppLocale) => void;
}

export const LocaleContext = createContext<LocaleContextValue | null>(null);

export function useAppLocale(): LocaleContextValue {
  const context = useContext(LocaleContext);
  if (!context) throw new Error("useAppLocale must be used within AppProviders");
  return context;
}
