import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import { AccessStatusScreen } from "./AccessStatusScreen";

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/Auth/AccessStatusScreen",
  component: AccessStatusScreen,
  parameters: { layout: "fullscreen" },
  args: {
    kind: "pending",
    email: "anna@example.com",
    busy: false,
    onRetry: fn(),
    onSignOut: fn()
  },
  argTypes: {
    kind: { control: "inline-radio", options: ["pending", "blocked", "unconfigured", "configError", "connectionError", "checking"] }
  }
} satisfies Meta<typeof AccessStatusScreen>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Signed in, waiting for the owner to approve in Supabase. */
export const PendingMobileRu: Story = { globals: phone };

export const PendingDesktopEn: Story = { globals: { ...desktop, locale: "en" } };

export const PendingChecking: Story = { globals: phone, args: { busy: true } };

export const BlockedMobileRu: Story = { globals: phone, args: { kind: "blocked", onRetry: undefined } };

/** No Supabase project configured and no local bypass: the app stays closed. */
export const UnconfiguredDesktopRu: Story = { globals: desktop, args: { kind: "unconfigured", email: null, onRetry: undefined, onSignOut: undefined } };

export const ServerUnavailableMobileEn: Story = { globals: { ...phone, locale: "en" }, args: { kind: "configError", email: null, onSignOut: undefined } };

/** A stored session could not be checked (offline or the server is still asleep): retry, not login. */
export const NoConnectionMobileRu: Story = { globals: phone, args: { kind: "connectionError", sessionKept: true, email: null, onSignOut: undefined } };

/** Start-up check; after a few seconds it explains that the server is waking up. */
export const CheckingMobileRu: Story = { globals: phone, args: { kind: "checking", email: null } };
