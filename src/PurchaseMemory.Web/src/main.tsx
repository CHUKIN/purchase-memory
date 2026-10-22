import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { registerSW } from "virtual:pwa-register";
import { App } from "./app/App";
import { AppProviders } from "./app/AppProviders";
import { AuthGate } from "./auth/AuthGate";
import { settleReloadedLayerEntry } from "./design-system/history/historyLayers";

registerSW({ immediate: true });
// Reloaded with a sheet or inner screen open: start on the screen's own history entry.
settleReloadedLayerEntry();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AppProviders>
      <AuthGate>
        <App />
      </AuthGate>
    </AppProviders>
  </StrictMode>
);
