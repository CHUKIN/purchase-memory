import { AuthApiError, AuthRetryableFetchError } from "@supabase/supabase-js";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getAuthConfig, getCurrentUser } from "../api/client";
import { AppProviders } from "../app/AppProviders";
import { AuthGate } from "./AuthGate";
import { currentAccessToken, reportSessionRejected } from "./authSession";
import { signInProgress } from "./signInProgress";
import { hasStoredSession, supabaseFor } from "./supabaseClient";
import { transientRetry } from "./transientRetry";

vi.mock("../api/client", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../api/client")>();
  return { ...actual, getAuthConfig: vi.fn(), getCurrentUser: vi.fn() };
});
vi.mock("./supabaseClient", () => ({ supabaseFor: vi.fn(), hasStoredSession: vi.fn(() => false) }));

type Listener = (event: string, session: unknown) => void;

function fakeSupabase(session: { access_token: string } | null) {
  const auth = {
    getSession: vi.fn().mockResolvedValue({ data: { session } }),
    onAuthStateChange: vi.fn((listener: Listener) => {
      setTimeout(() => listener("INITIAL_SESSION", session), 0);
      return { data: { subscription: { unsubscribe: vi.fn() } } };
    }),
    signOut: vi.fn().mockResolvedValue({ error: null }),
    refreshSession: vi.fn().mockResolvedValue({ data: { session }, error: null }),
    signInWithOtp: vi.fn().mockResolvedValue({ error: null }),
    verifyOtp: vi.fn().mockResolvedValue({ error: null }),
    signInWithOAuth: vi.fn().mockResolvedValue({ error: null }),
    initialize: vi.fn().mockResolvedValue({ error: null })
  };
  vi.mocked(supabaseFor).mockReturnValue({ auth } as never);
  return auth;
}

const supabaseConfig = {
  mode: "supabase" as const,
  supabaseUrl: "https://demo.supabase.co",
  supabasePublishableKey: "sb_publishable_demo",
  providers: ["email", "google"],
  developmentBypass: false
};

function renderGate() {
  render(<AppProviders locale="ru"><AuthGate><p>Приложение открыто</p></AuthGate></AppProviders>);
}

const savedDelays = transientRetry.delaysMs;

