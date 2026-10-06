/**
 * The seam between sign-in and the API client. The auth gate installs a
 * token source (supabase-js, which refreshes tokens itself) and a listener for
 * rejected sessions; the API client only asks for "the current token".
 */
export type AccessTokenSource = () => Promise<string | null>;
/**
 * `token` is the access token the rejected request carried (null without
 * one). The gate uses it to tell a stale request that raced a token refresh
 * from a fresh token being rejected again.
 */
export type SessionRejectedListener = (status: 401 | 403, token: string | null) => void;

let tokenSource: AccessTokenSource = async () => null;
let rejectedListener: SessionRejectedListener = () => undefined;

export function setAccessTokenSource(source: AccessTokenSource | null) {
  tokenSource = source ?? (async () => null);
}

export function setSessionRejectedListener(listener: SessionRejectedListener | null) {
  rejectedListener = listener ?? (() => undefined);
}

export function currentAccessToken(): Promise<string | null> {
  return tokenSource();
}

export function reportSessionRejected(status: 401 | 403, token: string | null = null) {
  rejectedListener(status, token);
}

/** Adds `Authorization: Bearer …` when a session exists; keeps other headers. */
export async function withAuthorization(init?: RequestInit): Promise<RequestInit | undefined> {
  return (await authorize(init)).init;
}

/** Like `withAuthorization`, and also tells which token was attached. */
export async function authorize(init?: RequestInit): Promise<{ init: RequestInit | undefined; token: string | null }> {
  const token = await currentAccessToken();
  if (!token) return { init, token: null };
  const headers = new Headers(init?.headers);
  headers.set("Authorization", `Bearer ${token}`);
  return { init: { ...init, headers }, token };
}
