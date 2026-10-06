import { ApiError } from "../api/client";

/**
 * The free Render instance sleeps when idle; the first request after that can
 * take 30–60 seconds or briefly fail with 502/503. Sign-in checks retry such
 * failures instead of treating them as "signed out".
 */
export const transientRetry = {
  /** Pauses between attempts. Tests shorten them. */
  delaysMs: [1_000, 2_000, 4_000, 8_000, 15_000, 15_000],
  /** One attempt may wait this long for a waking server before it counts as failed. */
  attemptTimeoutMs: 75_000,
  /** No new attempt starts after this much time; the caller then shows "no connection". */
  totalBudgetMs: 150_000
};

export class AttemptTimeoutError extends Error {
  constructor() {
    super("The server did not answer in time.");
    this.name = "AttemptTimeoutError";
  }
}

export function isTransientError(error: unknown): boolean {
  if (error instanceof AttemptTimeoutError) return true;
  // fetch() rejects with TypeError when the network or the server is unreachable.
  if (error instanceof TypeError) return true;
  if (error instanceof DOMException && (error.name === "AbortError" || error.name === "TimeoutError" || error.name === "NetworkError")) return true;
  if (error instanceof ApiError) return error.status === 0 || error.status === 408 || error.status === 502 || error.status === 503 || error.status === 504;
  return false;
}

function withTimeout<T>(promise: Promise<T>, timeoutMs: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new AttemptTimeoutError()), timeoutMs);
    promise.then(
      (value) => { clearTimeout(timer); resolve(value); },
      (error: unknown) => { clearTimeout(timer); reject(error); }
    );
  });
}

const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Runs `action`, retrying only network-level failures; any other error is thrown at once. */
export async function retryTransient<T>(action: () => Promise<T>, isCancelled: () => boolean = () => false): Promise<T> {
  const delays = transientRetry.delaysMs;
  const startedAt = Date.now();
  for (let attempt = 0; ; attempt += 1) {
    try {
      return await withTimeout(action(), transientRetry.attemptTimeoutMs);
    } catch (error) {
      const outOfTime = Date.now() - startedAt >= transientRetry.totalBudgetMs;
      if (!isTransientError(error) || attempt >= delays.length || outOfTime || isCancelled()) throw error;
      await pause(delays[attempt]);
      if (isCancelled()) throw error;
    }
  }
}
