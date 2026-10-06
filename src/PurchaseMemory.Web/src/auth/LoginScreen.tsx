import { KeyRound, Mail } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Button, Field } from "../design-system/components";
import { AuthLayout } from "./AuthLayout";
import styles from "./LoginScreen.module.css";
import { signInProgress } from "./signInProgress";

export interface LoginScreenProps {
  /** From the server: "email" plus OAuth provider ids enabled in Supabase. */
  providers: string[];
  onSendEmailLink: (email: string) => Promise<void>;
  onVerifyCode: (email: string, code: string) => Promise<void>;
  onProviderSignIn: (provider: string) => Promise<void>;
  /** Shown above the form, e.g. "your session has ended". */
  notice?: string | null;
  /**
   * Running as the installed Home Screen app. There the link from the email
   * opens in the browser, not here, so the screen leads with the code.
   */
  standalone?: boolean;
  /** Start on the "email sent" step (stories). */
  initialSentTo?: string | null;
}

/**
 * Supabase projects send 6 to 10 digits (setting "Email OTP length"); ours
 * sends 8. Accept the whole range so a settings change needs no code change.
 */
export const minCodeLength = 6;
export const maxCodeLength = 10;

const providerNames: Record<string, string> = {
  google: "Google",
  apple: "Apple",
  github: "GitHub",
  azure: "Microsoft",
  facebook: "Facebook"
};

export function providerLabel(provider: string): string {
  return providerNames[provider] ?? provider.charAt(0).toUpperCase() + provider.slice(1);
}

/** Keeps digits only, so a pasted "123 456" or "Code: 123456" still works. */
export function normalizeCode(value: string): string {
  return value.replace(/\D/g, "").slice(0, maxCodeLength);
}

