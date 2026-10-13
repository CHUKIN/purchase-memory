import { setupIonicReact } from "@ionic/react";
import "@ionic/react/css/core.css";
import "@ionic/react/css/normalize.css";
import "@ionic/react/css/structure.css";
import "@ionic/react/css/typography.css";
import "@ionic/react/css/padding.css";
import "@ionic/react/css/text-alignment.css";
// No palettes/dark.*.css on purpose: the app is light only, also when iOS is in dark mode.
import "../styles.css";

/**
 * The iOS look on every device, including Android and the desktop browser:
 * the household uses iPhones and the owner asked for an iPhone-like app.
 * Imported once by the app, Storybook and AppProviders.
 */
setupIonicReact({ mode: "ios" });
