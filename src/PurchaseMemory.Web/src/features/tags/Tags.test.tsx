import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "../../api/client";
import { AppProviders } from "../../app/AppProviders";
import { knownTagFilter } from "../filters/categoryFilter";
import { chooseAction, dialog, row } from "../../test/ionicHelpers";
import { TagManager } from "./TagManager";
import { tagKey } from "./tagKey";

const tags = [
  { id: "kid", name: "для ребёнка", productCount: 3 },
  { id: "party", name: "к празднику", productCount: 0 }
];

function renderManager(overrides: Partial<Parameters<typeof TagManager>[0]> = {}) {
  const props = {
    tags,
    onCreate: vi.fn().mockResolvedValue(undefined),
    onRename: vi.fn().mockResolvedValue(undefined),
    onDelete: vi.fn().mockResolvedValue(undefined),
    ...overrides
  };
  render(<AppProviders locale="ru"><TagManager {...props} /></AppProviders>);
  return props;
}

describe("TagManager", () => {
  it("lists tags with counts and creates, renames and deletes them", async () => {
    const props = renderManager();
    const list = screen.getByRole("list", { name: "Список тегов" });
    expect(within(list).getByText("товаров: 3")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Новый тег"), { target: { value: " акция " } });
    fireEvent.click(screen.getByRole("button", { name: "Добавить" }));
    await waitFor(() => expect(props.onCreate).toHaveBeenCalledWith("акция"));

    fireEvent.click(row("для ребёнка"));
    chooseAction("Переименовать");
    fireEvent.change(within(dialog("Переименовать")).getByLabelText("Переименовать"), { target: { value: "детям" } });
    fireEvent.click(within(dialog("Переименовать")).getByRole("button", { name: "Сохранить" }));
    await waitFor(() => expect(props.onRename).toHaveBeenCalledWith("kid", "детям"));

    fireEvent.click(row("к празднику"));
    chooseAction("Удалить");
    expect(screen.getByText("Удалить тег «к празднику»?")).toBeInTheDocument();
    chooseAction("Да, удалить");
    await waitFor(() => expect(props.onDelete).toHaveBeenCalledWith("party"));
  });

  it("says plainly when a name is already taken", async () => {
    renderManager({ onCreate: vi.fn().mockRejectedValue(new ApiError("A tag named «x» already exists.", 409)) });
    fireEvent.change(screen.getByLabelText("Новый тег"), { target: { value: "ДЛЯ РЕБЁНКА" } });
    fireEvent.click(screen.getByRole("button", { name: "Добавить" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Такой тег уже есть");
  });

  it("invites to create the first tag when there are none", () => {
    renderManager({ tags: [] });
    expect(screen.getByText("Тегов пока нет — создайте первый.")).toBeInTheDocument();
  });
});

describe("knownTagFilter", () => {
  it("keeps a known tag, drops an unknown one once the list is loaded", () => {
    expect(knownTagFilter("kid", tags)).toBe("kid");
    expect(knownTagFilter("gone", tags)).toBe("");
    expect(knownTagFilter("gone", undefined)).toBe("gone");
    expect(knownTagFilter("", tags)).toBe("");
  });
});

describe("tagKey", () => {
  it("folds case, Polish letters, punctuation and spaces like the server", () => {
    expect(tagKey("Łódź  trip!")).toBe("lodz trip");
    expect(tagKey("ŻÓŁTE")).toBe(tagKey("zolte"));
    expect(tagKey("Акция")).toBe("акция");
    expect(tagKey("250g")).toBe("250 g");
    expect(tagKey("a1b")).toBe("a 1 b");
    expect(tagKey("1a2")).toBe("1 a 2");
    expect(tagKey("Straße")).toBe("strasse");
  });
});
