import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { VitePWA, type ManifestOptions, type VitePWAOptions } from "vite-plugin-pwa";

/** Exported for `src/auth/pwaShell.test.ts`: the iPhone Home Screen app depends on these. */
export const pwaManifest: Partial<ManifestOptions> = {
  // A stable id keeps the installed app the same app if start_url ever changes.
  id: "/",
  name: "Purchase Memory",
  short_name: "Покупки",
  description: "Личная история покупок и чеков",
  lang: "ru",
  theme_color: "#315c4b",
  background_color: "#f5f1e8",
  display: "standalone",
  start_url: "/",
  scope: "/",
  icons: [
    { src: "/icons/icon-192.png", sizes: "192x192", type: "image/png" },
    { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png" },
    { src: "/icons/icon-512-maskable.png", sizes: "512x512", type: "image/png", purpose: "maskable" }
  ]
};

export const pwaWorkbox: NonNullable<VitePWAOptions["workbox"]> = {
  navigateFallback: "/index.html",
  // Server-owned addresses must reach the network even if a browser
  // ever navigates to them; the SPA shell is never a substitute there.
  navigateFallbackDenylist: [/^\/api\//, /^\/mcp(\/|$)/, /^\/health$/],
  globPatterns: ["**/*.{js,css,html,svg,png,woff2}"],
  runtimeCaching: []
};

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["receipt.svg", "apple-touch-icon.png", "icons/*.png"],
      manifest: pwaManifest,
      workbox: pwaWorkbox
    })
  ],
  test: {
    environment: "jsdom",
    setupFiles: "./src/test/setup.ts",
    pool: "vmThreads"
  },
  build: {
    outDir: "../PurchaseMemory.Api/wwwroot",
    emptyOutDir: true,
    sourcemap: true
  },
  server: {
    host: "0.0.0.0",
    port: 5173,
    // xfwd makes the proxy append the real client address to X-Forwarded-For.
    // The API's local development bypass relies on it to refuse LAN clients
    // that reach it through this proxy (see DevelopmentBypass.cs).
    proxy: {
      "/api": { target: "http://127.0.0.1:5099", xfwd: true },
      "/health": { target: "http://127.0.0.1:5099", xfwd: true }
    }
  }
});
