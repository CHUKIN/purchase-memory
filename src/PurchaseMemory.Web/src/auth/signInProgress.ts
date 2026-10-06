/**
 * Small, non-secret breadcrumbs that survive a reload of the installed app.
 * iOS often restarts a Home Screen app while the user is in Mail copying the
 * code, or after a Google sheet is closed; without these the login screen
 * would silently start over.
 */
const emailKey = "pm.signIn.emailSent";
const providerKey = "pm.signIn.providerStarted";

/** Supabase email codes stay valid for an hour by default. */
const emailTtlMs = 60 * 60 * 1000;
const providerTtlMs = 10 * 60 * 1000;

interface Stamp<T> { value: T; at: number }

function read<T>(key: string, ttlMs: number): T | null {
  try {
    const raw = window.localStorage.getItem(key);
    if (!raw) return null;
    const stamp = JSON.parse(raw) as Stamp<T>;
    if (typeof stamp.at !== "number" || Date.now() - stamp.at > ttlMs) {
      window.localStorage.removeItem(key);
      return null;
    }
    return stamp.value;
  } catch {
    return null;
  }
}

function write<T>(key: string, value: T | null) {
  try {
    if (value === null) window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, JSON.stringify({ value, at: Date.now() } satisfies Stamp<T>));
  } catch {
    // Private mode or full storage: the breadcrumb is a convenience only.
  }
}

export const signInProgress = {
  emailSentTo: () => read<string>(emailKey, emailTtlMs),
  rememberEmailSent: (email: string | null) => write(emailKey, email),
  providerStarted: () => read<string>(providerKey, providerTtlMs),
  rememberProviderStarted: (provider: string | null) => write(providerKey, provider),
  clear() {
    write(emailKey, null);
    write(providerKey, null);
  }
};

export interface AuthCallbackInfo {
  /** The page was opened with an OAuth / email-link result in its address. */
  isCallback: boolean;
  /** The provider or Supabase reported an error in the address. */
  hasError: boolean;
}

/** Read before supabase-js runs: it strips a successful `?code=` itself. */
export function readAuthCallback(href: string): AuthCallbackInfo {
  const url = new URL(href);
  const hash = new URLSearchParams(url.hash.startsWith("#") ? url.hash.slice(1) : url.hash);
  const has = (name: string) => url.searchParams.has(name) || hash.has(name);
  const hasError = has("error") || has("error_description") || has("error_code");
  return { isCallback: hasError || has("code") || has("access_token"), hasError };
}

/** Removes a used or orphaned sign-in result from the address bar and history. */
export function clearAuthCallbackFromUrl() {
  const url = new URL(window.location.href);
  for (const name of ["code", "error", "error_description", "error_code", "state"]) url.searchParams.delete(name);
  const cleaned = `${url.pathname}${url.search}`;
  window.history.replaceState(window.history.state, "", cleaned);
}
