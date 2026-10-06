import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../app/AppProviders";
import { LoginScreen, normalizeCode } from "./LoginScreen";
import { signInProgress } from "./signInProgress";

function renderLogin(overrides: Partial<Parameters<typeof LoginScreen>[0]> = {}, locale: "ru" | "en" = "ru") {
  const props = {
    providers: ["email", "google"],
    onSendEmailLink: vi.fn().mockResolvedValue(undefined),
    onVerifyCode: vi.fn().mockResolvedValue(undefined),
    onProviderSignIn: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
  render(<AppProviders locale={locale}><LoginScreen {...props} /></AppProviders>);
  return props;
}

describe("LoginScreen", () => {
  it("offers the email link and every configured provider", () => {
    renderLogin({ providers: ["email", "google", "github"] });
    expect(screen.getByRole("heading", { level: 1, name: "Вход в историю покупок" })).toBeInTheDocument();
    expect(screen.getByLabelText("Электронная почта")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Продолжить через Google" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Продолжить через GitHub" })).toBeInTheDocument();
  });

  it("hides the email form when email sign-in is not configured", () => {
    renderLogin({ providers: ["google"] });
    expect(screen.queryByLabelText("Электронная почта")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Продолжить через Google" })).toBeInTheDocument();
  });

  it("validates the address, sends the link and then asks for the code", async () => {
    const props = renderLogin();
    fireEvent.click(screen.getByRole("button", { name: "Прислать ссылку для входа" }));
    expect(await screen.findByText("Введите адрес электронной почты")).toBeInTheDocument();
    expect(props.onSendEmailLink).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Электронная почта"), { target: { value: " anna@example.com " } });
    fireEvent.click(screen.getByRole("button", { name: "Прислать ссылку для входа" }));
    await waitFor(() => expect(props.onSendEmailLink).toHaveBeenCalledWith("anna@example.com"));
    expect(await screen.findByText(/Письмо отправлено на anna@example.com/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Код из письма"), { target: { value: "123456" } });
    fireEvent.click(screen.getByRole("button", { name: "Войти по коду" }));
    await waitFor(() => expect(props.onVerifyCode).toHaveBeenCalledWith("anna@example.com", "123456"));
  });

  it("leads with the code inside the installed app", async () => {
    const props = renderLogin({ standalone: true });
    expect(screen.getByText(/ссылка из письма откроется в браузере, а не здесь/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Прислать ссылку для входа" })).not.toBeInTheDocument();
    expect(screen.getByText(/вход через Google может открыться в отдельном окне/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Электронная почта"), { target: { value: "anna@example.com" } });
    fireEvent.click(screen.getByRole("button", { name: "Получить код на почту" }));
    await waitFor(() => expect(props.onSendEmailLink).toHaveBeenCalledWith("anna@example.com"));
    expect(await screen.findByText(/Не нажимайте ссылку в письме/)).toBeInTheDocument();
    expect(screen.getByLabelText("Код из письма")).toHaveFocus();
  });

  it("makes the code field fit iPhone autofill and accepts 6 to 10 digits", async () => {
    const props = renderLogin({ initialSentTo: "anna@example.com" });
    const input = screen.getByLabelText("Код из письма");
    expect(input).toHaveAttribute("autocomplete", "one-time-code");
    expect(input).toHaveAttribute("inputmode", "numeric");
    expect(input).toHaveAttribute("maxlength", "10");
    const submit = screen.getByRole("button", { name: "Войти по коду" });

    fireEvent.change(input, { target: { value: "12345" } });
    expect(submit).toBeDisabled();

    fireEvent.change(input, { target: { value: "Код: 1234 5678" } });
    expect(input).toHaveValue("12345678");
    expect(submit).toBeEnabled();
    fireEvent.click(submit);
    await waitFor(() => expect(props.onVerifyCode).toHaveBeenCalledWith("anna@example.com", "12345678"));
  });

  it("returns to the code step after the installed app was restarted", () => {
    signInProgress.rememberEmailSent("anna@example.com");
    renderLogin({ standalone: true });
    expect(screen.getByText(/Письмо отправлено на anna@example.com/)).toBeInTheDocument();
    expect(screen.getByLabelText("Код из письма")).toBeInTheDocument();
  });

  it("sends the email again and lets the user change the address", async () => {
    const props = renderLogin({ initialSentTo: "anna@example.com" });
    fireEvent.click(screen.getByRole("button", { name: "Отправить письмо ещё раз" }));
    await waitFor(() => expect(props.onSendEmailLink).toHaveBeenCalledWith("anna@example.com"));
    expect(await screen.findByText("Новое письмо отправлено. Подходит код только из него.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Другой адрес" }));
    expect(screen.getByLabelText("Электронная почта")).toBeInTheDocument();
    expect(signInProgress.emailSentTo()).toBeNull();
  });

  it("says so when a provider window was closed without finishing", async () => {
    renderLogin();
    signInProgress.rememberProviderStarted("google");
    fireEvent(window, new Event("pageshow"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Вход через Google не вернулся в приложение.");
    expect(signInProgress.providerStarted()).toBeNull();
  });

  it("keeps only digits of a pasted code", () => {
    expect(normalizeCode(" 123 456 ")).toBe("123456");
    expect(normalizeCode("12345678901234")).toBe("1234567890");
  });

  it("reports a failed provider start as an alert", async () => {
    const props = renderLogin({ onProviderSignIn: vi.fn().mockRejectedValue(new Error("boom")) });
    fireEvent.click(screen.getByRole("button", { name: "Продолжить через Google" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось начать вход через Google.");
    expect(props.onProviderSignIn).toHaveBeenCalledWith("google");
  });

  it("speaks English", () => {
    renderLogin({}, "en");
    expect(screen.getByRole("heading", { level: 1, name: "Sign in to Purchase Memory" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continue with Google" })).toBeInTheDocument();
  });
});
