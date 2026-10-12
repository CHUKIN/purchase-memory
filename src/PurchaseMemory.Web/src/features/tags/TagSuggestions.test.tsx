import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "../../api/client";
import type { ProductTag, TagSuggestionsResponse, TagSummary } from "../../api/types";
import { AppProviders } from "../../app/AppProviders";
import { ProductTags } from "./ProductTags";

const allTags: TagSummary[] = [
  { id: "kid", name: "для ребёнка", productCount: 3 },
  { id: "promo", name: "акция", productCount: 5 },
  { id: "vegan", name: "веган", productCount: 1 }
];

const answer: TagSuggestionsResponse = {
  productId: "p1",
  offeredTagCount: 2,
  suggestions: [
    { tagId: "kid", name: "для ребёнка", reason: "детский сок" },
    { tagId: "vegan", name: "веган", reason: "растительный продукт" }
  ]
};

function renderTags(overrides: Partial<Parameters<typeof ProductTags>[0]> = {}, locale: "ru" | "en" = "ru") {
  const props = {
    tags: [allTags[1]],
    allTags,
    onSave: vi.fn().mockResolvedValue(undefined),
    onCreate: vi.fn(),
    onSuggest: vi.fn().mockResolvedValue(answer),
    ...overrides
  };
  const view = render(<AppProviders locale={locale}><MemoryRouter><ProductTags {...props} /></MemoryRouter></AppProviders>);
  return { props, view };
}

describe("TagSuggestions", () => {
  it("shows suggestions with reasons and applies nothing until accepted", async () => {
    const { props } = renderTags();
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));

    const list = await screen.findByRole("list", { name: "Предложенные теги" });
    expect(within(list).getByText("для ребёнка")).toBeInTheDocument();
    expect(within(list).getByText("растительный продукт")).toBeInTheDocument();
    expect(screen.getByText(/Ничего не добавится, пока вы не нажмёте «Добавить»/)).toBeInTheDocument();
    expect(props.onSuggest).toHaveBeenCalledTimes(1);
    expect(props.onSave).not.toHaveBeenCalled();

    expect(screen.getByText("Найдены 2 подсказки.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Добавить тег «веган»" }));
    // The product's own tags stay; the accepted one is added to them.
    await waitFor(() => expect(props.onSave).toHaveBeenCalledWith(["promo", "vegan"]));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Добавить тег «веган»" })).not.toBeInTheDocument());
    // Focus moves to the remaining suggestion instead of falling to the page.
    expect(screen.getByRole("button", { name: "Добавить тег «для ребёнка»" })).toHaveFocus();
    expect(screen.getByText("Найдена 1 подсказка.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Скрыть подсказку «для ребёнка»" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Предложить теги" })).toHaveFocus());
  });

  it("accepts on top of the freshest tags, not the ones from the last render", async () => {
    const { props } = renderTags({ readCurrentTagIds: () => ["promo", "kid-added-elsewhere"] });
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    fireEvent.click(await screen.findByRole("button", { name: "Добавить тег «веган»" }));
    await waitFor(() => expect(props.onSave).toHaveBeenCalledWith(["promo", "kid-added-elsewhere", "vegan"]));
  });

  it("dismisses a suggestion and adds all the rest at once", async () => {
    const { props } = renderTags({
      onSuggest: vi.fn().mockResolvedValue({
        ...answer,
        suggestions: [...answer.suggestions, { tagId: "extra", name: "к празднику", reason: "" }]
      })
    });
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    fireEvent.click(await screen.findByRole("button", { name: "Скрыть подсказку «для ребёнка»" }));
    expect(screen.queryByText("детский сок")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Добавить все" }));
    await waitFor(() => expect(props.onSave).toHaveBeenCalledWith(["promo", "vegan", "extra"]));
  });

  it("keeps focus on the section after the last possible tag is accepted", async () => {
    // The product ends up with every household tag, so the suggestions block disappears.
    function Stateful() {
      const [tags, setTags] = useState<ProductTag[]>([allTags[1]]);
      return (
        <ProductTags
          tags={tags}
          allTags={allTags}
          onSave={async (ids) => setTags(allTags.filter((tag) => ids.includes(tag.id)))}
          onCreate={vi.fn()}
          onSuggest={vi.fn().mockResolvedValue(answer)}
        />
      );
    }
    render(<AppProviders locale="ru"><MemoryRouter><Stateful /></MemoryRouter></AppProviders>);
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    fireEvent.click(await screen.findByRole("button", { name: "Добавить все" }));

    await waitFor(() => expect(screen.queryByRole("button", { name: "Предложить ещё раз" })).not.toBeInTheDocument());
    expect(screen.getByRole("heading", { name: "Теги" })).toHaveFocus();
  });

  it("moves focus to «Предложить теги» when the last shown suggestion is accepted", async () => {
    const { props } = renderTags({
      allTags: [...allTags, { id: "extra", name: "к празднику", productCount: 0 }],
      onSuggest: vi.fn().mockResolvedValue({ ...answer, suggestions: [answer.suggestions[1]] })
    });
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    fireEvent.click(await screen.findByRole("button", { name: "Добавить тег «веган»" }));
    await waitFor(() => expect(props.onSave).toHaveBeenCalledWith(["promo", "vegan"]));
    await waitFor(() => expect(screen.getByRole("button", { name: /Предложить/ })).toHaveFocus());
  });

  it("says when nothing fits", async () => {
    renderTags({ onSuggest: vi.fn().mockResolvedValue({ ...answer, suggestions: [] }) });
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    expect(await screen.findByText("Подходящих тегов не нашлось.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Предложить ещё раз" })).toBeInTheDocument();
  });

  it("explains unavailable and rate-limited answers without breaking the section", async () => {
    const onSuggest = vi.fn()
      .mockRejectedValueOnce(new ApiError("unavailable", 503, null, null, "tagSuggestionsUnavailable"))
      .mockRejectedValueOnce(new ApiError("too many", 429, 150));
    renderTags({ onSuggest });

    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    expect(await screen.findByText("Подсказки тегов сейчас недоступны. Теги можно выбрать вручную.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Изменить теги" })).toBeEnabled();

    fireEvent.click(screen.getByRole("button", { name: "Предложить ещё раз" }));
    expect(await screen.findByText("Слишком много подсказок подряд. Попробуйте через 3 мин.")).toBeInTheDocument();
  });

  it("reports a failed accept and keeps the suggestion", async () => {
    renderTags({ onSave: vi.fn().mockRejectedValue(new ApiError("bad", 400)) });
    fireEvent.click(screen.getByRole("button", { name: "Предложить теги" }));
    fireEvent.click(await screen.findByRole("button", { name: "Добавить тег «веган»" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось добавить тег.");
    expect(screen.getByRole("button", { name: "Добавить тег «веган»" })).toBeInTheDocument();
  });

  it("is not offered when every household tag is already on the product or without the callback", () => {
    const { view } = renderTags({ tags: allTags });
    expect(screen.queryByRole("button", { name: "Предложить теги" })).not.toBeInTheDocument();
    view.unmount();

    renderTags({ onSuggest: undefined });
    expect(screen.queryByRole("button", { name: "Предложить теги" })).not.toBeInTheDocument();
  });

  it("speaks English", async () => {
    renderTags({}, "en");
    fireEvent.click(screen.getByRole("button", { name: "Suggest tags" }));
    expect(await screen.findByRole("button", { name: "Add tag “веган”" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Dismiss suggestion “для ребёнка”" })).toBeInTheDocument();
  });
});
