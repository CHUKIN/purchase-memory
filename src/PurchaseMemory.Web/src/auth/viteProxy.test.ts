import { describe, expect, it } from "vitest";
import config from "../../vite.config";

/**
 * The API's local development bypass refuses LAN clients only because this
 * proxy appends the real client address to X-Forwarded-For. Dropping xfwd
 * would silently let any device on the network in.
 */
describe("Vite dev proxy", () => {
  it("forwards the real client address for every API route", () => {
    const proxy = (config as { server?: { proxy?: Record<string, unknown> } }).server?.proxy ?? {};
    for (const route of ["/api", "/health"]) {
      expect(proxy[route]).toMatchObject({ xfwd: true });
    }
  });
});
