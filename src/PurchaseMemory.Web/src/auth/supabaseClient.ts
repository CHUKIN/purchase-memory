import { createClient, type SupabaseClient } from "@supabase/supabase-js";

let cached: { key: string; client: SupabaseClient } | null = null;

/**
 * Where supabase-js keeps the session in localStorage. This is the library's
 * own default (`sb-<project-ref>-auth-token`), spelled out so the gate can tell
 * "no session" from "session kept, but it could not be refreshed right now".
 * Changing it would sign everybody out.
 */
export function sessionStorageKey(url: string): string {
  return `sb-${new URL(url).hostname.split(".")[0]}-auth-token`;
}

/** A refresh token is still stored on this device (it may be unusable offline). */
export function hasStoredSession(url: string): boolean {
  try {
    const raw = window.localStorage.getItem(sessionStorageKey(url));
    if (!raw) return false;
    const stored = JSON.parse(raw) as { refresh_token?: unknown };
    return typeof stored.refresh_token === "string" && stored.refresh_token.length > 0;
  } catch {
    return false;
  }
}

/**
 * One client per project for the page lifetime. Values come from the server's
 * public `/api/auth/config`, so the bundle carries no project settings.
 * PKCE keeps tokens out of the redirect URL; supabase-js exchanges the code
 * on return, refreshes the access token before it expires and resumes that
 * when the app comes back to the foreground (visibilitychange).
 */
export function supabaseFor(url: string, publishableKey: string): SupabaseClient {
  const key = `${url}|${publishableKey}`;
  if (cached?.key !== key) {
    cached = {
      key,
      client: createClient(url, publishableKey, {
        auth: {
          flowType: "pkce",
          autoRefreshToken: true,
          persistSession: true,
          detectSessionInUrl: true,
          storageKey: sessionStorageKey(url)
        }
      })
    };
  }
  return cached.client;
}
