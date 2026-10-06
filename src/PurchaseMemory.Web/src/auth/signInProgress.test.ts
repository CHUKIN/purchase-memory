import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/client";
import { readAuthCallback, signInProgress } from "./signInProgress";
import { isTransientError, retryTransient, transientRetry } from "./transientRetry";

describe("readAuthCallback", () => {
  it("recognises PKCE codes and provider errors in the address", () => {
    expect(readAuthCallback("https://app.test/?code=abc")).toEqual({ isCallback: true, hasError: false });
    expect(readAuthCallback("https://app.test/?error=access_denied&error_description=x")).toEqual({ isCallback: true, hasError: true });
    expect(readAuthCallback("https://app.test/#error_code=otp_expired")).toEqual({ isCallback: true, hasError: true });
    expect(readAuthCallback("https://app.test/purchases?status=draft")).toEqual({ isCallback: false, hasError: false });
  });
});

describe("signInProgress", () => {
  afterEach(() => vi.useRealTimers());

  it("forgets an email step after the code would have expired", () => {
    vi.useFakeTimers();
    signInProgress.rememberEmailSent("anna@example.com");
    expect(signInProgress.emailSentTo()).toBe("anna@example.com");
    vi.advanceTimersByTime(61 * 60 * 1000);
    expect(signInProgress.emailSentTo()).toBeNull();
  });
});

describe("retryTransient", () => {
  const saved = { ...transientRetry };
  afterEach(() => Object.assign(transientRetry, saved));

  it("treats only network-level failures as worth another try", () => {
    expect(isTransientError(new TypeError("Load failed"))).toBe(true);
    expect(isTransientError(new ApiError("down", 503))).toBe(true);
    expect(isTransientError(new ApiError("unauthorized", 401))).toBe(false);
    expect(isTransientError(new ApiError("bad", 400))).toBe(false);
  });

  it("retries a sleeping server and gives up after the schedule", async () => {
    transientRetry.delaysMs = [0, 0];
    const action = vi.fn().mockRejectedValue(new ApiError("down", 502));
    await expect(retryTransient(action)).rejects.toMatchObject({ status: 502 });
    expect(action).toHaveBeenCalledTimes(3);
  });

  it("does not retry a real answer from the server", async () => {
    const action = vi.fn().mockRejectedValue(new ApiError("unauthorized", 401));
    await expect(retryTransient(action)).rejects.toMatchObject({ status: 401 });
    expect(action).toHaveBeenCalledOnce();
  });

  it("stops retrying once the overall time budget is spent", async () => {
    transientRetry.delaysMs = [0, 0, 0];
    transientRetry.totalBudgetMs = 0;
    const action = vi.fn().mockRejectedValue(new TypeError("Load failed"));
    await expect(retryTransient(action)).rejects.toThrow("Load failed");
    expect(action).toHaveBeenCalledOnce();
  });

  it("counts a hanging request as failed after the attempt timeout", async () => {
    transientRetry.delaysMs = [];
    transientRetry.attemptTimeoutMs = 5;
    await expect(retryTransient(() => new Promise(() => undefined))).rejects.toThrow("did not answer");
  });
});
