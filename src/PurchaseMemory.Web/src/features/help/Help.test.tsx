import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it } from "vitest";
import { AppProviders } from "../../app/AppProviders";
import { resources } from "../../i18n/i18n";
import { HelpPage } from "./HelpPage";
import { HelpWelcomeCard, helpWelcomeStorageKey } from "./HelpWelcomeCard";
import { helpEn, helpRu } from "./helpText";
import { helpTopicIds, helpTopics, isHelpTopicId } from "./topics";

// Every source file that might place a contextual «Как это работает» link.
const sources = import.meta.glob("../../**/*.tsx", { query: "?raw", import: "default", eager: true }) as Record<string, string>;

function keyPaths(value: unknown, prefix = ""): string[] {
  if (Array.isArray(value)) return [`${prefix}[]`];
  if (value && typeof value === "object") {
    return Object.entries(value).flatMap(([key, child]) => keyPaths(child, prefix ? `${prefix}.${key}` : key));
  }
  return [prefix];
}

describe("help content", () => {
  it("links from screens only to topics that exist", () => {
    const referenced = new Set<string>();
    for (const [path, text] of Object.entries(sources)) {
      if (path.includes(".test.") || path.includes(".stories.")) continue;
      for (const tag of text.matchAll(/<HelpLink\b[^>]*\/>/g)) {
        for (const literal of tag[0].matchAll(/"([^"]+)"/g)) referenced.add(literal[1]);
      }
    }
    expect(referenced.size).toBeGreaterThanOrEqual(8);
    for (const id of referenced) expect(isHelpTopicId(id), `unknown help topic "${id}"`).toBe(true);
  });

  it("has the same keys in Russian and English", () => {
    expect(keyPaths(helpRu).sort()).toEqual(keyPaths(helpEn).sort());
    expect(keyPaths(resources.ru.translation.help).sort()).toEqual(keyPaths(resources.en.translation.help).sort());
  });

  it("describes every topic in both languages and uses unique ids", () => {
    expect(new Set(helpTopics.map((topic) => topic.id)).size).toBe(helpTopicIds.length);
    for (const locale of [helpRu, helpEn]) {
      for (const id of helpTopicIds) {
        const topic = locale.topics[id];
        expect(topic.title.trim()).not.toBe("");
        expect(topic.steps.length + topic.see.length).toBeGreaterThan(0);
        expect(topic.steps.length).toBe(helpRu.topics[id].steps.length);
      }
    }
  });
});

function renderHelp(initial = "/help") {
  return render(
    <AppProviders>
      <MemoryRouter initialEntries={[initial]}>
        <Routes><Route path="/help" element={<HelpPage />} /></Routes>
      </MemoryRouter>
    </AppProviders>
  );
}

describe("HelpPage", () => {
  it("shows every topic with a table of contents", () => {
    renderHelp();
    expect(screen.getByRole("heading", { level: 1, name: "Подсказки" })).toBeInTheDocument();
    const contents = screen.getByRole("navigation", { name: "Содержание подсказок" });
    expect(contents.querySelectorAll("a")).toHaveLength(helpTopicIds.length);
    expect(screen.getAllByRole("article")).toHaveLength(helpTopicIds.length);
  });

  it("filters topics by a search word, ignoring Polish letters and case", () => {
    renderHelp();
    fireEvent.change(screen.getByLabelText("Найти подсказку"), { target: { value: "ŁACIATE" } });
    expect(screen.getAllByRole("article").map((article) => article.id)).toEqual(["filters"]);
    fireEvent.change(screen.getByLabelText("Найти подсказку"), { target: { value: "zzzz" } });
    expect(screen.getByRole("status")).toHaveTextContent("Ничего не нашлось");
    fireEvent.click(screen.getByRole("button", { name: "Показать все темы" }));
    expect(screen.getAllByRole("article")).toHaveLength(helpTopicIds.length);
  });

  it("opens the topic named in the address", async () => {
    renderHelp("/help#analytics");
    const heading = screen.getByRole("heading", { level: 3, name: "Аналитика: вопросы о покупках" });
    await new Promise((resolve) => requestAnimationFrame(resolve));
    expect(heading).toHaveFocus();
  });
});

describe("HelpWelcomeCard", () => {
  it("links to help and stays hidden once dismissed", () => {
    const { unmount } = render(<AppProviders><MemoryRouter><HelpWelcomeCard /></MemoryRouter></AppProviders>);
    expect(screen.getByRole("link", { name: "Открыть подсказки" })).toHaveAttribute("href", "/help");
    fireEvent.click(screen.getByRole("button", { name: "Скрыть" }));
    expect(screen.queryByText("Первый раз здесь?")).not.toBeInTheDocument();
    expect(window.localStorage.getItem(helpWelcomeStorageKey)).toBe("1");
    unmount();
    render(<AppProviders><MemoryRouter><HelpWelcomeCard /></MemoryRouter></AppProviders>);
    expect(screen.queryByText("Первый раз здесь?")).not.toBeInTheDocument();
  });
});
