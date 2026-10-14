import { IonBadge, IonLabel, IonTabBar, IonTabButton } from "@ionic/react";
import { BarChart3, Home, PackageSearch, ShoppingBasket } from "lucide-react";
import type { PropsWithChildren, ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate } from "react-router";

export interface AppShellProps extends PropsWithChildren {
  /** Items in the review queue; shown as a badge on «Главная», where the queue is opened. */
  reviewCount?: number | null;
}

type Tab = "home" | "purchases" | "products" | "analytics";

interface TabItem {
  tab: Tab;
  to: string;
  label: string;
  icon: ReactNode;
}

/** Which tab a screen belongs to: details stay under their list, review and help under Home. */
export function tabOf(pathname: string): Tab {
  if (pathname.startsWith("/purchases/new")) return "home";
  if (pathname.startsWith("/purchases")) return "purchases";
  if (pathname.startsWith("/products")) return "products";
  if (pathname.startsWith("/analytics")) return "analytics";
  return "home";
}

/** Badge text: the exact count up to 99, then «99+». */
export function badgeText(count: number): string {
  return count > 99 ? "99+" : String(count);
}

/**
 * The routed screen plus an iOS tab bar with the four sections. There is no
 * IonRouterOutlet (Ionic's router bindings stop at React Router 6), so the
 * tab bar is standalone and each screen is a plain `.ion-page`.
 */
export function AppShell({ children, reviewCount = null }: AppShellProps) {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const selected = tabOf(location.pathname);
  const tabs: TabItem[] = [
    { tab: "home", to: "/", label: t("common.home"), icon: <Home aria-hidden="true" /> },
    { tab: "purchases", to: "/purchases", label: t("common.purchases"), icon: <ShoppingBasket aria-hidden="true" /> },
    { tab: "products", to: "/products", label: t("common.products"), icon: <PackageSearch aria-hidden="true" /> },
    { tab: "analytics", to: "/analytics", label: t("common.analytics"), icon: <BarChart3 aria-hidden="true" /> }
  ];
  const badge = reviewCount && reviewCount > 0 ? reviewCount : null;

  return (
    <div className="pm-app-layout">
      {children}
      <IonTabBar selectedTab={selected} aria-label={t("shell.navigation")}>
        {tabs.map((item) => (
          <IonTabButton
            key={item.tab}
            tab={item.tab}
            selected={item.tab === selected}
            href={item.to}
            aria-current={item.tab === selected ? "page" : undefined}
            // Ionic cancels the link's own navigation and reports the tap here.
            onClick={() => {
              // Already on this tab's root: do nothing, so scroll, filters and loaded data stay.
              // On a nested screen (a detail, review, help) a tap goes back to the root, as on iOS.
              if (location.pathname !== item.to) navigate(item.to);
            }}
          >
            {/* The badge is anchored to the icon, not to the tab button, so it is never cut off. */}
            <span className="pm-tab-icon">
              {item.icon}
              {item.tab === "home" && badge !== null && (
                <IonBadge className="pm-tab-badge" color="danger" aria-hidden="true">{badgeText(badge)}</IonBadge>
              )}
            </span>
            <IonLabel>{item.label}</IonLabel>
            {item.tab === "home" && badge !== null && (
              <span className="visually-hidden">{`, ${t("reviewQueue.badgeLabel", { count: badge })}`}</span>
            )}
          </IonTabButton>
        ))}
      </IonTabBar>
    </div>
  );
}
