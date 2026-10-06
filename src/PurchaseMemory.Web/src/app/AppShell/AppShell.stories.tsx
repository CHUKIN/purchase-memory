import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import { Surface } from "../../design-system/components";
import { AppShell } from "./AppShell";

interface ShellStoryProps {
  path: string;
  title: string;
  recognitionAvailable: boolean | null;
}

function ShellStory({ path, title, recognitionAvailable }: ShellStoryProps) {
  return (
    <MemoryRouter initialEntries={[path]}>
      <AppShell
        recognition={recognitionAvailable === null ? null : {
          available: recognitionAvailable,
          mode: recognitionAvailable ? "Hybrid" : "unknown",
          models: recognitionAvailable ? ["glm-ocr", "qwen3.5:4b"] : [],
          message: null
        }}
      >
        <div style={{ padding: "2rem" }}>
          <Surface><h1 style={{ margin: 0 }}>{title}</h1></Surface>
        </div>
      </AppShell>
    </MemoryRouter>
  );
}

const meta = {
  title: "Purchase Memory/AppShell",
  component: ShellStory,
  parameters: { layout: "fullscreen" },
  args: { path: "/", title: "Home", recognitionAvailable: true }
} satisfies Meta<typeof ShellStory>;

export default meta;
type Story = StoryObj<typeof meta>;

export const MobileHomeActive: Story = {
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const MobilePurchasesActive: Story = {
  args: { path: "/purchases", title: "Purchases" },
  globals: { viewport: { value: "phone", isRotated: false } }
};

export const DesktopHomeActive: Story = {
  globals: { viewport: { value: "desktop", isRotated: false } }
};

export const DesktopRecognitionChecking: Story = {
  args: { recognitionAvailable: null },
  globals: { viewport: { value: "desktop", isRotated: false } }
};

export const DesktopNeedsAttentionActive: Story = {
  args: { path: "/needs-attention", title: "Needs attention", recognitionAvailable: false },
  globals: { viewport: { value: "desktop", isRotated: false } }
};
