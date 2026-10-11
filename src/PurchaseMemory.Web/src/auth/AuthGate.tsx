import { isAuthRetryableFetchError, type SupabaseClient } from "@supabase/supabase-js";
import { useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, type PropsWithChildren } from "react";
import { useTranslation } from "react-i18next";
import { ApiError, getAuthConfig, getCurrentUser, type AuthConfig, type CurrentUser } from "../api/client";
import { AccessStatusScreen } from "./AccessStatusScreen";
import { AuthContext, type AuthContextValue } from "./AuthContext";
import { setAccessTokenSource, setSessionRejectedListener } from "./authSession";
import { isStandaloneDisplay } from "./displayMode";
import { LoginScreen, providerLabel } from "./LoginScreen";
import { clearAuthCallbackFromUrl, readAuthCallback, signInProgress } from "./signInProgress";
import { hasStoredSession, supabaseFor } from "./supabaseClient";
import { isTransientError, retryTransient } from "./transientRetry";

type Phase =
  | { kind: "checking" }
  | { kind: "configError" }
  /** `sessionKept`: a stored session exists and was not dropped. */
  | { kind: "connectionError"; sessionKept: boolean }
  | { kind: "unconfigured" }
  | { kind: "signedOut"; notice: string | null }
  | { kind: "user"; user: CurrentUser };

/** What a forced token refresh says about the stored session. */
type RefreshOutcome = "refreshed" | "unreachable" | "rejected";

/** After a successful recovery from a 401, another 401 within this time ends the session. */
const recoveryWindowMs = 30_000;

/**
 * Decides what the visitor sees: login, "waiting for approval", "blocked" or
 * the app. The server stays the authority: this screen only reflects what
 * `/api/me` says about the token supabase-js holds.
 *
 * A session is dropped only when Supabase or the API actually rejects it.
 * A sleeping server, a slow first request or a flaky phone network leave the
 * stored session in place and show "no connection, try again" instead.
 */
