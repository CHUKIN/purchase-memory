/**
 * The single structured source of the «Подсказки» section.
 *
 * Structure (ids, groups, deep links) lives here; the words live in
 * `helpText.ts` under the same ids for every locale. A stage that changes
 * user-visible behaviour updates the matching topic in both files.
 */

export const helpGroupIds = ["start", "capture", "history", "catalog", "attention", "analytics", "advanced"] as const;
export type HelpGroupId = (typeof helpGroupIds)[number];

export const helpTopicIds = [
  "sign-in",
  "iphone-app",
  "capture",
  "capture-batch",
  "recognition",
  "review-save",
  "feed",
  "filters",
  "purchase-edit",
  "products",
  "categories",
  "tags",
  "impressions",
  "review-queue",
  "analytics",
  "analytics-history",
  "mcp"
] as const;
export type HelpTopicId = (typeof helpTopicIds)[number];

/** Keys of `help.links.*`: the label of a deep link into the app. */
export type HelpLinkId =
  | "home"
  | "capture"
  | "purchases"
  | "needsAttentionFilter"
  | "products"
  | "uncategorizedProducts"
  | "reviewQueue"
  | "analytics";

export interface HelpDeepLink {
  id: HelpLinkId;
  to: string;
}

export interface HelpTopic {
  id: HelpTopicId;
  group: HelpGroupId;
  links: HelpDeepLink[];
}

const link = (id: HelpLinkId, to: string): HelpDeepLink => ({ id, to });

export const helpTopics: HelpTopic[] = [
  { id: "sign-in", group: "start", links: [link("home", "/")] },
  { id: "iphone-app", group: "start", links: [link("home", "/")] },
  { id: "capture", group: "capture", links: [link("capture", "/purchases/new")] },
  { id: "capture-batch", group: "capture", links: [link("capture", "/purchases/new"), link("home", "/")] },
  { id: "recognition", group: "capture", links: [link("home", "/"), link("reviewQueue", "/needs-attention")] },
  { id: "review-save", group: "capture", links: [link("capture", "/purchases/new")] },
  { id: "feed", group: "history", links: [link("purchases", "/purchases"), link("needsAttentionFilter", "/purchases?status=needsAttention")] },
  { id: "filters", group: "history", links: [link("purchases", "/purchases"), link("products", "/products")] },
  { id: "purchase-edit", group: "history", links: [link("purchases", "/purchases"), link("reviewQueue", "/needs-attention")] },
  { id: "products", group: "catalog", links: [link("products", "/products")] },
  { id: "categories", group: "catalog", links: [link("products", "/products"), link("uncategorizedProducts", "/products?category=none"), link("reviewQueue", "/needs-attention")] },
  { id: "tags", group: "catalog", links: [link("products", "/products"), link("purchases", "/purchases"), link("analytics", "/analytics"), link("reviewQueue", "/needs-attention")] },
  { id: "impressions", group: "catalog", links: [link("home", "/"), link("products", "/products")] },
  { id: "review-queue", group: "attention", links: [link("reviewQueue", "/needs-attention")] },
  { id: "analytics", group: "analytics", links: [link("analytics", "/analytics")] },
  { id: "analytics-history", group: "analytics", links: [link("analytics", "/analytics")] },
  { id: "mcp", group: "advanced", links: [] }
];

export const helpPath = (topic?: HelpTopicId) => topic ? `/help#${topic}` : "/help";

export function isHelpTopicId(value: string): value is HelpTopicId {
  return (helpTopicIds as readonly string[]).includes(value);
}
