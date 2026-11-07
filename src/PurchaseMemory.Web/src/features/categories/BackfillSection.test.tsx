import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, getCategorizationPending, runCategorizationBackfill } from "../../api/client";
import type { CategorizationRunSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { BackfillSection } from "./BackfillSection";

vi.mock("../../api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/client")>()),
  getCategorizationPending: vi.fn(),
  runCategorizationBackfill: vi.fn()
}));

const summary = (extra: Partial<CategorizationRunSummary> = {}): CategorizationRunSummary => ({
  configured: true, considered: 12, suggested: 9, failedBatches: 0, inputTokens: 100, outputTokens: 50, namesSuggested: 4, ...extra
});

function renderSection() {
  render(
    <AppProviders locale="ru">
      <MemoryRouter><BackfillSection /></MemoryRouter>
    </AppProviders>
  );
}

const openSection = async () => fireEvent.click(await screen.findByRole("button", { name: "Дозаполнить названия и категории" }));
const press = async (name: string) => fireEvent.click(await screen.findByRole("button", { name }));

describe("Backfill section in «Ещё»", () => {
  beforeEach(() => {
    vi.mocked(getCategorizationPending).mockReset().mockResolvedValue({ unclassified: 7, unnamed: 5, waiting: 12, waitingWithRetry: 12 });
    vi.mocked(runCategorizationBackfill).mockReset().mockResolvedValue(summary());
  });

  it("asks nothing until the section is opened, then shows what is waiting and the cost", async () => {
    renderSection();
    expect(getCategorizationPending).not.toHaveBeenCalled();
    await openSection();

    expect(await screen.findByText(/Ждут 12 товаров \(без категории: 7, без названия: 5\)/)).toBeInTheDocument();
    expect(screen.getByText(/около 0,0001 \$ за товар/)).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(runCategorizationBackfill).not.toHaveBeenCalled();
  });

  it("needs a confirm, runs once with the limit and shows the result in words", async () => {
    vi.mocked(runCategorizationBackfill).mockResolvedValue(summary({ failedBatches: 1 }));
    renderSection();
    await openSection();
    await press("Дозаполнить");

    expect(runCategorizationBackfill).not.toHaveBeenCalled();
    expect(screen.getByText(/Отправить AI 12 товаров\?/)).toBeInTheDocument();
    await press("Да, запустить");

    expect(await screen.findByText("Просмотрено товаров: 12.")).toBeInTheDocument();
    expect(screen.getByText(/Получили предложение .*: 9, из них названия: 4/)).toBeInTheDocument();
    expect(screen.getByText(/сбоев: 1/)).toBeInTheDocument();
    expect(runCategorizationBackfill).toHaveBeenCalledTimes(1);
    expect(runCategorizationBackfill).toHaveBeenCalledWith(100, false);
  });

  it("can be cancelled at the confirm step without a paid call", async () => {
    renderSection();
    await openSection();
    await press("Дозаполнить");
    await press("Отмена");

    expect(runCategorizationBackfill).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Дозаполнить" })).toBeInTheDocument();
  });

  it("is busy while running and cannot be fired twice", async () => {
    let finish: (value: CategorizationRunSummary) => void = () => undefined;
    vi.mocked(runCategorizationBackfill).mockReturnValue(new Promise((resolve) => { finish = resolve; }));
    renderSection();
    await openSection();
    await press("Дозаполнить");
    await press("Да, запустить");

    expect(await screen.findByText(/AI работает/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Да, запустить" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Дозаполнить" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Дозаполнить" }));
    expect(runCategorizationBackfill).toHaveBeenCalledTimes(1);

    finish(summary());
    expect(await screen.findByText("Просмотрено товаров: 12.")).toBeInTheDocument();
  });

  it("says «already running» on 409", async () => {
    vi.mocked(runCategorizationBackfill).mockRejectedValue(new ApiError("A categorization run is already in progress.", 409));
    renderSection();
    await openSection();
    await press("Дозаполнить");
    await press("Да, запустить");

    expect(await screen.findByText("Уже идёт, подождите.")).toBeInTheDocument();
  });

  it("explains a server without an AI key", async () => {
    vi.mocked(runCategorizationBackfill).mockResolvedValue({ configured: false, considered: 0, suggested: 0, failedBatches: 0, inputTokens: 0, outputTokens: 0 });
    renderSection();
    await openSection();
    await press("Дозаполнить");
    await press("Да, запустить");

    expect(await screen.findByText(/AI на сервере не подключён/)).toBeInTheDocument();
    expect(screen.queryByText(/Просмотрено товаров/)).not.toBeInTheDocument();
  });

  it("retries the attempted ones only when the checkbox is ticked", async () => {
    vi.mocked(getCategorizationPending).mockResolvedValue({ unclassified: 0, unnamed: 0, waiting: 0, waitingWithRetry: 3 });
    renderSection();
    await openSection();

    expect(await screen.findByText(/Все товары уже с названиями и категориями/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Дозаполнить" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: /Повторить для тех, где раньше не получилось/ }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Дозаполнить" })).toBeEnabled());
    await press("Дозаполнить");
    await press("Да, запустить");
    await waitFor(() => expect(runCategorizationBackfill).toHaveBeenCalledWith(100, true));
  });

  it("offers a reload when the count cannot be read and spends nothing", async () => {
    vi.mocked(getCategorizationPending).mockRejectedValue(new ApiError("down", 500));
    renderSection();
    await openSection();

    expect(await screen.findByText(/Не получилось узнать, сколько товаров ждут/)).toBeInTheDocument();
    expect(runCategorizationBackfill).not.toHaveBeenCalled();
  });
});
