# Purchase Memory — дизайн-бриф и UI-стек

Статус: предложение после продуктового интервью; UI-стек заменён на Ionic
React (iOS) 13.10.2026 — см. раздел «Current UI stack»  
Дата: 3 августа 2026

Полные подтверждённые требования находятся в `ПРОДУКТОВАЯ_СЕССИЯ.md`. Этот
документ переводит их в визуальное направление и план реализации PWA.

## Зафиксированная основа

- Основной клиент — адаптивная PWA на React, TypeScript и Vite.
- Мобильная главная начинается с добавления чека; desktop — со сводки и очереди
  проверки.
- Исходный характер — тёплый семейный архив, но без декоративной перегрузки.
- В первой версии тщательно прорабатывается одна светлая тема.
- Интерфейс переключается между русским и английским; основное название —
  английское.
- Основной кандидат названия: `Purchase Memory / История покупок`.
- Фотографии важны для продуктов и впечатлений, но не должны вытеснять данные.
- Аналитика на desktop плотнее, чем основной мобильный поток.

## Два варианта тёплого направления

### A. Warm Journal — тёплый дневник

- кремовый фон, бумажные поверхности, лесной зелёный и небольшой терракотовый
  акцент;
- выразительный serif только в бренде и крупных заголовках, нейтральный sans в
  данных и формах;
- крупное действие камеры, мягкие карточки, заметные фотографии и человеческие
  формулировки;
- впечатления и история товара визуально важнее технических атрибутов.

Сильная сторона: приложение ощущается личной памятью, а не учётом расходов.
Риск: при избыточных тенях, скруглениях и крупных заголовках desktop станет
слишком рыхлым.

### B. Warm Utility — тёплый инструмент

- та же природная палитра, но почти везде sans-serif;
- тонкие границы вместо выраженных теней, компактнее отступы и радиусы;
- на главной по-прежнему большая камера, но история, фильтры и аналитика
  занимают меньше вертикального места;
- фотографии используются как компактные миниатюры, числа и статусы читаются
  быстрее.

Сильная сторона: легче масштабируется до аналитики, ресторанов и больших
каталогов. Риск: может потерять эмоциональную узнаваемость.

### Рекомендация

Использовать гибрид: `Warm Journal` для главной, capture-потока, карточки товара
и впечатлений; `Warm Utility` для проверки чека, фильтров, таблиц и аналитики.
Это одна дизайн-система, а не два разных интерфейса: меняются только плотность и
визуальная иерархия.

Начальная палитра уже близка к нужной:

| Токен | Черновое значение | Назначение |
|---|---:|---|
| `surface.canvas` | `#F5F1E8` | фон приложения |
| `surface.paper` | `#FFFDF7` | карточки и формы |
| `content.primary` | `#20352D` | основной текст |
| `content.muted` | `#6C776F` | вторичный текст |
| `action.primary` | `#315C4B` | главные действия |
| `accent.warm` | `#D9823A` | редкие акценты |

Значения не являются окончательными: перед фиксацией проверяются контраст,
focus-состояния и реальные фотографии товаров.

## Current UI stack (since 13.10.2026) — Ionic React, iOS mode

The owner chose an iPhone-like UI (approved prototype variant B). The app now
uses **Ionic React 9.0.7 with `setupIonicReact({ mode: "ios" })` on every
device**; Mantine is removed. The sections below about Mantine are history.

- Look: iOS large collapsing titles, searchbar under the title, inset grouped
  lists on `#f2f2f7`, bottom sheets (`IonModal` with breakpoints) for filters,
  editors and pickers, action sheets and alerts for short choices, toasts for
  background news. Brand green `#315c4b` is Ionic `primary`.
- Navigation: standalone `IonTabBar` (Главная / Покупки / Товары / Аналитика)
  with React Router 8 — `@ionic/react-router` supports React Router 6 only, so
  there is no `IonRouterOutlet` and no native page-transition animation.
- Simplicity rules: one primary action per screen; rarely used things behind
  «Ещё» on Главная (help, language, recognition status, sign-out) or in
  «Справочники» at the bottom of Товары (categories, tags).
