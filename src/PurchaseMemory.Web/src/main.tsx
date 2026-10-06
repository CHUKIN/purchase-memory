import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { registerSW } from "virtual:pwa-register";
import { App } from "./app/App";
import { AppProviders } from "./app/AppProviders";
import { AuthGate } from "./auth/AuthGate";

registerSW({ immediate: true });

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AppProviders>
      <AuthGate>
        <App />
      </AuthGate>
    </AppProviders>
  </StrictMode>
);
