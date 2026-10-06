import type { StorybookConfig } from "@storybook/react-vite";
import type { PluginOption } from "vite";

function withoutPwa(plugin: PluginOption): PluginOption {
  if (Array.isArray(plugin)) return plugin.map(withoutPwa);
  const pluginName = (plugin as { name?: string } | null)?.name;
  return pluginName?.startsWith("vite-plugin-pwa") ? false : plugin;
}

const config: StorybookConfig = {
  stories: ["../src/**/*.stories.@(ts|tsx)"],
  addons: ["@storybook/addon-docs", "@storybook/addon-a11y"],
  framework: {
    name: "@storybook/react-vite",
    options: {}
  },
  staticDirs: ["../public"],
  viteFinal: (config) => {
    // Storybook is a component lab, not the production PWA. Reusing the app's
    // Vite config must not generate a service worker for the Storybook manager.
    config.plugins = config.plugins?.map(withoutPwa);
    // Storybook's manager, docs renderer and bundled axe runtime are expected
    // to be larger than product chunks; this keeps the build signal useful.
    config.build = { ...config.build, chunkSizeWarningLimit: 1_500 };
    return config;
  }
};

export default config;
