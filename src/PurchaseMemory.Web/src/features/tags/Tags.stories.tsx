import type { Meta, StoryObj } from "@storybook/react-vite";
import { MemoryRouter } from "react-router";
import { fn } from "storybook/test";
import type { TagSuggestionsResponse, TagSummary } from "../../api/types";
import { ProductTags } from "./ProductTags";
import { TagManager } from "./TagManager";
import { TagSuggestions, type SuggestionState } from "./TagSuggestions";

const householdTags: TagSummary[] = [
  { id: "kid", name: "для ребёнка", productCount: 7 },
  { id: "party", name: "к празднику", productCount: 3 },
  { id: "promo", name: "акция", productCount: 12 },
  { id: "vegan", name: "веган", productCount: 2 },
  { id: "long", name: "очень длинное название тега на сорок зн", productCount: 1 }
];

const meta = {
  title: "Purchase Memory/Tags/ProductTags",
  component: ProductTags,
  parameters: { layout: "padded" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>],
  args: {
    tags: [householdTags[0], householdTags[2]],
    allTags: householdTags,
    onSave: fn(async () => undefined),
    onCreate: fn(async (name: string) => ({ id: `new-${name}`, name }))
  }
} satisfies Meta<typeof ProductTags>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WithTags: Story = {};
export const NoTags: Story = { args: { tags: [] } };
export const Editing: Story = { args: { initiallyEditing: true } };
export const EditingFirstTag: Story = { args: { tags: [], allTags: [], initiallyEditing: true } };
export const EditingMobileEn: Story = {
  args: { initiallyEditing: true },
  globals: { viewport: { value: "phone", isRotated: false }, locale: "en" }
};

/** The manager lives at the bottom of «Товары», next to the category manager. */
export const Manager: StoryObj<typeof TagManager> = {
  render: () => (
    <TagManager
      tags={householdTags}
      onCreate={fn(async () => undefined)}
      onRename={fn(async () => undefined)}
      onDelete={fn(async () => undefined)}
    />
  )
};

export const ManagerMobile: StoryObj<typeof TagManager> = {
  ...Manager,
  globals: { viewport: { value: "phone", isRotated: false } }
};

const suggestionAnswer: TagSuggestionsResponse = {
  productId: "juice",
  offeredTagCount: 4,
  suggestions: [
    { tagId: "kid", name: "для ребёнка", reason: "детский яблочный сок в маленькой упаковке" },
    { tagId: "vegan", name: "веган", reason: "растительный продукт" },
    { tagId: "long", name: "очень длинное название тега на сорок зн", reason: "проверка длинного названия на узком экране" }
  ]
};

/** «Предложить теги» on the product card: press it to see the hints (mocked, no AI call). */
export const WithSuggestions: Story = {
  args: {
    tags: [householdTags[2]],
    onSuggest: fn(async () => suggestionAnswer)
  }
};

export const WithSuggestionsMobileEn: Story = {
  ...WithSuggestions,
  globals: { viewport: { value: "phone", isRotated: false }, locale: "en" }
};

function suggestionStory(state: SuggestionState, mobile = false): StoryObj<typeof TagSuggestions> {
  return {
    render: () => (
      <TagSuggestions
        initialState={state}
        currentTagIds={["promo"]}
        onSuggest={fn(async () => suggestionAnswer)}
        onAccept={fn(async () => undefined)}
      />
    ),
    ...(mobile ? { globals: { viewport: { value: "phone", isRotated: false } } } : {})
  };
}

export const SuggestionsShown = suggestionStory({ kind: "shown", suggestions: suggestionAnswer.suggestions });
export const SuggestionsShownMobile = suggestionStory({ kind: "shown", suggestions: suggestionAnswer.suggestions }, true);
export const SuggestionsLoading = suggestionStory({ kind: "loading" });
export const SuggestionsEmpty = suggestionStory({ kind: "empty" });
export const SuggestionsUnavailable = suggestionStory({ kind: "unavailable" });
export const SuggestionsRateLimited = suggestionStory({ kind: "rateLimited", retryAfterSeconds: 150 }, true);
