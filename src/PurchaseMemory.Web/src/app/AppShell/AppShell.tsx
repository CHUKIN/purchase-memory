import {
  AlertTriangle,
  BarChart3,
  Camera,
  CircleHelp,
  Home,
  LogOut,
  PackageSearch,
  ShoppingBasket
} from "lucide-react";
import type { PropsWithChildren, ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link, NavLink } from "react-router";
import type { RecognitionStatus } from "../../api/types";
import { useAuth } from "../../auth/AuthContext";
import { useAppLocale } from "../LocaleContext";
import styles from "./AppShell.module.css";

export interface AppShellProps extends PropsWithChildren {
  recognition: RecognitionStatus | null;
  recognitionFailed?: boolean;
  /** Items in the review queue; the badge is hidden while unknown or zero. */
  reviewCount?: number | null;
}

interface NavigationItem {
  to: string;
  label: string;
  icon: ReactNode;
  end?: boolean;
  mobile: boolean;
  badge?: number | null;
}

export function AppShell({ children, recognition, recognitionFailed = false, reviewCount = null }: AppShellProps) {
  const { t } = useTranslation();
  const items: NavigationItem[] = [
    { to: "/", label: t("common.home"), icon: <Home aria-hidden="true" />, end: true, mobile: true },
    { to: "/purchases", label: t("common.purchases"), icon: <ShoppingBasket aria-hidden="true" />, mobile: true },
    { to: "/products", label: t("common.products"), icon: <PackageSearch aria-hidden="true" />, mobile: true },
    { to: "/analytics", label: t("common.analytics"), icon: <BarChart3 aria-hidden="true" />, mobile: true },
    { to: "/needs-attention", label: t("common.review"), icon: <AlertTriangle aria-hidden="true" />, mobile: false, badge: reviewCount },
    { to: "/help", label: t("help.ui.navLabel"), icon: <CircleHelp aria-hidden="true" />, mobile: false }
  ];

  const recognitionTone: RecognitionTone = recognition === null && !recognitionFailed
    ? "checking"
    : recognition?.available === true && !recognitionFailed
      ? "available"
      : "unavailable";
  const statusLabel = t(`shell.${recognitionTone === "available" ? "recognitionAvailable" : recognitionTone === "checking" ? "recognitionChecking" : "recognitionUnavailable"}`);

  return (
    <div className={styles.shell}>
      <aside className={styles.sidebar}>
        <Brand />
        <nav className={styles.desktopNavigation} aria-label={t("shell.desktopNavigation")}>
          {items.map((item) => <NavigationLink key={item.to} item={item} />)}
        </nav>
        <div className={styles.sidebarFooter}>
          <RecognitionIndicator tone={recognitionTone} label={statusLabel} />
          <LocaleSwitcher />
          <SignOutButton />
        </div>
      </aside>

      <div className={styles.workspace}>
        <header className={styles.mobileHeader}>
          <Brand compact />
          <span className={styles.mobileHeaderActions}>
            {/* The bottom bar has room for four sections, so help lives up here on phones. */}
            <NavLink className={({ isActive }) => [styles.mobileHelpLink, isActive ? styles.active : ""].filter(Boolean).join(" ")} to="/help" aria-label={t("help.ui.navLabel")} title={t("help.ui.navLabel")}>
              <CircleHelp aria-hidden="true" size={20} />
            </NavLink>
            <LocaleSwitcher />
            <SignOutButton compact />
          </span>
        </header>

        <header className={styles.desktopHeader}>
          <RecognitionIndicator tone={recognitionTone} label={statusLabel} />
          <Link className={styles.captureLink} to="/purchases/new">
            <Camera aria-hidden="true" size={19} />
            {t("shell.addReceipt")}
          </Link>
        </header>

        <main className={styles.content}>{children}</main>
      </div>

      <nav className={styles.mobileNavigation} aria-label={t("shell.mobileNavigation")}>
        {items.filter((item) => item.mobile).map((item) => <NavigationLink key={item.to} item={item} mobile />)}
      </nav>
    </div>
  );
}

function Brand({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();
  return (
    <Link className={compact ? styles.compactBrand : styles.brand} to="/" aria-label={t("common.appNameEnglish")}>
      <span className={styles.brandMark} aria-hidden="true">PM</span>
      <span className={styles.brandCopy}>
        <strong>{t("common.appNameEnglish")}</strong>
        <small>{t("common.appTagline")}</small>
      </span>
    </Link>
  );
}

function NavigationLink({ item, mobile = false }: { item: NavigationItem; mobile?: boolean }) {
  return (
    <NavLink
      to={item.to}
      end={item.end}
      className={({ isActive }) => [mobile ? styles.mobileNavigationLink : styles.desktopNavigationLink, isActive ? styles.active : ""].filter(Boolean).join(" ")}
    >
      {item.icon}
      <span>{item.label}</span>
      {item.badge ? <AttentionBadge count={item.badge} /> : null}
    </NavLink>
  );
}

function LocaleSwitcher() {
  const { t } = useTranslation();
  const { locale, setLocale } = useAppLocale();
  return (
    <div className={styles.localeSwitcher} role="group" aria-label={t("shell.interfaceLanguage")}>
      <button type="button" aria-pressed={locale === "ru"} onClick={() => setLocale("ru")}>RU</button>
      <button type="button" aria-pressed={locale === "en"} onClick={() => setLocale("en")}>EN</button>
    </div>
  );
}

/** Rendered only inside the auth gate with a real session to end. */
function SignOutButton({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();
  const auth = useAuth();
  if (!auth?.signOut) return null;
  const signedInAs = t("auth.signedInAs", { email: auth.user.email ?? t("auth.unknownEmail") });
  return (
    <button type="button" className={styles.signOut} title={signedInAs} aria-label={compact ? `${t("auth.signOut")} (${signedInAs})` : undefined} onClick={() => void auth.signOut?.()}>
      <LogOut aria-hidden="true" size={18} />
      {!compact && <span>{t("auth.signOut")}</span>}
    </button>
  );
}

type RecognitionTone = "checking" | "available" | "unavailable";

function RecognitionIndicator({ tone, label }: { tone: RecognitionTone; label: string }) {
  return (
    <span className={styles.recognitionIndicator} title={label}>
      <span className={tone === "available" ? styles.onlineDot : tone === "checking" ? styles.checkingDot : styles.offlineDot} aria-hidden="true" />
      <span>{label}</span>
    </span>
  );
}

function AttentionBadge({ count }: { count: number }) {
  const { t } = useTranslation();
  return (
    <span className={styles.attentionBadge}>
      <span aria-hidden="true">{count > 99 ? "99+" : count}</span>
      <span className={styles.visuallyHidden}>{`, ${t("reviewQueue.badgeLabel", { count })}`}</span>
    </span>
  );
}
