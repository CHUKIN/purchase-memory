import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  ApiError,
  askAnalytics,
  deleteAnalyticsHistoryEntry,
  getAnalyticsContext,
  getAnalyticsHistoryEntry,
  listAnalyticsHistory,
  listCategories,
  listTags,
  refreshAnalyticsHistoryEntry,
  runAnalyticsPlan
} from "../../api/client";
import type { AnalyticsAnswer, AnalyticsAskResponse } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { sampleCategories } from "../categories/categoryFixtures";
import { AnalyticsPage } from "./AnalyticsPage";
import { AnalyticsView, type AnalyticsViewProps } from "./AnalyticsView";
import {
  allByTagAnswer,
  biedronkaSpendAnswer,
  coffeePriceAnswer,
  kidsByTagAnswer,
  milkQuantityAnswer,
  pastaNoAnswer,
  sampleAnalyticsHistory,
  sampleAnalyticsTags
} from "./analyticsFixtures";

vi.mock("../../api/client", async (importOriginal) => {
  const original = await importOriginal<typeof import("../../api/client")>();
  return {
    ApiError: original.ApiError,
    getAnalyticsContext: vi.fn(),
    listCategories: vi.fn(),
    listTags: vi.fn(),
    askAnalytics: vi.fn(),
    runAnalyticsPlan: vi.fn(),
    listAnalyticsHistory: vi.fn(),
    getAnalyticsHistoryEntry: vi.fn(),
    refreshAnalyticsHistoryEntry: vi.fn(),
    deleteAnalyticsHistoryEntry: vi.fn()
  };
});

const asked = (answer: AnalyticsAnswer, historyId: string | null = "history-new", reused = false): AnalyticsAskResponse =>
  ({ ...answer, historyId, reused });

beforeEach(() => {
  vi.mocked(listAnalyticsHistory).mockReset().mockResolvedValue({ items: [], hasMore: false });
});

function renderView(props: Partial<AnalyticsViewProps> = {}) {
  const defaults: AnalyticsViewProps = {
    enabled: true,
    question: "",
    onQuestionChange: vi.fn(),
    onAsk: vi.fn(),
    asking: false,
    rerunning: false,
    problem: null,
    answer: null,
    followUp: null,
    onClearFollowUp: vi.fn(),
    onPlanChange: vi.fn(),
    categories: sampleCategories,
    merchants: ["Biedronka", "Lidl"],
    locale: "ru-RU"
  };
  const merged = { ...defaults, ...props };
  return { ...render(<AppProviders><MemoryRouter><AnalyticsView {...merged} /></MemoryRouter></AppProviders>), props: merged };
}

