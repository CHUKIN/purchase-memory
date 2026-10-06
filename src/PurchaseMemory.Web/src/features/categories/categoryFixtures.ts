import type { ProductCategory } from "../../api/types";

const category = (
  id: string,
  parentId: string | null,
  name: string,
  systemKey: string | null,
  position: number,
  productCount = 0
): ProductCategory => ({ id, parentId, name, systemKey, position, productCount });

/** A slice of the seeded tree plus one household category with a long name. */
export const sampleCategories: ProductCategory[] = [
  category("dairy", null, "Молочное и яйца", "dairy", 0),
  category("milk", "dairy", "Молоко", "milk", 0, 4),
  category("cheese", "dairy", "Сыр", "cheese", 1, 2),
  category("drinks", null, "Напитки", "drinks", 1),
  category("coffee", "drinks", "Кофе", "coffee", 0, 3),
  category("tea", "drinks", "Чай", "tea", 1),
  category("own", null, "Товары для дачи, рыбалки и прочих выходных за городом", null, 2, 1),
  category("own-child", "own", "Удочки, крючки, поплавки и вообще всё для рыбалки", null, 0)
];
