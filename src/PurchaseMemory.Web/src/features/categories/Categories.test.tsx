import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { chooseAction, dialog, pick, pickerOptions, row } from "../../test/ionicHelpers";
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
    expect(screen.getByText(/предложено/)).toBeInTheDocument();
  });

  it("shows «Без категории» for none and no marker for confirmed", () => {
    const { rerender } = renderRu(<CategoryChip label={null} />);
    expect(screen.getByText("Без категории")).toBeInTheDocument();
    rerender(<AppProviders locale="ru"><CategoryChip label="Сыр" source="confirmed" /></AppProviders>);
    expect(screen.queryByText(/предложено/)).not.toBeInTheDocument();
  });
});

describe("CategoryPicker", () => {
  it("lists subcategories under their group, translating seeded names", () => {
    const onChange = vi.fn();
    renderRu(<CategoryPicker categories={sampleCategories} value="milk" onChange={onChange} />, "en");

    // The closed row shows the whole path of the choice.
    expect(row("Product category")).toHaveTextContent("Dairy & eggs / Milk");
    fireEvent.click(row("Product category"));
    const picker = dialog("Product category");
    expect(within(picker).getAllByRole("heading", { level: 3 }).map((heading) => heading.textContent)).toEqual([
      "Dairy & eggs",
      "Drinks",
      // The household's own category keeps the name they typed.
      "Товары для дачи, рыбалки и прочих выходных за городом"
    ]);
    // Inside a section the rows are short: the header already names the group.
    expect(within(picker).getByRole("button", { name: /^Milk/ })).toHaveAttribute("aria-current", "true");
    fireEvent.click(within(picker).getByRole("button", { name: "Cancel" }));

    expect(pickerOptions("Product category").slice(0, 4)).toEqual(["No category", "No subcategory", "Milk", "Cheese"]);
    pick("Product category", "No category");
    expect(onChange).toHaveBeenCalledWith(null);
    pick("Product category", "Coffee");
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
    expect(screen.getByText("Категория · предложено")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ categoryId: "coffee", brand: "Lavazza" }));
  });

  it("does not resend a confirmed brand when only the category was suggested", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    renderRu(<ProductClassification {...base} brandSource="confirmed" onSave={onSave} />);
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ categoryId: "coffee" }));
  });

  it("saves a picked category at once and clears an empty brand", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    renderRu(<ProductClassification {...base} categorySource="confirmed" brandSource="confirmed" onSave={onSave} />);

    expect(screen.queryByRole("button", { name: "Подтвердить" })).not.toBeInTheDocument();
    pick("Категория", "Чай");
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ categoryId: "tea" }));

    fireEvent.click(row("Бренд"));
    const alert = dialog("Бренд");
    expect(within(alert).getByLabelText("Например, Łaciate")).toHaveValue("Lavazza");
    fireEvent.change(within(alert).getByLabelText("Например, Łaciate"), { target: { value: "   " } });
    fireEvent.click(within(alert).getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onSave).toHaveBeenLastCalledWith({ brand: null }));
  });

  it("reports a failed save", async () => {
    const onSave = vi.fn().mockRejectedValue(new Error("offline"));
    renderRu(<ProductClassification {...base} onSave={onSave} />);
    fireEvent.click(screen.getByRole("button", { name: "Подтвердить" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Не удалось сохранить");
    expect(row("Категория")).toBeInTheDocument();
  });

  it("shows «Без категории» and no brand when nothing is known", () => {
    renderRu(
      <ProductClassification categories={sampleCategories} categoryId={null} categorySource={null} brand={null}
        brandSource={null} onSave={vi.fn()} />
    );
    expect(row("Категория")).toHaveTextContent("Без категории");
    expect(screen.getByText("Бренд не указан")).toBeInTheDocument();
  });
});

describe("CategoryManager", () => {
  const manager = (props: Partial<React.ComponentProps<typeof CategoryManager>> = {}) => (
    <CategoryManager categories={sampleCategories} onCreate={vi.fn()} onRename={vi.fn()} onDelete={vi.fn()} {...props} />
  );

  it("adds a subcategory under the chosen group", async () => {
    const onCreate = vi.fn().mockResolvedValue(undefined);
    renderRu(manager({ onCreate }));

    fireEvent.change(screen.getByLabelText("Новая категория"), { target: { value: "  Кефир " } });
    pick("Где создать", "Молочное и яйца");
    expect(row("Где создать")).toHaveTextContent("Молочное и яйца");
    fireEvent.click(screen.getByRole("button", { name: "Добавить" }));
    await waitFor(() => expect(onCreate).toHaveBeenCalledWith("Кефир", "dairy"));
  });

  it("renames in an alert and deletes only after confirmation", async () => {
    const onRename = vi.fn().mockResolvedValue(undefined);
    const onDelete = vi.fn().mockResolvedValue(undefined);
    renderRu(manager({ onRename, onDelete }));

    expect(screen.getByRole("list", { name: "Дерево категорий" })).toBeInTheDocument();
    fireEvent.click(row("Сыр"));
    chooseAction("Переименовать");
    fireEvent.change(within(dialog("Переименовать")).getByLabelText("Переименовать"), { target: { value: "Сыры" } });
    fireEvent.click(within(dialog("Переименовать")).getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(onRename).toHaveBeenCalledWith("cheese", "Сыры"));

    fireEvent.click(row("Чай"));
    chooseAction("Удалить");
    expect(onDelete).not.toHaveBeenCalled();
    expect(screen.getByText("Удалить «Чай»?")).toBeInTheDocument();
    chooseAction("Да, удалить");
    await waitFor(() => expect(onDelete).toHaveBeenCalledWith("tea"));
  });

  it("does not offer deleting a group that still has subcategories", () => {
    renderRu(manager());
    fireEvent.click(row("Молочное и яйца"));
    expect(screen.getByText("Сначала удалите подкатегории.")).toBeInTheDocument();
    expect(within(dialog()).queryByRole("button", { name: "Удалить" })).not.toBeInTheDocument();
  });

  it("reports a refused action", async () => {
    const onDelete = vi.fn().mockRejectedValue(new Error("conflict"));
    renderRu(manager({ onDelete }));
    fireEvent.click(row("Чай"));
    chooseAction("Удалить");
    chooseAction("Да, удалить");
    expect(await screen.findByRole("alert")).toHaveTextContent("conflict");
  });
});
