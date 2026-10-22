import {
  Bath,
  Beef,
  Candy,
  Carrot,
  Croissant,
  CupSoda,
  House,
  Milk,
  Package,
  Snowflake,
  SprayCan,
  Tag,
  Wheat,
  type LucideIcon
} from "lucide-react";
import type { ProductCategory } from "../../api/types";

/**
 * The seeded top-level groups (`ProductCategorySeed.cs`), each with its icon;
 * the tint is in styles.css (`.pm-category-tile[data-group=…]`).
 */
const groupIcons: Record<string, LucideIcon> = {
  dairy: Milk,
  meat_fish: Beef,
  produce: Carrot,
  bakery: Croissant,
  grocery: Wheat,
  sweets_snacks: Candy,
  drinks: CupSoda,
  frozen: Snowflake,
  household_chemicals: SprayCan,
  hygiene: Bath,
  home: House,
  other: Package
};

/**
 * The seed's fixed ids of those groups (`ProductCategorySeed.IdFor`): a
 * renamed group loses its `systemKey` but keeps its id, and its icon.
 */
const groupsBySeedNumber: Record<number, string> = {
  1: "dairy", 8: "meat_fish", 13: "produce", 17: "bakery", 20: "grocery", 28: "sweets_snacks",
  33: "drinks", 40: "frozen", 44: "household_chemicals", 48: "hygiene", 52: "home", 55: "other"
};
const seedIdPrefix = "00000000-0000-4000-8000-";

/** The top-level group a category is in (itself for a group); null when it is not in the tree. */
export function topLevelCategory(categories: ProductCategory[], categoryId: string | null | undefined): ProductCategory | null {
  let category = categoryId ? categories.find((entry) => entry.id === categoryId) : undefined;
  // Two levels only, but a stale parent link must not loop.
  for (let depth = 0; category?.parentId && depth < 4; depth += 1) {
    const parent = categories.find((entry) => entry.id === category!.parentId);
    if (!parent) break;
    category = parent;
  }
  return category ?? null;
}

/**
 * Which seeded group's icon and tint a product's category gets: by its group's
 * key, else by the seed id; a household's own group gets the neutral tag.
 */
export function categoryGroupKey(categories: ProductCategory[], categoryId: string | null | undefined): string | null {
  const group = topLevelCategory(categories, categoryId);
  if (!group) return null;
  if (group.systemKey && groupIcons[group.systemKey]) return group.systemKey;
  const id = group.id.toLowerCase();
  if (id.startsWith(seedIdPrefix)) {
    const key = groupsBySeedNumber[Number(id.slice(seedIdPrefix.length))];
    if (key) return key;
  }
  return "custom";
}

/**
 * The 40 px rounded square at the start of a product row: the category's
 * group as a stroke icon on a light tint. Decorative: the row's text names the
 * category. `group` null is «без категории» (grey tag).
 */
export function CategoryTile({ group }: { group: string | null }) {
  const Icon = (group && groupIcons[group]) || Tag;
  return (
    <span slot="start" className="pm-category-tile" data-group={group ?? "none"} aria-hidden="true">
      <Icon size={22} strokeWidth={2} />
    </span>
  );
}