- Own kit on top of Ionic: `src/design-system/components` — `Page`,
  `SectionHeader`/`Footnote`/`Notice`/`EmptyState`, `ItemLink`/`ButtonLink`
  (real links routed by React Router), `Sheet`, `PickerModal`/`SelectRow`
  (full-screen list with group headers and a checkmark), `DateField`
  (`IonDatetime` in a sheet; replaces the native-date `DateInput`),
  `DecimalInput` (accepts «12,5»), `FilterButton` with a count badge.
- Light only: Ionic's dark palette is not imported, `color-scheme: light`.
- Charts: recharts directly, lazy-loaded with a table copy of the same numbers.
- Tests run against a semantic stand-in for `@ionic/react`
  (`src/test/ionicShim.tsx`); one smoke test loads the real package.
- Storybook: `AppProviders` applies the Ionic setup; parts of screens are shown
  on the grouped background, whole screens (`layout: "fullscreen"`) bring
  their own page. Details and numbers: `РЕЗУЛЬТАТЫ_IONIC.md`.

## Рекомендуемая UI-библиотека (историческое, до 13.10.2026)

### Основной вариант: Mantine с собственной темой

Сохраняем React/TypeScript/Vite и добавляем Mantine только как основу
интерактивных компонентов. Feature-компоненты `PurchaseCard`, `ProductCard`,
`ReceiptIssue` и `AnalyticsAnswer` остаются нашими и получают собственный
характер.

Причины выбора:

- официальная поддержка Vite и готовая совместимость с Vitest и Storybook;
- единая система доступных полей, dialog/drawer, уведомлений и загрузки файлов;
- отдельные пакеты для дат, dropzone и графиков можно подключать только по мере
  необходимости;
- тема централизует цвета, типографику, радиусы, spacing, focus и reduced
  motion;
- Styles API и CSS Modules позволяют уйти от стандартного вида Mantine;
- `@mantine/charts` использует Recharts и включает клавиатурный слой
  доступности.

Официальные материалы:

