import { IonButton, IonItem, IonLabel, IonList, IonNote, IonSearchbar } from "@ionic/react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { Receipt } from "lucide-react";
import { useState } from "react";
import { MemoryRouter } from "react-router";
import {
  ActionButton,
  DateField,
  DecimalInput,
  EmptyState,
  FilterButton,
  Footnote,
  ItemLink,
  LoadingList,
  Notice,
  Page,
  PrimaryButton,
  SectionHeader,
  SelectRow,
  Sheet,
  StatusText
} from ".";

/*
 * The app's own building blocks on top of Ionic (iOS mode). Every screen is
 * made of these: a Page with a large title, inset lists with section
 * headers and footnotes, picker and date rows, sheets.
 */
const meta = {
  title: "Components/Kit",
  parameters: { layout: "fullscreen" },
  decorators: [(Story) => <MemoryRouter><Story /></MemoryRouter>]
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

const phone = { viewport: { value: "phone", isRotated: false } };

function ScreenDemo() {
  const [query, setQuery] = useState("");
  const [sheet, setSheet] = useState(false);
  const [from, setFrom] = useState("2026-08-01");
  const [to, setTo] = useState("");
  const [category, setCategory] = useState("");
  const [total, setTotal] = useState<number | null>(63.12);
  return (
    <Page
      title="Покупки"
      end={<FilterButton count={from || to ? 1 : 0} onClick={() => setSheet(true)} />}
      search={<IonSearchbar value={query} placeholder="Магазин или название позиции" onIonInput={(event) => setQuery(event.detail.value ?? "")} />}
    >
      <PrimaryButton>Сфотографировать чек</PrimaryButton>
      <SectionHeader variant="title" action={<a href="#all">Смотреть все</a>}>Последние покупки</SectionHeader>
      <SectionHeader>Лента покупок · 2 покупки</SectionHeader>
      <IonList inset className="pm-after-header">
        <ItemLink to="/purchases/a">
          <IonLabel className="pm-wrap">
            <h2>Biedronka Jeronimo Martins Polska ul. Marszałkowska 104/122</h2>
            <p>24 авг. 2026 г., 19:12 · 12 позиций</p>
            <p><StatusText status="verified" /></p>
          </IonLabel>
          <IonNote slot="end" className="pm-amount">1 284,32 PLN</IonNote>
        </ItemLink>
        <ItemLink to="/purchases/b">
          <IonLabel className="pm-wrap">
            <h2>Lidl</h2>
            <p>23 авг. 2026 г., 08:40 · 3 позиции</p>
            <p><StatusText status="needsAttention" /></p>
            <p className="pm-warn-text">Сумма строк не сходится с итогом чека</p>
          </IonLabel>
          <IonNote slot="end" className="pm-amount">42,50 PLN</IonNote>
        </ItemLink>
      </IonList>
      <Footnote>Серый текст под списком: короткое пояснение.</Footnote>
      <IonList inset>
        <IonItem>
          <DecimalInput label="Итог чека" value={total} onChange={setTotal} />
          <IonNote slot="end">PLN</IonNote>
        </IonItem>
      </IonList>
      <Footnote tone="success">Строки дают 63,12 PLN — совпадает с итогом чека.</Footnote>
      <Sheet isOpen={sheet} onClose={() => setSheet(false)} title="Фильтры" start={<IonButton onClick={() => { setFrom(""); setTo(""); }}>Сбросить</IonButton>}>
        <SectionHeader level={3}>Период</SectionHeader>
        <IonList inset className="pm-after-header">
          <DateField label="Дата с" value={from} max={to || undefined} onChange={setFrom} />
          <DateField label="Дата по" value={to} min={from || undefined} onChange={setTo} />
        </IonList>
        <IonList inset>
          <SelectRow
            label="Категория"
            valueText={category === "" ? "Все категории" : category === "milk" ? "Молочное и яйца / Молоко" : "Без категории"}
            value={category}
            onChange={setCategory}
            searchable
            groups={[
              { options: [{ value: "", label: "Все категории" }, { value: "none", label: "Без категории" }] },
              { title: "Молочное и яйца", options: [{ value: "dairy", label: "Вся группа" }, { value: "milk", label: "Молоко" }] }
            ]}
          />
        </IonList>
      </Sheet>
    </Page>
  );
}

/** A list screen: large title, search, filter button with a badge, rows, a sheet. Tap «Фильтры». */
export const ListScreen: Story = { render: () => <ScreenDemo />, globals: phone };

/** Waiting, nothing yet, and a problem. */
export const States: Story = {
  globals: phone,
  render: () => (
    <Page title="Состояния" back={{ to: "/", label: "Главная" }}>
      <LoadingList label="Загружаем покупки…" rows={2} />
      <EmptyState icon={<Receipt />} title="Покупок пока нет" description="Сфотографируйте первый чек — сохранённая покупка появится в этой ленте." action={<ActionButton shape="round">Сфотографировать чек</ActionButton>} />
      <Notice tone="danger" role="alert"><p>Не удалось загрузить покупки.</p><IonButton fill="outline" size="small">Повторить</IonButton></Notice>
      <Notice tone="success"><p>Исправления сохранены.</p></Notice>
      <PrimaryButton loading>Сохраняем…</PrimaryButton>
    </Page>
  )
};
