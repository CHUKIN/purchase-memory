import { createContext, useContext } from "react";
import type { AppLocale } from "../i18n/i18n";

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