- [Mantine с Vite](https://mantine.dev/guides/vite/)
- [Mantine theme object](https://mantine.dev/theming/theme-object/)
- [Mantine Styles API](https://mantine.dev/styles/styles-api/)
- [Mantine Charts](https://mantine.dev/charts/getting-started/)

На старте достаточно:

- `@mantine/core` и `@mantine/hooks`;
- `@mantine/notifications` для фоновой обработки;
- `@mantine/dropzone` для камеры/галереи и нескольких частей чека;
- `@mantine/dates` при реализации фильтров периода;
- `@mantine/charts` при реализации аналитики.

Версии фиксируются по последним стабильным релизам в момент начала этапа, а не
словом `latest` в committed `package.json`.

### Альтернатива: Base UI + CSS Modules

[Base UI](https://base-ui.com/react/overview/about) — стабильная headless-база
доступных React-компонентов без встроенного CSS. Она даст максимальный контроль
над тёплым визуальным стилем и почти не навяжет структуру, но нам придётся
самостоятельно собрать больше компонентов и отдельно выбрать даты, графики,
уведомления и загрузчик файлов.

Этот путь имеет смысл, если после первого дизайн-прототипа Mantine окажется
слишком заметен визуально. Для первой пользовательской версии скорость Mantine
полезнее потенциальной чистоты headless-подхода.

## Остальной frontend-стек

- `react-router` — явные URL для покупок, товаров, аналитики и review.
- `@tanstack/react-query` — server state, polling фоновой обработки и
  инвалидирование истории после изменений.
- `i18next` + `react-i18next` — русский/английский интерфейс и форматирование.
- `@mantine/form` с schema-валидацией — редакторы покупки и товара.
- `lucide-react` — единая система понятных SVG-иконок вместо текстовых символов.
- существующие Vitest + Testing Library — логика и взаимодействия компонентов.
- Storybook for React/Vite — живой каталог компонентов и всех их состояний.
- Playwright — несколько end-to-end потоков и локальные screenshot-сравнения.

Storybook официально поддерживает React/Vite и позволяет проверять компоненты в
изоляции: [React/Vite integration](https://storybook.js.org/docs/9/get-started/frameworks/react-vite).
Playwright умеет сравнивать скриншоты с локальными эталонами:
[visual comparisons](https://playwright.dev/docs/test-snapshots).

## Подход к совместному дизайну

Вместо серии оторванных статических картинок создаётся `UI Lab` в Storybook с
реалистичными состояниями. Владелец и жена открывают его с телефона и ноутбука,
кликают реальные элементы и дают конкретный feedback.

Первыми историями становятся:

1. `AppShell`: mobile и desktop.
2. `ReceiptCapture`: пусто, одна фотография, длинный чек из нескольких частей.
3. `ReceiptProgress`: загружено, распознаётся, готово, ошибка провайдера.
4. `ReceiptIssue`: пропущенный товар, неверная цена, неверное количество.
5. `PurchaseCard`: проверено, нужно проверить, возможный дубль.
6. `ProductCard`: без фото, несколько вариантов, история двух пользователей.
7. `AnalyticsAnswer`: число, график, coverage и список источников.

Для каждой истории задаются размеры телефона и desktop. После утверждения
состояние подключается к API; дизайн не строится на случайно удобном mock-ответе.

## Предлагаемая последовательность

Каждый этап выполняется по обязательному циклу из `AGENTS.md`: отдельный
implementation-агент → независимое review и тестирование root-агентом →
follow-up исправлений при необходимости → демонстрация пользователю → новое
явное согласие перед следующим этапом.

### UI-0. Основа дизайн-системы

- подключить Mantine, Storybook, иконки и локализацию;
- перенести цвета, типографику и spacing в тему;
- создать базовые `Button`, `Field`, `Surface`, `StatusBadge`, `EmptyState`;
- показать два гибридных экрана: mobile home и desktop home.

Фактически реализовано 3 августа 2026 года:

- выбран Mantine 9.5.1 и создана общая светлая тема, которую используют PWA и
  Storybook через один `AppProviders`;
- семантические токены дополнены отдельными контрастными цветами для muted- и
  accent-текста после проверки axe, при этом исходный тёплый акцент сохранён
  для крупных декоративных областей;
- созданы базовые `Button`, `Field`, `Surface`, `StatusBadge`, `EmptyState` и
  UI Lab-композиции `ReceiptCapture`, `PurchaseCard`, mobile home и desktop
  home;
- Storybook 10.5.5 использует встроенные Controls и viewport toolbar, отдельный
  RU/EN toolbar и addon a11y; mobile home закреплён за 390×844, desktop home —
  за 1440×960;
- композиции home и feature-компоненты пока работают на явных mock-данных и не
  заменяют существующий API-поток приложения.

Это фиксация выполненной реализации, а не отметка о пользовательском принятии:
`UI-0` остаётся на walkthrough, обязательном по `AGENTS.md`.

Обязательная часть завершения `UI-0` — совместный walkthrough Storybook на
компонентах самого Purchase Memory. Codex должен на примерах `Button`,
`ReceiptCapture` и `PurchaseCard` показать владельцу:

- как story импортирует компонент и его локальные стили;
- откуда приходят глобальные токены и Mantine theme;
- как Controls меняют props и состояния компонента;
- как переключаются русский/английский и mobile/desktop viewport;
- чем изолированный mock в Storybook отличается от компонента, подключённого к
  реальному API;
- как после выбора варианта удалить отклонённые эксперименты и оставить один
  согласованный компонент.

### UI-1. Каркас и навигация

- адаптивный AppShell;
- пять продуктовых разделов;
- mobile bottom navigation и desktop sidebar;
- русский/английский переключатель.

### UI-2. Добавление и фоновая обработка

- камера, галерея и несколько фотографий;
- прогресс фоновой обработки;
- уведомление о завершении;
- повтор после ошибки и защита от дублей.

### UI-3. История и точечная проверка

- лента покупок и фильтры;
- issue-based review по фрагменту чека;
- полный редактор как дополнительный режим;
- audit-friendly подтверждение исправлений.

### UI-4. Товары и впечатления

- каталог, поиск и группировка вариантов;
- карточка продукта, фотографии и barcode;
- личные впечатления двух пользователей;
- категории, теги и AI-предложения.

### UI-5. Аналитика

- отдельная строка естественного языка;
- структурированные ответы, графики и coverage;
- drill-down до покупок и чеков.

## Критерий готовности дизайна к реализации

- mobile home и desktop home утверждены на реальных размерах;
- основные токены и базовые компоненты не меняются от экрана к экрану;
- все обязательные состояния capture/review представлены в Storybook;
- интерфейс читается на русском и английском без обрезки;
- клавиатура, touch, focus и reduced motion проверены;
- пользователь может пройти основной поток без знания устройства AI-пайплайна.