export function LoginScreen({
  providers,
  onSendEmailLink,
  onVerifyCode,
  onProviderSignIn,
  notice = null,
  standalone = false,
  initialSentTo = null
}: LoginScreenProps) {
  const { t } = useTranslation();
  // iOS may restart the installed app while the user is in Mail copying the
  // code; come back to the code step instead of the empty email form.
  const [sentTo, setSentTo] = useState<string | null>(() => initialSentTo ?? signInProgress.emailSentTo());
  const [email, setEmail] = useState(sentTo ?? "");
  const [code, setCode] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [emailInvalid, setEmailInvalid] = useState(false);
  const emailEnabled = providers.includes("email");
  const oauthProviders = providers.filter((provider) => provider !== "email");
  const codeReady = code.length >= minCodeLength;

  // A provider sheet that was closed without finishing leaves the page in
  // place: say so instead of waiting silently.
  useEffect(() => {
    function onReturn() {
      if (document.visibilityState !== "visible") return;
      const provider = signInProgress.providerStarted();
      if (!provider) return;
      signInProgress.rememberProviderStarted(null);
      setBusy(null);
      setError(t("auth.providerNotReturned", { provider: providerLabel(provider) }));
    }
    document.addEventListener("visibilitychange", onReturn);
    window.addEventListener("pageshow", onReturn);
    return () => {
      document.removeEventListener("visibilitychange", onReturn);
      window.removeEventListener("pageshow", onReturn);
    };
  }, [t]);

  async function run(key: string, action: () => Promise<void>, failure: string) {
    setBusy(key);
    setError(null);
    setStatus(null);
    try {
      await action();
      return true;
    } catch {
      setError(failure);
      return false;
    } finally {
      setBusy(null);
    }
  }

  async function sendLink(event: FormEvent) {
    event.preventDefault();
    const address = email.trim();
    if (!/^\S+@\S+\.\S+$/.test(address)) {
      setEmailInvalid(true);
      return;
    }
    setEmailInvalid(false);
    if (await run("email", () => onSendEmailLink(address), t("auth.sendFailed"))) {
      signInProgress.rememberEmailSent(address);
      setSentTo(address);
    }
  }

  async function resend() {
    if (!sentTo) return;
    if (await run("resend", () => onSendEmailLink(sentTo), t("auth.sendFailed"))) {
      signInProgress.rememberEmailSent(sentTo);
      setCode("");
      setStatus(t("auth.resent"));
    }
  }

  async function verify(event: FormEvent) {
    event.preventDefault();
    if (!sentTo || !codeReady) return;
    await run("code", () => onVerifyCode(sentTo, code), t("auth.codeInvalid"));
  }

  function startOver() {
    signInProgress.rememberEmailSent(null);
    setSentTo(null);
    setCode("");
    setError(null);
    setStatus(null);
  }

  return (
    <AuthLayout>
      <h1>{t("auth.loginTitle")}</h1>
      <p className={styles.intro}>{t("auth.loginIntro")}</p>
      {notice && <p className={styles.notice} role="status">{notice}</p>}

      {emailEnabled && !sentTo && (
        <form className={styles.form} onSubmit={sendLink} noValidate>
          {standalone && <p className={styles.hint}>{t("auth.standaloneIntro")}</p>}
          <Field
            label={t("auth.emailLabel")}
            type="email"
            autoComplete="email"
            inputMode="email"
            placeholder={t("auth.emailPlaceholder")}
            value={email}
            onChange={(event) => setEmail(event.currentTarget.value)}
            error={emailInvalid ? t("auth.emailInvalid") : undefined}
          />
          <Button type="submit" fullWidth loading={busy === "email"} disabled={busy !== null} leftSection={<Mail aria-hidden="true" size={18} />}>
            {standalone ? t("auth.sendCode") : t("auth.sendLink")}
          </Button>
        </form>
      )}

      {emailEnabled && sentTo && (
        <form className={styles.form} onSubmit={verify}>
          <p className={styles.notice} role="status">
            {t(standalone ? "auth.codeSentStandalone" : "auth.linkSent", { email: sentTo })}
          </p>
          <Field
            label={t("auth.codeLabel")}
            description={t("auth.codeHint", { min: minCodeLength, max: maxCodeLength })}
            autoComplete="one-time-code"
            inputMode="numeric"
            pattern="[0-9]*"
            maxLength={maxCodeLength}
            autoFocus={standalone}
            value={code}
            onChange={(event) => setCode(normalizeCode(event.currentTarget.value))}
          />
          <Button
            type="submit"
            fullWidth
            loading={busy === "code"}
            disabled={busy !== null || !codeReady}
            leftSection={<KeyRound aria-hidden="true" size={18} />}
          >
            {t("auth.verifyCode")}
          </Button>
          <div className={styles.secondaryActions}>
            <Button intent="quiet" loading={busy === "resend"} disabled={busy !== null} onClick={() => void resend()}>
              {t("auth.resend")}
            </Button>
            <Button intent="quiet" disabled={busy !== null} onClick={startOver}>
              {t("auth.useAnotherEmail")}
            </Button>
          </div>
          {status && <p className={styles.hint} role="status">{status}</p>}
        </form>
      )}

      {/* Right under the form the user just used, not below the providers. */}
      {error && <p className={styles.error} role="alert">{error}</p>}
      {emailEnabled && oauthProviders.length > 0 && <div className={styles.divider}>{t("auth.orDivider")}</div>}

      {oauthProviders.length > 0 && (
        <div className={styles.providers}>
          {oauthProviders.map((provider) => (
            <Button
              key={provider}
              intent="secondary"
              fullWidth
              loading={busy === provider}
              disabled={busy !== null}
              onClick={() => void run(provider, () => onProviderSignIn(provider), t("auth.providerFailed", { provider: providerLabel(provider) }))}
            >
              {t("auth.continueWith", { provider: providerLabel(provider) })}
            </Button>
          ))}
          {standalone && emailEnabled && <p className={styles.hint}>{t("auth.standaloneProviderHint")}</p>}
        </div>
      )}

    </AuthLayout>
  );
}
