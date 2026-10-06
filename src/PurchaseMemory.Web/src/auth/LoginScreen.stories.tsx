import type { Meta, StoryObj } from "@storybook/react-vite";
import { fn } from "storybook/test";
import { LoginScreen } from "./LoginScreen";

const phone = { viewport: { value: "phone", isRotated: false } };
const desktop = { viewport: { value: "desktop", isRotated: false } };

const meta = {
  title: "Purchase Memory/Auth/LoginScreen",
  component: LoginScreen,
  parameters: { layout: "fullscreen" },
  args: {
    providers: ["email", "google"],
    onSendEmailLink: fn(async () => undefined),
    onVerifyCode: fn(async () => undefined),
    onProviderSignIn: fn(async () => undefined)
  }
} satisfies Meta<typeof LoginScreen>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Email link plus Google, the owner's starting setup. */
export const EmailAndGoogleMobileRu: Story = { globals: phone };

export const EmailAndGoogleDesktopEn: Story = { globals: { ...desktop, locale: "en" } };

/** After the email is sent in a browser: open the link or type the code. */
export const LinkSentMobileRu: Story = { globals: phone, args: { initialSentTo: "anna@example.com" } };

/** Installed iPhone app: the link would open in the browser, so the screen asks for the code. */
export const HomeScreenAppMobileRu: Story = { globals: phone, args: { standalone: true } };

export const HomeScreenAppCodeStepMobileRu: Story = { globals: phone, args: { standalone: true, initialSentTo: "anna@example.com" } };

export const HomeScreenAppCodeStepMobileEn: Story = { globals: { ...phone, locale: "en" }, args: { standalone: true, initialSentTo: "anna@example.com" } };

/** Google finished in another window (no PKCE verifier here): explained, with the code as the way out. */
export const SignInFinishedElsewhereMobileRu: Story = {
  globals: phone,
  args: { standalone: true, notice: "Вход не завершился в этом окне: ссылка из письма или Google открылись не там, где вы начинали вход. Вернитесь туда и введите код из письма или начните вход здесь." }
};

/** Providers come from configuration; any enabled in Supabase appear as buttons. */
export const SeveralProvidersDesktopRu: Story = { globals: desktop, args: { providers: ["email", "google", "apple", "github"] } };

export const SessionExpiredMobileRu: Story = { globals: phone, args: { notice: "Сессия закончилась. Войдите снова." } };

/** Sending fails: the error is announced as an alert. */
export const SendFailsMobileEn: Story = {
  globals: { ...phone, locale: "en" },
  args: { onSendEmailLink: fn(async () => { throw new Error("rate limited"); }) }
};
