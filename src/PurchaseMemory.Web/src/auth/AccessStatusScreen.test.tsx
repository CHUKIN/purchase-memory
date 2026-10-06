import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../app/AppProviders";
import { AccessStatusScreen, slowCheckDelayMs } from "./AccessStatusScreen";

describe("AccessStatusScreen", () => {
  it("tells a pending user the request is waiting and lets them check again or sign out", () => {
    const onRetry = vi.fn();
    const onSignOut = vi.fn();
    render(<AppProviders locale="ru"><AccessStatusScreen kind="pending" email="anna@example.com" onRetry={onRetry} onSignOut={onSignOut} /></AppProviders>);

    expect(screen.getByRole("heading", { level: 1, name: "Заявка отправлена" })).toBeInTheDocument();
    expect(screen.getByText(/anna@example.com/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Проверить снова" }));
    fireEvent.click(screen.getByRole("button", { name: "Выйти" }));
    expect(onRetry).toHaveBeenCalledOnce();
    expect(onSignOut).toHaveBeenCalledOnce();
  });

  it("announces a blocked account without a retry button", () => {
    render(<AppProviders locale="en"><AccessStatusScreen kind="blocked" email="boris@example.com" onRetry={vi.fn()} onSignOut={vi.fn()} /></AppProviders>);

    expect(screen.getByRole("alert")).toHaveTextContent("Access blocked");
    expect(screen.queryByRole("button", { name: "Check again" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  it("explains a slow start while the server wakes up", () => {
    vi.useFakeTimers();
    try {
      render(<AppProviders locale="ru"><AccessStatusScreen kind="checking" /></AppProviders>);
      expect(screen.getByRole("status")).toHaveTextContent("Проверяем вход…");
      expect(screen.queryByText(/Сервер просыпается/)).not.toBeInTheDocument();
      act(() => vi.advanceTimersByTime(slowCheckDelayMs));
      expect(screen.getByText(/Сервер просыпается/)).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it("offers a retry when the server cannot be reached", () => {
    const onRetry = vi.fn();
    render(<AppProviders locale="ru"><AccessStatusScreen kind="connectionError" onRetry={onRetry} /></AppProviders>);
    expect(screen.getByRole("alert")).toHaveTextContent("Нет связи с сервером");
    fireEvent.click(screen.getByRole("button", { name: "Повторить" }));
    expect(onRetry).toHaveBeenCalledOnce();
  });
});
