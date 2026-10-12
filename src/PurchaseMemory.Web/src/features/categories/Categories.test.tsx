import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { CategoryChip } from "./CategoryChip";
import { sampleCategories } from "./categoryFixtures";
import { CategoryManager } from "./CategoryManager";
import { CategoryPicker } from "./CategoryPicker";
import { ProductClassification } from "./ProductClassification";

function renderRu(node: React.ReactNode, locale: "ru" | "en" = "ru") {
  return render(<AppProviders locale={locale}>{node}</AppProviders>);
}

describe("CategoryChip", () => {
  it("says in words that a category is only suggested", () => {
    renderRu(<CategoryChip label="Напитки / Кофе" source="suggested" />);
    expect(screen.getByText("Напитки / Кофе")).toBeInTheDocument();
    expect(screen.getByText("предложено")).toBeInTheDocument();
  });

  it("shows «Без категории» for none and no marker for confirmed", () => {
    const { rerender } = renderRu(<CategoryChip label={null} />);
    expect(screen.getByText("Без категории")).toBeInTheDocument();
    rerender(<AppProviders locale="ru"><CategoryChip label="Сыр" source="confirmed" /></AppProviders>);
    expect(screen.queryByText("предложено")).not.toBeInTheDocument();
  });
});

describe("CategoryPicker", () => {
  it("groups subcategories under their group, translating seeded names", () => {
    const onChange = vi.fn();
    renderRu(<CategoryPicker categories={sampleCategories} value="milk" onChange={onChange} />, "en");

    const select = screen.getByLabelText("Product category") as HTMLSelectElement;
    expect(select.value).toBe("milk");
    const groups = [...select.querySelectorAll("optgroup")];
    expect(groups.map((group) => group.label)).toEqual([
      "Dairy & eggs",
      "Drinks",
      // The household's own category keeps the name they typed.
      "Товары для дачи, рыбалки и прочих выходных за городом"
    ]);
    // Inside a group the options are short — the optgroup already names the
    // group — and only the chosen one keeps its full path for the closed select.
    expect(within(groups[0]).getAllByRole("option").map((option) => option.textContent)).toEqual([
      "No subcategory",
      "Dairy & eggs / Milk",
      "Cheese"
    ]);
    expect(within(groups[0]).getByRole("option", { name: "Cheese" })).toHaveValue("cheese");

    fireEvent.change(select, { target: { value: "" } });
    expect(onChange).toHaveBeenCalledWith(null);
    fireEvent.change(select, { target: { value: "coffee" } });
    expect(onChange).toHaveBeenLastCalledWith("coffee");
  });
});

describe("ProductClassification", () => {
  const base = {
    categories: sampleCategories,
    categoryId: "coffee",
    categorySource: "suggested" as const,
    brand: "Lavazza",
    brandSource: "suggested" as const
  };

  it("confirms exactly the suggested values", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    renderRu(<ProductClassification {...base} onSave={onSave} />);

    expect(screen.getByText("Напитки / Кофе")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ categoryId: "coffee", brand: "Lavazza" }));
  });

  it("does not resend a confirmed brand when only the category was suggested", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    renderRu(<ProductClassification {...base} brandSource="confirmed" onSave={onSave} />);
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ categoryId: "coffee" }));
  });

  it("edits category and brand, clearing an empty brand", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    renderRu(<ProductClassification {...base} categorySource="confirmed" brandSource="confirmed" onSave={onSave} />);

    expect(screen.queryByRole("button", { name: "Подтвердить" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Изменить" }));
    fireEvent.change(screen.getByLabelText("Категория товара"), { target: { value: "tea" } });
    fireEvent.change(screen.getByLabelText("Бренд"), { target: { value: "   " } });
    expect(screen.getByText(/применится ко всем покупкам/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ categoryId: "tea", brand: null }));
    expect(await screen.findByRole("button", { name: "Изменить" })).toBeInTheDocument();
  });

  it("keeps the form and reports a failed save", async () => {
    const onSave = vi.fn().mockRejectedValue(new Error("offline"));
    renderRu(<ProductClassification {...base} onSave={onSave} initiallyEditing />);
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось сохранить");
    expect(screen.getByLabelText("Категория товара")).toBeInTheDocument();
  });

  it("shows «Без категории» and no brand when nothing is known", () => {
    renderRu(
      <ProductClassification categories={sampleCategories} categoryId={null} categorySource={null} brand={null}
        brandSource={null} onSave={vi.fn()} />
    );
    expect(screen.getByText("Без категории")).toBeInTheDocument();
    expect(screen.getByText("Бренд не указан")).toBeInTheDocument();
  });
});

describe("CategoryManager", () => {
  it("adds a subcategory under the chosen group", async () => {
    const onCreate = vi.fn().mockResolvedValue(undefined);
    renderRu(<CategoryManager categories={sampleCategories} onCreate={onCreate} onRename={vi.fn()} onDelete={vi.fn()} defaultOpen />);

    fireEvent.change(screen.getByLabelText("Новая категория"), { target: { value: "  Кефир " } });
    fireEvent.change(screen.getByLabelText("Где создать"), { target: { value: "dairy" } });
    fireEvent.click(screen.getByRole("button", { name: "Добавить" }));
    await waitFor(() => expect(onCreate).toHaveBeenCalledWith("Кефир", "dairy"));
  });

  it("renames inline and deletes only after confirmation", async () => {
    const onRename = vi.fn().mockResolvedValue(undefined);
    const onDelete = vi.fn().mockResolvedValue(undefined);
    renderRu(<CategoryManager categories={sampleCategories} onCreate={vi.fn()} onRename={onRename} onDelete={onDelete} defaultOpen />);

    fireEvent.click(screen.getByRole("button", { name: "Переименовать: Сыр" }));
    fireEvent.change(screen.getByLabelText("Переименовать"), { target: { value: "Сыры" } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onRename).toHaveBeenCalledWith("cheese", "Сыры"));

    fireEvent.click(screen.getByRole("button", { name: "Удалить: Чай" }));
    expect(onDelete).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Да, удалить" }));
    await waitFor(() => expect(onDelete).toHaveBeenCalledWith("tea"));
  });

  it("does not offer deleting a group that still has subcategories", () => {
    renderRu(<CategoryManager categories={sampleCategories} onCreate={vi.fn()} onRename={vi.fn()} onDelete={vi.fn()} defaultOpen />);
    expect(screen.getByRole("button", { name: "Удалить: Молочное и яйца" })).toBeDisabled();
  });

  it("reports a refused action", async () => {
    const onDelete = vi.fn().mockRejectedValue(new Error("conflict"));
    renderRu(<CategoryManager categories={sampleCategories} onCreate={vi.fn()} onRename={vi.fn()} onDelete={onDelete} defaultOpen />);
    fireEvent.click(screen.getByRole("button", { name: "Удалить: Чай" }));
    fireEvent.click(screen.getByRole("button", { name: "Да, удалить" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("conflict");
  });
});
