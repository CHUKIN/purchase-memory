import { describe, expect, it } from "vitest";
import html from "../../index.html?raw";
import { pwaManifest, pwaWorkbox } from "../../vite.config";

/**
 * The Home Screen app on iPhone depends on these: the icon and name come from
 * index.html, and the service worker must never answer server routes with
 * the cached SPA page.
 */
describe("PWA shell", () => {
  it("declares the iPhone Home Screen icon, title and safe-area viewport", () => {
    expect(html).toContain('<link rel="apple-touch-icon" href="/apple-touch-icon.png" />');
    expect(html).toContain('name="apple-mobile-web-app-title"');
    expect(html).toContain("viewport-fit=cover");
  });

  it("installs as a standalone app covering the whole site", () => {
    expect(pwaManifest).toMatchObject({ id: "/", display: "standalone", start_url: "/", scope: "/" });
    expect(pwaManifest.icons?.some((icon) => icon.purpose === "maskable")).toBe(true);
  });

  it("keeps API, MCP and health routes out of the offline navigation fallback", () => {
    const denylist = pwaWorkbox.navigateFallbackDenylist ?? [];
    for (const path of ["/api/me", "/mcp", "/mcp/x", "/health"]) {
      expect(denylist.some((pattern) => pattern.test(path))).toBe(true);
    }
    expect(denylist.some((pattern) => pattern.test("/purchases"))).toBe(false);
    expect(pwaWorkbox.runtimeCaching ?? []).toHaveLength(0);
  });
});