describe("AuthGate", () => {
  beforeEach(() => {
    vi.mocked(getAuthConfig).mockReset();
    vi.mocked(getCurrentUser).mockReset();
    vi.mocked(supabaseFor).mockReset();
    vi.mocked(hasStoredSession).mockReset().mockReturnValue(false);
    transientRetry.delaysMs = [0, 0, 0];
  });

  afterEach(() => {
    transientRetry.delaysMs = savedDelays;
    window.history.replaceState(null, "", "/");
  });

  it("shows the login screen without a session", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    fakeSupabase(null);
    renderGate();
    expect(await screen.findByRole("heading", { name: "Вход в историю покупок" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Продолжить через Google" })).toBeInTheDocument();
    expect(screen.queryByText("Приложение открыто")).not.toBeInTheDocument();
  });

  it("opens the app for an approved user and exposes the session token to the API client", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    fakeSupabase({ access_token: "jwt-anna" });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();
    expect(await currentAccessToken()).toBe("jwt-anna");
  });

  it("shows the waiting screen for a pending user", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "anna@example.com", status: "pending" });
    renderGate();
    expect(await screen.findByRole("heading", { name: "Заявка отправлена" })).toBeInTheDocument();
    expect(screen.queryByText("Приложение открыто")).not.toBeInTheDocument();
  });

  it("shows the blocked screen for a blocked user", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "boris@example.com", status: "blocked" });
    renderGate();
    expect(await screen.findByRole("heading", { name: "Доступ заблокирован" })).toBeInTheDocument();
  });

  it("returns to login when the server rejects the token and Supabase refuses to refresh it", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "stale" });
    auth.refreshSession.mockResolvedValue({ data: { session: null }, error: new AuthApiError("Invalid Refresh Token", 400, "refresh_token_not_found") });
    vi.mocked(getCurrentUser).mockRejectedValue(new ApiError("unauthorized", 401));
    renderGate();
    expect(await screen.findByRole("heading", { name: "Вход в историю покупок" })).toBeInTheDocument();
    await waitFor(() => expect(auth.signOut).toHaveBeenCalledWith({ scope: "local" }));
  });

  it("refreshes a stale token once instead of signing out on the first 401", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "stale" });
    vi.mocked(getCurrentUser)
      .mockRejectedValueOnce(new ApiError("unauthorized", 401))
      .mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();
    expect(auth.refreshSession).toHaveBeenCalledOnce();
    expect(auth.signOut).not.toHaveBeenCalled();
  });

  it("waits for a waking server instead of dropping the session", async () => {
    vi.mocked(getAuthConfig)
      .mockRejectedValueOnce(new TypeError("Load failed"))
      .mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser)
      .mockRejectedValueOnce(new ApiError("Bad gateway", 502))
      .mockRejectedValueOnce(new ApiError("Service unavailable", 503))
      .mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();
    expect(getCurrentUser).toHaveBeenCalledTimes(3);
    expect(auth.signOut).not.toHaveBeenCalled();
  });

  it("shows 'no connection' and keeps the session when the server stays unreachable", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser).mockRejectedValue(new TypeError("Load failed"));
    renderGate();
    expect(await screen.findByRole("heading", { name: "Нет связи с сервером" })).toBeInTheDocument();
    expect(screen.getByText(/вход сохранён/)).toBeInTheDocument();
    expect(auth.signOut).not.toHaveBeenCalled();

    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();
  });

  it("does not show the login screen when a stored session could not be refreshed offline", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    vi.mocked(hasStoredSession).mockReturnValue(true);
    const auth = fakeSupabase(null);
    auth.refreshSession.mockResolvedValue({ data: { session: null }, error: new AuthRetryableFetchError("Load failed", 0) });
    renderGate();
    expect(await screen.findByRole("heading", { name: "Нет связи с сервером" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Вход в историю покупок" })).not.toBeInTheDocument();
    expect(auth.signOut).not.toHaveBeenCalled();
  });

  it("keeps the app open when a 401 during use meets an unreachable refresh", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();

    auth.refreshSession.mockResolvedValue({ data: { session: null }, error: new AuthRetryableFetchError("Load failed", 0) });
    act(() => reportSessionRejected(401));
    await waitFor(() => expect(auth.refreshSession).toHaveBeenCalled());
    expect(screen.getByText("Приложение открыто")).toBeInTheDocument();
    expect(auth.signOut).not.toHaveBeenCalled();
  });

  it("coalesces parallel 401s into one refresh and ends the session if the fresh token is rejected again", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();

    act(() => {
      reportSessionRejected(401, "jwt");
      reportSessionRejected(401, "jwt");
      reportSessionRejected(401, "jwt");
    });
    await waitFor(() => expect(getCurrentUser).toHaveBeenCalledTimes(2));
    expect(auth.refreshSession).toHaveBeenCalledOnce();
    expect(auth.signOut).not.toHaveBeenCalled();

    // Requests that left before the refresh, with the old token: no new cycle,
    // and only one re-ask however many of them come back.
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
    const invalidate = vi.spyOn(QueryClient.prototype, "invalidateQueries");
    act(() => {
      reportSessionRejected(401, "old-jwt");
      reportSessionRejected(401, "old-jwt");
      reportSessionRejected(401, "old-jwt");
    });
    expect(invalidate).toHaveBeenCalledOnce();
    invalidate.mockRestore();
    expect(auth.refreshSession).toHaveBeenCalledOnce();
    expect(auth.signOut).not.toHaveBeenCalled();

    // The fresh token is rejected too: end the session instead of looping.
    act(() => reportSessionRejected(401, "jwt"));
    expect(await screen.findByText("Сессия закончилась. Войдите снова.")).toBeInTheDocument();
    expect(auth.refreshSession).toHaveBeenCalledOnce();
    expect(auth.signOut).toHaveBeenCalledWith({ scope: "local" });
  });

  it("keeps the session when Supabase rate-limits the refresh", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase({ access_token: "jwt" });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "u1", email: "anna@example.com", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();

    auth.refreshSession.mockResolvedValue({ data: { session: null }, error: new AuthApiError("Request rate limit reached", 429, "over_request_rate_limit") });
    act(() => reportSessionRejected(401, "jwt"));
    await waitFor(() => expect(auth.refreshSession).toHaveBeenCalled());
    expect(screen.getByText("Приложение открыто")).toBeInTheDocument();
    expect(auth.signOut).not.toHaveBeenCalled();
  });

  it("does not promise a kept sign-in when even the settings are unreachable", async () => {
    vi.mocked(getAuthConfig).mockRejectedValue(new TypeError("Load failed"));
    renderGate();
    expect(await screen.findByRole("heading", { name: "Нет связи с сервером" })).toBeInTheDocument();
    expect(screen.getByText("Сервер сейчас не отвечает. Проверьте интернет и нажмите «Повторить».")).toBeInTheDocument();
    expect(screen.queryByText(/вход сохранён/)).not.toBeInTheDocument();
  });

  it("explains a sign-in result that arrived without its verifier and cleans the address", async () => {
    window.history.replaceState(null, "", "/?code=orphan-code");
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    fakeSupabase(null);
    renderGate();
    expect(await screen.findByText(/Вход не завершился в этом окне/)).toBeInTheDocument();
    expect(window.location.search).toBe("");
  });

  it("reports a failed code exchange as a sign-in error, not as 'opened elsewhere'", async () => {
    window.history.replaceState(null, "", "/?code=rejected-code");
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase(null);
    auth.initialize.mockResolvedValue({ error: new AuthApiError("invalid flow state", 400, "flow_state_not_found") });
    renderGate();
    expect(await screen.findByText(/сервис входа вернул ошибку/)).toBeInTheDocument();
    expect(window.location.search).toBe("");
  });

  it("says when a Google sign-in did not come back to the installed app", async () => {
    signInProgress.rememberProviderStarted("google");
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    fakeSupabase(null);
    renderGate();
    expect(await screen.findByText(/Вход через Google не вернулся в приложение/)).toBeInTheDocument();
    expect(signInProgress.providerStarted()).toBeNull();
  });

  it("remembers a started Google sign-in before leaving the page", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue(supabaseConfig);
    const auth = fakeSupabase(null);
    renderGate();
    fireEvent.click(await screen.findByRole("button", { name: "Продолжить через Google" }));
    await waitFor(() => expect(auth.signInWithOAuth).toHaveBeenCalledWith({ provider: "google", options: { redirectTo: `${window.location.origin}/` } }));
    expect(signInProgress.providerStarted()).toBe("google");
  });

  it("stays closed when sign-in is not configured", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue({ ...supabaseConfig, mode: "unconfigured", supabaseUrl: null, supabasePublishableKey: null });
    renderGate();
    expect(await screen.findByRole("heading", { name: "Вход ещё не настроен" })).toBeInTheDocument();
    expect(screen.queryByText("Приложение открыто")).not.toBeInTheDocument();
  });

  it("uses the local development bypass when the server accepts it", async () => {
    vi.mocked(getAuthConfig).mockResolvedValue({ ...supabaseConfig, mode: "developmentBypass", supabaseUrl: null, supabasePublishableKey: null, developmentBypass: true });
    vi.mocked(getCurrentUser).mockResolvedValue({ id: "local-development-user", email: "dev@localhost", status: "approved" });
    renderGate();
    expect(await screen.findByText("Приложение открыто")).toBeInTheDocument();
    expect(supabaseFor).not.toHaveBeenCalled();
  });
});
