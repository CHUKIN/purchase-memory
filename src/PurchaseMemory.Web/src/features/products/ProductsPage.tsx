import { IonLabel, IonList, IonItem, IonNote } from "@ionic/react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { FolderTree, Hash } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import {
  createCategory,
  createTag,
  deleteCategory,
  deleteTag,
  listCategories,
  listTags,
  renameCategory,
  renameTag
} from "../../api/client";
import { Footnote, Notice, SectionHeader, Sheet } from "../../design-system/components";
import { CategoryManager } from "../categories/CategoryManager";
import { categoriesQueryKey } from "../categories/categoryQueries";
import { TagManager } from "../tags/TagManager";
import { tagsQueryKey } from "../tags/tagQueries";
import { ProductCatalogSection } from "./ProductCatalogSection";

/** The «Товары» tab: the catalog, plus the rarely used category and tag lists at the bottom. */
export function ProductsPage() {
  return <ProductCatalogSection footer={<ReferenceLists />} />;
}

/**
 * «Категории» and «Теги» are edited rarely, so they live in two rows under
 * the catalog, each opening its own full-screen list.
 */
function ReferenceLists() {
  const { t } = useTranslation();
  const [open, setOpen] = useState<"categories" | "tags" | null>(null);
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });

  return (
    <section aria-labelledby="reference-lists-title">
      <SectionHeader id="reference-lists-title">{t("products.referenceTitle")}</SectionHeader>
      <IonList inset className="pm-after-header">
        <IonItem button detail onClick={() => setOpen("categories")}>
          <FolderTree slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel>{t("categories.sheetTitle")}</IonLabel>
          {categoriesQuery.data && <IonNote slot="end">{categoriesQuery.data.length}</IonNote>}
        </IonItem>
        <IonItem button detail lines="none" onClick={() => setOpen("tags")}>
          <Hash slot="start" size={22} className="pm-row-icon" aria-hidden="true" />
          <IonLabel>{t("tags.title")}</IonLabel>
          {tagsQuery.data && <IonNote slot="end">{tagsQuery.data.length}</IonNote>}
        </IonItem>
      </IonList>
      <Sheet isOpen={open === "categories"} onClose={() => setOpen(null)} title={t("categories.sheetTitle")} variant="full">
        <CategoryManagerSection />
      </Sheet>
      <Sheet isOpen={open === "tags"} onClose={() => setOpen(null)} title={t("tags.title")} variant="full">
        <TagManagerSection />
      </Sheet>
    </section>
  );
}

export function TagManagerSection() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const tagsQuery = useQuery({ queryKey: tagsQueryKey, queryFn: listTags });
  // Renames and deletions change chips and filter results everywhere.
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: tagsQueryKey }),
    queryClient.invalidateQueries({ queryKey: ["products"] }),
    queryClient.invalidateQueries({ queryKey: ["purchases", "feed"] })
  ]);

  if (tagsQuery.isError) return <Notice tone="danger" role="alert"><p>{t("tags.loadError")}</p></Notice>;
  if (!tagsQuery.data) return <Footnote>{t("ui.loading")}</Footnote>;

  return (
    <TagManager
      tags={tagsQuery.data}
      onCreate={(name) => createTag(name).then(refresh)}
      onRename={(tagId, name) => renameTag(tagId, name).then(refresh)}
      onDelete={(tagId) => deleteTag(tagId).then(refresh)}
    />
  );
}

export function CategoryManagerSection() {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const categoriesQuery = useQuery({ queryKey: categoriesQueryKey, queryFn: listCategories });
  // Renames and deletions change labels and product placement everywhere.
  const refresh = () => Promise.all([
    queryClient.invalidateQueries({ queryKey: categoriesQueryKey }),
    queryClient.invalidateQueries({ queryKey: ["products"] })
  ]);

  if (categoriesQuery.isError) return <Notice tone="danger" role="alert"><p>{t("categories.loadError")}</p></Notice>;
  if (!categoriesQuery.data) return <Footnote>{t("ui.loading")}</Footnote>;

  return (
    <CategoryManager
      categories={categoriesQuery.data}
      onCreate={(name, parentId) => createCategory(name, parentId).then(refresh)}
      onRename={(categoryId, name) => renameCategory(categoryId, name).then(refresh)}
      onDelete={(categoryId) => deleteCategory(categoryId).then(refresh)}
    />
  );
}
