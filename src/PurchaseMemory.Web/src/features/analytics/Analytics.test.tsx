import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { ApiError, askAnalytics, getAnalyticsContext, listCategories, listTags, runAnalyticsPlan } from "../../api/client";
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
    runAnalyticsPlan: vi.fn()
  };
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
    vi.mocked(askAnalytics).mockResolvedValue(milkQuantityAnswer);
    vi.mocked(runAnalyticsPlan).mockResolvedValue(biedronkaSpendAnswer);

    render(<AppProviders><MemoryRouter><AnalyticsPage /></MemoryRouter></AppProviders>);
    fireEvent.change(screen.getByLabelText("Ваш вопрос"), { target: { value: "Сколько мы купили молока за год?" } });
    fireEvent.click(screen.getByRole("button", { name: "Спросить" }));

    expect(await screen.findByRole("heading", { level: 2, name: milkQuantityAnswer.conclusion })).toBeInTheDocument();
    expect(askAnalytics).toHaveBeenLastCalledWith("Сколько мы купили молока за год?", null, "ru");
    // The household's tags reach the plan editor.
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    expect(within(screen.getByLabelText("Тег")).getByRole("option", { name: "Для ребёнка" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Уточните вопрос"), { target: { value: "а в Biedronce?" } });
    fireEvent.click(screen.getByRole("button", { name: "Спросить" }));
    await waitFor(() => expect(askAnalytics).toHaveBeenLastCalledWith("а в Biedronce?", milkQuantityAnswer.plan, "ru"));

    fireEvent.click(await screen.findByRole("button", { name: "Убрать фильтр: «mleko» в названии" }));
    await waitFor(() => expect(runAnalyticsPlan).toHaveBeenCalledWith({ ...milkQuantityAnswer.plan, nameTerms: [] }, "ru"));
    expect(await screen.findByRole("heading", { level: 2, name: biedronkaSpendAnswer.conclusion })).toBeInTheDocument();
    expect(askAnalytics).toHaveBeenCalledTimes(2);
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
