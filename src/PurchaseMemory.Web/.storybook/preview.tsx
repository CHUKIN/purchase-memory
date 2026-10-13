import { IonContent } from "@ionic/react";
import type { Preview } from "@storybook/react-vite";
// AppProviders imports src/app/ionic.ts: Ionic's CSS, the app theme and setupIonicReact({ mode: "ios" }).
import { AppProviders } from "../src/app/AppProviders";
import type { AppLocale } from "../src/i18n/i18n";
import "./preview.css";

const preview: Preview = {
  globalTypes: {
    locale: {
      description: "Interface language",
      defaultValue: "ru",
      toolbar: {
        icon: "globe",
        items: [
          { value: "ru", title: "Русский" },
          { value: "en", title: "English" }
        ]
      }
    }
  },
  initialGlobals: {
    locale: "ru"
  },
  decorators: [
    (Story, context) => (
      <AppProviders locale={context.globals.locale as AppLocale}>
        {context.parameters.layout === "fullscreen"
          // Whole screens bring their own Ionic page (navigation bar and content).
          ? <Story />
          // A part of a screen sits on the grouped background, scrollable like in the app.
          : (
            <div className="ion-page">
              <IonContent color="light">
                <div className="pm-page-body">
                  <Story />
                </div>
              </IonContent>
            </div>
          )}
      </AppProviders>
    )
  ],
  parameters: {
    controls: {
      matchers: {
        color: /(background|color)$/i,
        date: /Date$/i
      }
    },
    a11y: {
      test: "error"
    },
    backgrounds: {
      options: {
        grouped: { name: "iOS grouped", value: "#f2f2f7" },
        white: { name: "White", value: "#ffffff" }
      }
    },
    viewport: {
      options: {
        phone: { name: "iPhone 390 × 844", styles: { width: "390px", height: "844px" }, type: "mobile" },
        desktop: { name: "Desktop 1440 × 960", styles: { width: "1440px", height: "960px" }, type: "desktop" }
      }
    }
  },
  tags: ["autodocs"]
};

export default preview;