export function AuthGate({ children }: PropsWithChildren) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [phase, setPhase] = useState<Phase>({ kind: "checking" });
  const [config, setConfig] = useState<AuthConfig | null>(null);
  const [busy, setBusy] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const clientRef = useRef<SupabaseClient | null>(null);
  /** While the gate itself asks `/api/me`, a 401 there is handled by the gate. */
  const verifyingRef = useRef(0);
  /**
   * Recovery from a 401 during use: one refresh + reload at a time, and at
   * most one per `recoveryWindowMs`. If the token it produced is rejected
   * again inside that window, the session ends instead of looping.
   * `staleRetried`: requests that left with an older token are re-asked once
   * per recovery; further stale 401s in the window are ignored, so the app
   * does not keep refetching everything for up to `recoveryWindowMs`.
   */
  const recoveryRef = useRef<{ inFlight: boolean; refreshedAt: number; freshToken: string | null; staleRetried: boolean }>({
    inFlight: false,
    refreshedAt: Number.NEGATIVE_INFINITY,
    freshToken: null,
    staleRetried: false
  });
  const tRef = useRef(t);
  tRef.current = t;
  // Captured once per page load: supabase-js removes a successful `?code=` itself.
  const callbackRef = useRef(typeof window === "undefined" ? null : readAuthCallback(window.location.href));
  const standalone = useMemo(() => isStandaloneDisplay(), []);

  const refreshStoredSession = useCallback(async (): Promise<RefreshOutcome> => {
    const client = clientRef.current;
    if (!client) return "rejected";
    try {
      const { data, error } = await client.auth.refreshSession();
      if (!error && data.session) return "refreshed";
      // A network failure or Supabase rate limiting (429) says nothing about
      // the session itself: keep it and try again later.
      if (error && (isAuthRetryableFetchError(error) || (error as { status?: number }).status === 429)) return "unreachable";
    } catch (error) {
      if (isTransientError(error)) return "unreachable";
    }
    return typeof navigator !== "undefined" && navigator.onLine === false ? "unreachable" : "rejected";
  }, []);

  const endSession = useCallback(async (notice: string | null) => {
    await clientRef.current?.auth.signOut({ scope: "local" }).catch(() => undefined);
    queryClient.clear();
    setPhase({ kind: "signedOut", notice });
  }, [queryClient]);

  const loadUser = useCallback(async (isCancelled: () => boolean = () => false) => {
    verifyingRef.current += 1;
    try {
      let user: CurrentUser;
      try {
        user = await retryTransient(() => getCurrentUser(), isCancelled);
      } catch (error) {
        if (!(error instanceof ApiError && error.status === 401)) throw error;
        // The access token may be stale because a refresh failed on a bad
        // network. Refresh once before believing the server.
        const outcome = await refreshStoredSession();
        if (outcome === "unreachable") {
          if (!isCancelled()) setPhase({ kind: "connectionError", sessionKept: true });
          return;
        }
        if (outcome === "rejected") {
          if (!isCancelled()) await endSession(null);
          return;
        }
        user = await retryTransient(() => getCurrentUser(), isCancelled);
      }
      if (!isCancelled()) {
        signInProgress.clear();
        setPhase({ kind: "user", user });
      }
    } catch (error) {
      if (isCancelled()) return;
      if (error instanceof ApiError && error.status === 401) await endSession(tRef.current("auth.sessionExpired"));
      else if (isTransientError(error)) setPhase({ kind: "connectionError", sessionKept: true });
      else setPhase({ kind: "configError" });
    } finally {
      verifyingRef.current -= 1;
    }
  }, [endSession, refreshStoredSession]);

  /** The login screen after a start-up without a session, with a reason when we know one. */
  const showSignedOut = useCallback((exchangeFailed = false) => {
    const callback = callbackRef.current;
    callbackRef.current = null;
    let notice: string | null = null;
    if (callback?.isCallback) {
      // A sign-in result arrived here but produced no session. Either the
      // provider/Supabase reported an error (or the code exchange failed), or
      // the email link / Google finished in another browser than the one that
      // started it, so this window lacks the PKCE verifier and supabase-js
      // ignored the code.
      notice = tRef.current(callback.hasError || exchangeFailed ? "auth.callbackError" : "auth.callbackElsewhere");
      clearAuthCallbackFromUrl();
    } else {
      const provider = signInProgress.providerStarted();
      if (provider) notice = tRef.current("auth.providerNotReturned", { provider: providerLabel(provider) });
    }
    signInProgress.rememberProviderStarted(null);
    setPhase({ kind: "signedOut", notice });
  }, []);

  useEffect(() => {
    let cancelled = false;
    const isCancelled = () => cancelled;
    let unsubscribe: (() => void) | null = null;
    setPhase({ kind: "checking" });

    void (async () => {
      let loaded: AuthConfig;
      try {
        loaded = await retryTransient(() => getAuthConfig(), isCancelled);
      } catch (error) {
        // Without the settings we cannot tell whether a session is stored: say nothing about it.
        if (!cancelled) setPhase(isTransientError(error) ? { kind: "connectionError", sessionKept: false } : { kind: "configError" });
        return;
      }
      if (cancelled) return;
      setConfig(loaded);

      if (loaded.developmentBypass) {
        try {
          const user = await getCurrentUser();
          if (!cancelled) setPhase({ kind: "user", user });
          return;
        } catch {
          // Not a loopback request: fall through to real sign-in.
        }
      }

      if (loaded.mode !== "supabase" || !loaded.supabaseUrl || !loaded.supabasePublishableKey) {
        if (!cancelled) setPhase({ kind: "unconfigured" });
        return;
      }

      const projectUrl = loaded.supabaseUrl;
      const client = supabaseFor(projectUrl, loaded.supabasePublishableKey);
      clientRef.current = client;
      setAccessTokenSource(async () => (await client.auth.getSession()).data.session?.access_token ?? null);

      const { data } = client.auth.onAuthStateChange((event, session) => {
        // Defer: supabase-js advises against awaiting its own calls in here.
        setTimeout(() => {
          if (cancelled) return;
          if (event === "INITIAL_SESSION" && !session) {
            if (!hasStoredSession(projectUrl)) {
              // initialize() returns the stored start-up result: an error
              // means supabase-js tried to exchange a code from the address.
              void client.auth.initialize()
                .then(({ error }) => { if (!cancelled) showSignedOut(Boolean(error)); })
                .catch(() => { if (!cancelled) showSignedOut(); });
              return;
            }
            // supabase-js keeps the session when a refresh fails for network
            // reasons but reports "no session" at start-up. Ask again rather
            // than showing the login screen to someone who is signed in.
            void refreshStoredSession().then((outcome) => {
              if (cancelled) return;
              if (outcome === "refreshed") void loadUser(isCancelled);
              else if (outcome === "unreachable") setPhase({ kind: "connectionError", sessionKept: true });
              else showSignedOut(false);
            });
          } else if (event === "SIGNED_OUT" || !session) {
            queryClient.clear();
            setPhase({ kind: "signedOut", notice: null });
          } else if (event === "INITIAL_SESSION" || event === "SIGNED_IN" || event === "USER_UPDATED") {
            if (callbackRef.current?.isCallback) clearAuthCallbackFromUrl();
            callbackRef.current = null;
            void loadUser(isCancelled);
          }
        }, 0);
      });
      unsubscribe = () => data.subscription.unsubscribe();
    })();

    return () => {
      cancelled = true;
      unsubscribe?.();
    };
  }, [attempt, loadUser, queryClient, refreshStoredSession, showSignedOut]);

  useEffect(() => {
    setSessionRejectedListener((status, token) => {
      if (status === 401) {
        // The gate's own /api/me check handles its 401 itself.
        if (verifyingRef.current > 0) return;
        const recovery = recoveryRef.current;
        // Parallel 401s share the refresh that is already running.
        if (recovery.inFlight) return;
        if (Date.now() - recovery.refreshedAt < recoveryWindowMs) {
          if (token !== null && token !== recovery.freshToken) {
            // A request that left before the refresh: ask again with the new token,
            // once — one refetch already re-sends every active query.
            if (!recovery.staleRetried) {
              recovery.staleRetried = true;
              void queryClient.invalidateQueries();
            }
          } else {
            // The fresh token was rejected too: refreshing again would loop.
            void endSession(tRef.current("auth.sessionExpired"));
          }
          return;
        }
        recovery.inFlight = true;
        void (async () => {
          try {
            const outcome = await refreshStoredSession();
            if (outcome === "refreshed") {
              recovery.refreshedAt = Date.now();
              recovery.staleRetried = false;
              recovery.freshToken = (await clientRef.current?.auth.getSession())?.data.session?.access_token ?? null;
              await loadUser();
              void queryClient.invalidateQueries();
            } else if (outcome === "rejected") {
              await endSession(tRef.current("auth.sessionExpired"));
            }
            // Unreachable: keep the app open; the failed request shows its own error.
          } finally {
            recovery.inFlight = false;
          }
        })();
      } else {
        // Approval was withdrawn while the app was open: ask the server again.
        void loadUser();
      }
    });
    return () => setSessionRejectedListener(null);
  }, [endSession, loadUser, queryClient, refreshStoredSession]);

  useEffect(() => () => setAccessTokenSource(null), []);

  const signOut = useCallback(async () => {
    await clientRef.current?.auth.signOut().catch(() => undefined);
    signInProgress.clear();
    queryClient.clear();
    setPhase({ kind: "signedOut", notice: null });
  }, [queryClient]);

  const checkAgain = useCallback(async () => {
    setBusy(true);
    try {
      // The approval arrives as a claim in a fresh token.
      await clientRef.current?.auth.refreshSession();
      await loadUser();
    } finally {
      setBusy(false);
    }
  }, [loadUser]);

  const redirectTo = typeof window === "undefined" ? undefined : `${window.location.origin}/`;
  const authValue = useMemo<AuthContextValue | null>(
    () => phase.kind === "user" ? { user: phase.user, signOut: clientRef.current ? signOut : null } : null,
    [phase, signOut]
  );
  const retry = () => setAttempt((value) => value + 1);

  switch (phase.kind) {
    case "checking":
      return <AccessStatusScreen kind="checking" />;
    case "configError":
      return <AccessStatusScreen kind="configError" onRetry={retry} />;
    case "connectionError":
      return <AccessStatusScreen kind="connectionError" sessionKept={phase.sessionKept} onRetry={retry} />;
    case "unconfigured":
      return <AccessStatusScreen kind="unconfigured" />;
    case "signedOut":
      return (
        <LoginScreen
          providers={config?.providers ?? ["email"]}
          notice={phase.notice}
          standalone={standalone}
          onSendEmailLink={async (email) => {
            const { error } = await clientRef.current!.auth.signInWithOtp({ email, options: { emailRedirectTo: redirectTo } });
            if (error) throw error;
          }}
          onVerifyCode={async (email, token) => {
            const { error } = await clientRef.current!.auth.verifyOtp({ email, token, type: "email" });
            if (error) throw error;
          }}
          onProviderSignIn={async (provider) => {
            // Same-window navigation (supabase-js default). A popup or new tab
            // would open outside the installed iPhone app, whose storage holds
            // the PKCE verifier needed to finish the sign-in.
            signInProgress.rememberProviderStarted(provider);
            const { error } = await clientRef.current!.auth.signInWithOAuth({
              provider: provider as Parameters<SupabaseClient["auth"]["signInWithOAuth"]>[0]["provider"],
              options: { redirectTo }
            });
            if (error) {
              signInProgress.rememberProviderStarted(null);
              throw error;
            }
          }}
        />
      );
    case "user":
      if (phase.user.status === "pending" || phase.user.status === "blocked") {
        return (
          <AccessStatusScreen
            kind={phase.user.status}
            email={phase.user.email}
            busy={busy}
            onRetry={phase.user.status === "pending" ? () => void checkAgain() : undefined}
            onSignOut={clientRef.current ? () => void signOut() : undefined}
          />
        );
      }
      return <AuthContext.Provider value={authValue}>{children}</AuthContext.Provider>;
  }
}

