import type { Preview } from "@storybook/react-vite";
import { AppProviders } from "../src/app/AppProviders";
import type { AppLocale } from "../src/i18n/i18n";

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
        <Story />
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
        canvas: { name: "Canvas", value: "#f5f1e8" },
        paper: { name: "Paper", value: "#fffdf7" }
      }
    },
    viewport: {
      options: {
        phone: { name: "Phone 390 × 844", styles: { width: "390px", height: "844px" }, type: "mobile" },
        desktop: { name: "Desktop 1440 × 960", styles: { width: "1440px", height: "960px" }, type: "desktop" }
      }
    }
  },
  tags: ["autodocs"]
};

export default preview;