describe("AnalyticsView", () => {
  it("offers tappable examples that ask right away", () => {
    const { props } = renderView();
    fireEvent.click(screen.getByRole("button", { name: "Сколько потратили в Biedronce в августе?" }));
    expect(props.onQuestionChange).toHaveBeenCalledWith("Сколько потратили в Biedronce в августе?");
    expect(props.onAsk).toHaveBeenCalledWith("Сколько потратили в Biedronce в августе?");
  });

  it("submits a typed question with Enter and ignores blanks", () => {
    const { props } = renderView({ question: "  Сколько кофе?  " });
    const input = screen.getByLabelText("Ваш вопрос");
    fireEvent.keyDown(input, { key: "Enter" });
    expect(props.onAsk).toHaveBeenCalledWith("Сколько кофе?");
  });

  it("shows the conclusion, numbers, chart table, coverage and linked purchases", () => {
    renderView({ answer: milkQuantityAnswer });
    expect(screen.getByRole("heading", { level: 2, name: "Куплено 21 шт.: 17 позиций в 14 чеках." })).toBeInTheDocument();
    expect(screen.getByText("Куплено").nextSibling).toHaveTextContent("21 шт.");
    const table = screen.getByRole("table");
    expect(within(table).getAllByRole("row")).toHaveLength(10);
    expect(screen.getByText(/Учтено 14 чеков, 17 позиций/)).toBeInTheDocument();
    expect(screen.getByText(/23 позиции за период без категории — фильтр по категории их не видит/)).toBeInTheDocument();
    expect(screen.getByText(/1 скидка на весь чек не учтена/)).toBeInTheDocument();
    expect(screen.getByText("У 3 позиций не указано количество — они посчитаны как 1 шт.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Biedronka/ })).toHaveAttribute("href", "/receipts/r1");
    expect(screen.getByRole("link", { name: "MLEKO LACIATE 3,2% 1L" })).toHaveAttribute("href", "/products/prod-milk");
    expect(screen.getByText("Показаны 2 из 14 чеков.")).toBeInTheDocument();
  });

  it("shows the interpretation as chips and removes a filter without asking the model", () => {
    const { props } = renderView({ answer: milkQuantityAnswer });
    const chips = screen.getByRole("list", { name: "Фильтры ответа" });
    expect(within(chips).getByText("Количество")).toBeInTheDocument();
    expect(within(chips).getByText("Молочное и яйца / Молоко")).toBeInTheDocument();
    expect(within(chips).getByText("Все магазины")).toBeInTheDocument();
    expect(within(chips).getByText("01.01.2026 – 26.09.2026")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Убрать фильтр: Молочное и яйца / Молоко" }));
    expect(props.onPlanChange).toHaveBeenCalledWith({ ...milkQuantityAnswer.plan, categoryIds: [] });
    expect(props.onAsk).not.toHaveBeenCalled();
  });

  it("edits the period and store in a form", () => {
    const { props } = renderView({ answer: biedronkaSpendAnswer });
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    fireEvent.change(screen.getByLabelText("С даты"), { target: { value: "2026-07-01" } });
    fireEvent.change(screen.getByLabelText("Магазин"), { target: { value: "Lidl" } });
    fireEvent.change(screen.getByLabelText("Разбивка"), { target: { value: "week" } });
    fireEvent.click(screen.getByRole("button", { name: "Пересчитать" }));
    expect(props.onPlanChange).toHaveBeenCalledWith({
      ...biedronkaSpendAnswer.plan, from: "2026-07-01", merchants: ["Lidl"], groupBy: "week"
    });
  });

  it("shows a tag filter as a removable chip and a breakdown by tag with the overlap note", () => {
    const { props } = renderView({ answer: kidsByTagAnswer, tags: sampleAnalyticsTags });
    const chips = screen.getByRole("list", { name: "Фильтры ответа" });
    expect(within(chips).getByText("#Для ребёнка")).toBeInTheDocument();
    expect(within(chips).getByText("По тегам")).toBeInTheDocument();
    expect(screen.getByText("Один из тегов не найден — этот фильтр убран.")).toBeInTheDocument();
    const table = screen.getByRole("table");
    expect(within(table).getByText("Тег")).toBeInTheDocument();
    expect(within(table).getByText("Акция")).toBeInTheDocument();
    expect(screen.getByText(/сумма строк может быть больше итога/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Убрать фильтр: #Для ребёнка" }));
    expect(props.onPlanChange).toHaveBeenCalledWith({ ...kidsByTagAnswer.plan, tagIds: [] });
    expect(props.onAsk).not.toHaveBeenCalled();
  });

  it("names untagged lines «Без тега» and hides the overlap note when nothing overlaps", () => {
    const noOverlap = {
      ...allByTagAnswer,
      result: { ...allByTagAnswer.result, coverage: { ...allByTagAnswer.result.coverage, multiTaggedLines: 0 } }
    };
    renderView({ answer: noOverlap, tags: sampleAnalyticsTags });
    expect(within(screen.getByRole("table")).getByText("Без тега")).toBeInTheDocument();
    expect(screen.queryByText(/сумма строк может быть больше итога/)).not.toBeInTheDocument();
  });

  it("picks a tag and the breakdown by tag in the form", () => {
    const { props } = renderView({ answer: biedronkaSpendAnswer, tags: sampleAnalyticsTags });
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    const tagField = screen.getByLabelText("Тег");
    expect(tagField).toHaveValue("");
    fireEvent.change(tagField, { target: { value: "tag-promo" } });
    fireEvent.change(screen.getByLabelText("Разбивка"), { target: { value: "tag" } });
    fireEvent.click(screen.getByRole("button", { name: "Пересчитать" }));
    expect(props.onPlanChange).toHaveBeenCalledWith({ ...biedronkaSpendAnswer.plan, tagIds: ["tag-promo"], groupBy: "tag" });
  });

  it("keeps several tags when only the period changes, and «Все теги» clears them", () => {
    const twoTags = { ...kidsByTagAnswer, plan: { ...kidsByTagAnswer.plan, tagIds: ["tag-kid", "tag-promo"] } };
    const { props } = renderView({ answer: twoTags, tags: sampleAnalyticsTags });
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    fireEvent.change(screen.getByLabelText("С даты"), { target: { value: "2026-08-01" } });
    fireEvent.click(screen.getByRole("button", { name: "Пересчитать" }));
    expect(props.onPlanChange).toHaveBeenLastCalledWith({ ...twoTags.plan, from: "2026-08-01" });

    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    fireEvent.change(screen.getByLabelText("Тег"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Пересчитать" }));
    expect(props.onPlanChange).toHaveBeenLastCalledWith({ ...twoTags.plan, tagIds: [] });
  });

  it("offers no tag field or tag breakdown to a household without tags", () => {
    renderView({ answer: biedronkaSpendAnswer, tags: [] });
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    expect(screen.queryByLabelText("Тег")).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("Разбивка")).queryByRole("option", { name: "По тегам" })).not.toBeInTheDocument();
  });

  it("explains dropped filters and price units", () => {
    renderView({ answer: coffeePriceAnswer });
    expect(screen.getByText("Магазин «Carrefour» не встречается в чеках — фильтр убран.")).toBeInTheDocument();
    expect(screen.getByText("Средняя цена за шт.")).toBeInTheDocument();
    expect(screen.getByText(/1 позиция в другой единице измерения/)).toBeInTheDocument();
  });

  it("says «no» honestly with a hint", () => {
    renderView({ answer: pastaNoAnswer });
    expect(screen.getByRole("heading", { level: 2, name: "Нет, таких покупок не нашлось." })).toBeInTheDocument();
    expect(screen.getByText(/Товары без категории находятся только по названию/)).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("shows follow-up context and lets the user start over", () => {
    const { props } = renderView({ answer: milkQuantityAnswer, followUp: milkQuantityAnswer.plan });
    expect(screen.getByText(/Уточняем прошлый ответ/)).toBeInTheDocument();
    expect(screen.getByLabelText("Уточните вопрос")).toBeInTheDocument();
    expect(screen.queryByText("Попробуйте:")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Новый вопрос" }));
    expect(props.onClearFollowUp).toHaveBeenCalled();
  });

  it("announces problems and the disabled state", () => {
    const { unmount } = renderView({ problem: { kind: "rateLimited", retryAfterSeconds: 170 } });
    expect(screen.getByRole("alert")).toHaveTextContent("примерно через 3 мин");
    unmount();
    renderView({ enabled: false });
    expect(screen.getByLabelText("Ваш вопрос")).toBeDisabled();
    expect(screen.getByText(/Вопросы сейчас выключены на сервере/)).toBeInTheDocument();
  });

  it("moves focus to a newly arrived answer", () => {
    const { rerender, props } = renderView();
    rerender(<AppProviders><MemoryRouter><AnalyticsView {...props} answer={biedronkaSpendAnswer} /></MemoryRouter></AppProviders>);
    expect(screen.getByRole("heading", { level: 2 })).toHaveFocus();
  });

  it("lists previous questions with who asked, opens one and deletes only after confirming", () => {
    const { props } = renderView({
      history: sampleAnalyticsHistory,
      historyHasMore: true,
      onOpenHistory: vi.fn(),
      onDeleteHistory: vi.fn(),
      onShowMoreHistory: vi.fn(),
      activeHistoryId: "history-milk"
    });
    const section = screen.getByRole("region", { name: "Предыдущие вопросы" });
    const items = within(section).getAllByRole("listitem");
    expect(items).toHaveLength(3);
    expect(within(items[0]).getByText("вы")).toBeInTheDocument();
    expect(within(items[0]).getByRole("button", { name: /^Сколько мы купили молока за год\?/ })).toHaveAttribute("aria-current", "true");
    expect(within(items[1]).getByText("Уточнение")).toBeInTheDocument();
    expect(within(items[1]).getByText("boris.with.a.rather.long.address@example.com")).toBeInTheDocument();
    expect(within(items[2]).getByText("участник")).toBeInTheDocument();

    fireEvent.click(within(items[1]).getByRole("button", { name: /^Уточнениеа по магазинам\?|^а по магазинам\?/ }));
    expect(props.onOpenHistory).toHaveBeenCalledWith("history-biedronka-shops");

    fireEvent.click(screen.getByRole("button", { name: "Удалить вопрос: а по магазинам?" }));
    expect(props.onDeleteHistory).not.toHaveBeenCalled();
    expect(screen.getByText("Удалить этот вопрос из истории?")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Отмена" }));
    expect(screen.queryByText("Удалить этот вопрос из истории?")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Удалить вопрос: а по магазинам?" }));
    fireEvent.click(within(screen.getByRole("group", { name: "Удалить этот вопрос из истории?" })).getByRole("button", { name: "Удалить" }));
    expect(props.onDeleteHistory).toHaveBeenCalledWith("history-biedronka-shops");
    expect(screen.getByRole("heading", { level: 2, name: "Предыдущие вопросы" })).toHaveFocus();

    fireEvent.click(screen.getByRole("button", { name: "Показать ещё" }));
    expect(props.onShowMoreHistory).toHaveBeenCalled();
  });

  it("hides the history section while it is empty", () => {
    renderView({ history: [], onOpenHistory: vi.fn(), onDeleteHistory: vi.fn() });
    expect(screen.queryByRole("region", { name: "Предыдущие вопросы" })).not.toBeInTheDocument();
  });

  it("marks a saved answer and offers «Обновить» and «Спросить заново»", () => {
    const onRefreshSaved = vi.fn();
    const onAskAgain = vi.fn();
    const saved = {
      entryId: "history-milk", question: "Сколько мы купили молока за год?", answeredAt: "2026-09-26T18:40:00Z",
      origin: "history" as const, isFollowUp: false
    };
    const { unmount } = renderView({ answer: milkQuantityAnswer, saved, onRefreshSaved, onAskAgain });
    expect(screen.getByText(/Сохранённый ответ от 26 сент/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Обновить" }));
    expect(onRefreshSaved).toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Спросить заново" }));
    expect(onAskAgain).toHaveBeenCalled();
    unmount();

    // A follow-up depends on the answer it refined, so only «Обновить» is offered; a reused answer is fresh already.
    const followUp = renderView({ answer: biedronkaSpendAnswer, saved: { ...saved, isFollowUp: true }, onRefreshSaved, onAskAgain });
    expect(screen.queryByRole("button", { name: "Спросить заново" })).not.toBeInTheDocument();
    followUp.unmount();
    renderView({ answer: milkQuantityAnswer, saved: { ...saved, origin: "reused" }, onRefreshSaved, onAskAgain, enabled: true });
    expect(screen.getByText(/Этот вопрос уже задавали/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Обновить" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Спросить заново" })).toBeInTheDocument();
  });

  it("renders in English", () => {
    render(
      <AppProviders locale="en">
        <MemoryRouter>
          <AnalyticsView {...renderViewDefaults()} locale="en-GB" answer={pastaNoAnswer} />
        </MemoryRouter>
      </AppProviders>
    );
    expect(screen.getByRole("heading", { level: 1, name: "Ask about your purchases" })).toBeInTheDocument();
    expect(screen.getByText("“makaron pelnoziarn” in the name")).toBeInTheDocument();
    expect(screen.getByText("All history")).toBeInTheDocument();
  });
});

function renderViewDefaults(): AnalyticsViewProps {
  return {
    enabled: true, question: "", onQuestionChange: vi.fn(), onAsk: vi.fn(), asking: false, rerunning: false, problem: null,
    answer: null, followUp: null, onClearFollowUp: vi.fn(), onPlanChange: vi.fn(), categories: sampleCategories, merchants: [], locale: "ru-RU"
  };
}

describe("AnalyticsPage", () => {
  it("asks, then sends the previous plan with a follow-up, and re-runs chips without the model", async () => {
    vi.mocked(getAnalyticsContext).mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: ["Biedronka"], brands: [] });
    vi.mocked(listCategories).mockResolvedValue(sampleCategories);
    vi.mocked(listTags).mockResolvedValue(sampleAnalyticsTags.map((tag) => ({ ...tag, productCount: 1 })));
    vi.mocked(askAnalytics).mockResolvedValue(asked(milkQuantityAnswer));
    vi.mocked(runAnalyticsPlan).mockResolvedValue(biedronkaSpendAnswer);

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    fireEvent.change(screen.getByLabelText("Ваш вопрос"), { target: { value: "Сколько мы купили молока за год?" } });
    fireEvent.click(screen.getByRole("button", { name: "Спросить" }));

    expect(await screen.findByRole("heading", { level: 2, name: milkQuantityAnswer.conclusion })).toBeInTheDocument();
    expect(askAnalytics).toHaveBeenLastCalledWith("Сколько мы купили молока за год?", null, "ru", false);
    // The household's tags reach the plan editor.
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    expect(within(screen.getByLabelText("Тег")).getByRole("option", { name: "Для ребёнка" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Уточните вопрос"), { target: { value: "а в Biedronce?" } });
    fireEvent.click(screen.getByRole("button", { name: "Спросить" }));
    await waitFor(() => expect(askAnalytics).toHaveBeenLastCalledWith("а в Biedronce?", milkQuantityAnswer.plan, "ru", false));

    fireEvent.click(await screen.findByRole("button", { name: "Убрать фильтр: «mleko» в названии" }));
    await waitFor(() => expect(runAnalyticsPlan).toHaveBeenCalledWith({ ...milkQuantityAnswer.plan, nameTerms: [] }, "ru"));
    expect(await screen.findByRole("heading", { level: 2, name: biedronkaSpendAnswer.conclusion })).toBeInTheDocument();
    expect(askAnalytics).toHaveBeenCalledTimes(2);
  });

  it("opens a saved question without asking, refreshes it without the model and deletes it", async () => {
    vi.mocked(getAnalyticsContext).mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: ["Biedronka"], brands: [] });
    vi.mocked(listCategories).mockResolvedValue(sampleCategories);
    vi.mocked(listTags).mockResolvedValue([]);
    vi.mocked(listAnalyticsHistory).mockResolvedValue({ items: sampleAnalyticsHistory, hasMore: false });
    vi.mocked(getAnalyticsHistoryEntry).mockResolvedValue({ entry: sampleAnalyticsHistory[0], answer: milkQuantityAnswer });
    vi.mocked(refreshAnalyticsHistoryEntry).mockResolvedValue({
      entry: { ...sampleAnalyticsHistory[0], answeredAt: "2026-10-08T09:00:00Z" },
      answer: { ...milkQuantityAnswer, conclusion: "Куплено 23 шт.: 19 позиций в 15 чеках." }
    });
    vi.mocked(deleteAnalyticsHistoryEntry).mockResolvedValue(undefined);
    vi.mocked(askAnalytics).mockClear();
    vi.mocked(askAnalytics).mockResolvedValue(asked(milkQuantityAnswer, "history-milk"));

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    const section = await screen.findByRole("region", { name: "Предыдущие вопросы" });
    fireEvent.click(within(section).getByRole("button", { name: /^Сколько мы купили молока за год\?/ }));
    expect(await screen.findByRole("heading", { level: 2, name: milkQuantityAnswer.conclusion })).toBeInTheDocument();
    expect(getAnalyticsHistoryEntry).toHaveBeenCalledWith("history-milk");
    expect(screen.getByText(/Сохранённый ответ от/)).toBeInTheDocument();
    expect(askAnalytics).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Обновить" }));
    expect(await screen.findByRole("heading", { level: 2, name: "Куплено 23 шт.: 19 позиций в 15 чеках." })).toBeInTheDocument();
    expect(refreshAnalyticsHistoryEntry).toHaveBeenCalledWith("history-milk", "ru");
    expect(askAnalytics).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Спросить заново" }));
    await waitFor(() => expect(askAnalytics).toHaveBeenCalledWith("Сколько мы купили молока за год?", null, "ru", true));

    fireEvent.click(screen.getByRole("button", { name: "Удалить вопрос: Сколько мы купили молока за год?" }));
    fireEvent.click(within(screen.getByRole("group", { name: "Удалить этот вопрос из истории?" })).getByRole("button", { name: "Удалить" }));
    await waitFor(() => expect(deleteAnalyticsHistoryEntry).toHaveBeenCalledWith("history-milk"));
    await waitFor(() => expect(listAnalyticsHistory).toHaveBeenCalledTimes(4));
  });

  it("shows only the answer of the most recently opened question when an earlier one arrives last", async () => {
    vi.mocked(getAnalyticsContext).mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: [], brands: [] });
    vi.mocked(listCategories).mockResolvedValue([]);
    vi.mocked(listTags).mockResolvedValue([]);
    vi.mocked(listAnalyticsHistory).mockResolvedValue({ items: sampleAnalyticsHistory, hasMore: false });
    let finishMilk: (value: Awaited<ReturnType<typeof getAnalyticsHistoryEntry>>) => void = () => undefined;
    vi.mocked(getAnalyticsHistoryEntry).mockReset().mockImplementation((id) => id === "history-milk"
      ? new Promise((resolve) => { finishMilk = resolve; })
      : Promise.resolve({ entry: sampleAnalyticsHistory[2], answer: coffeePriceAnswer }));

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    const section = await screen.findByRole("region", { name: "Предыдущие вопросы" });
    fireEvent.click(within(section).getByRole("button", { name: /^Сколько мы купили молока за год\?/ }));
    fireEvent.click(within(section).getByRole("button", { name: /^Сколько в среднем стоил кофе/ }));
    expect(await screen.findByRole("heading", { level: 2, name: coffeePriceAnswer.conclusion })).toBeInTheDocument();

    finishMilk({ entry: sampleAnalyticsHistory[0], answer: milkQuantityAnswer });
    await waitFor(() => expect(within(section).getByRole("button", { name: /^Сколько мы купили молока за год\?/ })).not.toBeDisabled());
    expect(screen.getByRole("heading", { level: 2, name: coffeePriceAnswer.conclusion })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { level: 2, name: milkQuantityAnswer.conclusion })).not.toBeInTheDocument();
    expect(within(section).getByRole("button", { name: /^Сколько в среднем стоил кофе/ })).toHaveAttribute("aria-current", "true");
  });

  it("explains an outdated saved question and forgets one someone else already deleted", async () => {
    vi.mocked(getAnalyticsContext).mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: [], brands: [] });
    vi.mocked(listCategories).mockResolvedValue([]);
    vi.mocked(listTags).mockResolvedValue([]);
    vi.mocked(listAnalyticsHistory).mockResolvedValue({ items: sampleAnalyticsHistory, hasMore: false });
    vi.mocked(getAnalyticsHistoryEntry).mockReset().mockImplementation((id) => Promise.resolve(id === "history-coffee"
      ? { entry: sampleAnalyticsHistory[2], answer: null }
      : { entry: sampleAnalyticsHistory[0], answer: milkQuantityAnswer }));
    vi.mocked(refreshAnalyticsHistoryEntry).mockReset()
      .mockRejectedValue(new ApiError("Outdated", 422, null, null, "historyEntryOutdated"));
    vi.mocked(deleteAnalyticsHistoryEntry).mockReset().mockRejectedValue(new ApiError("Gone", 404, null, null, "historyEntryNotFound"));

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    const section = await screen.findByRole("region", { name: "Предыдущие вопросы" });
    fireEvent.click(within(section).getByRole("button", { name: /^Сколько в среднем стоил кофе/ }));
    expect(await screen.findByText(/сохранила старая версия приложения/)).toBeInTheDocument();
    expect(refreshAnalyticsHistoryEntry).toHaveBeenCalledWith("history-coffee", "ru");

    const milk = () => within(section).getByRole("button", { name: /^Сколько мы купили молока за год\?/ });
    fireEvent.click(milk());
    expect(await screen.findByRole("heading", { level: 2, name: milkQuantityAnswer.conclusion })).toBeInTheDocument();
    expect(milk()).toHaveAttribute("aria-current", "true");

    fireEvent.click(screen.getByRole("button", { name: "Удалить вопрос: Сколько мы купили молока за год?" }));
    fireEvent.click(within(screen.getByRole("group", { name: "Удалить этот вопрос из истории?" })).getByRole("button", { name: "Удалить" }));
    await waitFor(() => expect(milk()).not.toHaveAttribute("aria-current"));
    expect(screen.queryByText(/Сохранённый ответ от/)).not.toBeInTheDocument();
  });

  it("says when the same question reused its saved answer", async () => {
    vi.mocked(getAnalyticsContext).mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: [], brands: [] });
    vi.mocked(listCategories).mockResolvedValue([]);
    vi.mocked(listTags).mockResolvedValue([]);
    vi.mocked(askAnalytics).mockResolvedValue(asked(biedronkaSpendAnswer, "history-biedronka", true));

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    fireEvent.change(screen.getByLabelText("Ваш вопрос"), { target: { value: "Сколько потратили в Biedronce?" } });
    fireEvent.click(screen.getByRole("button", { name: "Спросить" }));
    expect(await screen.findByText(/Этот вопрос уже задавали/)).toBeInTheDocument();
    expect(screen.getByLabelText("Уточните вопрос")).toHaveValue("");
  });

  it("turns a 429 into a rate-limit notice and a 503 into the disabled state", async () => {
    vi.mocked(getAnalyticsContext).mockResolvedValue({ enabled: true, today: "2026-09-26", merchants: [], brands: [] });
    vi.mocked(listCategories).mockResolvedValue([]);
    vi.mocked(listTags).mockResolvedValue([]);
    vi.mocked(askAnalytics).mockRejectedValueOnce(new ApiError("Too many", 429, 60));

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    fireEvent.click(await screen.findByRole("button", { name: "Сколько мы купили молока за год?" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Слишком много вопросов подряд");

    vi.mocked(askAnalytics).mockRejectedValueOnce(new ApiError("Off", 503, null, null, "analyticsDisabled"));
    fireEvent.change(screen.getByLabelText("Ваш вопрос"), { target: { value: "Сколько?" } });
    fireEvent.click(screen.getByRole("button", { name: "Спросить" }));
    expect(await screen.findByText("Вопросы сейчас выключены на сервере.")).toBeInTheDocument();
    expect(screen.getByLabelText("Ваш вопрос")).toBeDisabled();
  });
});
