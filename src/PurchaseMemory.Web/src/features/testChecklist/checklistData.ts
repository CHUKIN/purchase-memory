/**
 * The phone test checklist: structure only. The words live in `i18n.ts`
 * under `testChecklist.groups.<group>.title` and
 * `testChecklist.items.<item>.title` / `.description`.
 * To add an item, add its id here and its two texts in both languages.
 */
export interface ChecklistGroup {
  id: string;
  items: readonly string[];
}

export const checklistGroups: readonly ChecklistGroup[] = [
  { id: "barcodes", items: ["scanHome", "scanProducts", "unknownCode", "productPhoto", "nameFromPhoto"] },
  { id: "receipt", items: ["shootReceipt", "lineHighlight", "totalsDiscounts"] },
  { id: "details", items: ["dislikeWarning", "howOften", "storeLogos", "backfill", "serverWakes"] },
  { id: "comfort", items: ["fits", "tapTargets", "backButton"] }
];

export const checklistItemIds: readonly string[] = checklistGroups.flatMap((group) => group.items);
