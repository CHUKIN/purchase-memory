import { MantineProvider } from "@mantine/core";
import { Notifications } from "@mantine/notifications";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useState, type PropsWithChildren } from "react";
import { I18nextProvider } from "react-i18next";
import { purchaseMemoryTheme } from "../design-system/theme";
import { createPurchaseMemoryI18n, isAppLocale, type AppLocale } from "../i18n/i18n";
import { LocaleContext, localeStorageKey } from "./LocaleContext";
import "@mantine/core/styles.css";
import "@mantine/notifications/styles.css";
import "@mantine/charts/styles.css";
import "../styles.css";

interface AppProvidersProps extends PropsWithChildren {
  locale?: AppLocale;
}

function storedLocale(): AppLocale {
  if (typeof window === "undefined") return "ru";
  const locale = window.localStorage.getItem(localeStorageKey);
  return isAppLocale(locale) ? locale : "ru";
}

export function AppProviders({ children, locale: controlledLocale }: AppProvidersProps) {
  const [selectedLocale, setSelectedLocale] = useState<AppLocale>(() => controlledLocale ?? storedLocale());
  const locale = controlledLocale ?? selectedLocale;
  const i18n = useMemo(() => createPurchaseMemoryI18n(locale), []);
  const [queryClient] = useState(() => new QueryClient({
    defaultOptions: {
      queries: { retry: 1, refetchOnWindowFocus: true, staleTime: 1_000 },
      mutations: { retry: false }
    }
  }));

  useEffect(() => {
    if (i18n.language !== locale) void i18n.changeLanguage(locale);
    if (typeof document !== "undefined") document.documentElement.lang = locale;
  }, [i18n, locale]);

  const setLocale = useCallback((nextLocale: AppLocale) => {
    setSelectedLocale(nextLocale);
    if (typeof window !== "undefined") window.localStorage.setItem(localeStorageKey, nextLocale);
    if (typeof document !== "undefined") document.documentElement.lang = nextLocale;
    void i18n.changeLanguage(nextLocale);
  }, [i18n]);

  const localeContext = useMemo(() => ({ locale, setLocale }), [locale, setLocale]);

  return (
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider theme={purchaseMemoryTheme} forceColorScheme="light">
          <Notifications position="top-right" limit={3} />
          <LocaleContext.Provider value={localeContext}>{children}</LocaleContext.Provider>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}
