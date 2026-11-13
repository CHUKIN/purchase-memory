# Results: UI moved from Mantine to Ionic React (iOS mode)

Date: 2026-10-13. Base commit: `cfd573b`. Owner picked the iPhone-like
prototype variant (Ionic) and approved the migration.

## What changed

- `@mantine/*` removed; `@ionic/react` 9.0.7 added. `setupIonicReact({ mode: "ios" })`
  on every device (`src/app/ionic.ts`), light only (`color-scheme: light`,
  Ionic dark palette not imported). Brand green `#315c4b` is Ionic `primary`.
- React Router 8 kept. `@ionic/react-router` supports only React Router 6, so
  there is no `IonRouterOutlet`: a standalone `IonTabBar` (`AppShell.tsx`)
  navigates with `useNavigate`, each screen is a plain `.ion-page`. Routes are
  lazy-loaded (`src/app/lazyPage.tsx`); a stale chunk after an app update triggers one reload, then a «screen did not load» notice.
- Own kit on top of Ionic in `src/design-system/components`: `Page`
  (large collapsing title, back button, toolbar actions), `SectionHeader`,
  `Footnote`, `Notice`, `EmptyState`, `LoadingList`, `ItemLink`/`ButtonLink`,
  `PrimaryButton`, `FilterButton` (count badge), `Sheet` (`IonModal` with
  breakpoints or full screen), `PickerModal`/`SelectRow` (searchable list,
  group headers, checkmark), `DateField` (`IonDatetime` in a sheet; replaces
  `DateInput`), `DecimalInput` (accepts «12,5»). Old Mantine components
  (`Button`, `Field`, `DateInput`, `IconButton`, `Surface`, `StatusBadge`,
  `SkeletonCard`, `EmptyState`) and all `*.module.css` are deleted; so is the
  UI-0 `src/ui-lab` prototype.
- Toasts (`src/app/toasts.tsx`) replace Mantine notifications for receipt
  ready/failed news.

## Where things moved

- Settings, help, RU/EN and sign-out: «Ещё» (⋯ top right on «Главная») →
  sheet «Настройки». The login screen keeps its own RU/EN switch.
- «Нужно проверить»: a row with the count on «Главная»; the same count is a
  badge on the «Главная» tab.
- Filters of «Покупки», «Товары» and «Нужно проверить»: filter button top
  right (with a count) → bottom sheet; «Сбросить» / «Готово» in the sheet.
- Category and tag management: «Справочники» at the bottom of «Товары» →
  «Управление категориями» / «Управление тегами» (full-screen sheets titled
  «Категории» / «Теги»).
- Product card: category and brand save immediately (no separate save step).
- Receipt review and purchase editor: lines are a list; tap a line to edit it
  in a sheet, swipe left to delete («Удалить строку N» also in the sheet).
- Help (`helpText.ts` ru/en) rewritten for these locations; `HelpLink`s keep
  the same topics as before, plus «Товары» catalog footnote (`products`) and
  the catalog filter sheet (`filters`).

## Checks (2026-10-13, final run)

- `tsc -b`: 0 errors. `npm run build`: OK. `npm run build-storybook`: OK,
  185 stories; each story iframe loaded at 390 px and rendered without an
  error display (0 broken, including AppShell, CategoryManager,
  ProductClassification, ReceiptDiscountLine, Tags that were broken earlier).
- `npx vitest run`: 35 files, 306 tests passed. Tests run against a semantic
  stand-in for `@ionic/react` (`src/test/ionicShim.tsx`, aliased in
  `vite.config.ts`; helpers in `src/test/ionicHelpers.ts`);
  `ionic.smoke.test.tsx` loads the real package. Deleted tests belong to
  deleted components (`DateInput`, `Field`, `IconButton`, ui-lab); their
  behaviour is covered by `Kit.test.tsx` (DecimalInput, DateField, SelectRow).
- `./scripts/dotnet.sh test`: 626 passed, 8 skipped (Postgres/S3 without a
  server), 0 failed.
- iOS Simulator (iPhone 17e, Safari, iOS 27), built app served by the API on a
  fresh temp data folder (dev auth bypass, apple pipeline, AI features off),
  two public test receipts from `test-data/receipts/public`: Главная, «Ещё»
  sheet, Подсказки, capture screen, receipt review (issue card fix, full
  editor, tap-to-edit line sheet, swipe delete), save, Покупки filter sheet
  and filtered list, purchase detail → «Исправить» → swipe delete → save,
  Товары + Справочники + categories sheet, Нужно проверить, Аналитика.

## Bundle (vite, gzip)

- Before (Mantine): ~366 kB gzip.
- After: first load (entry + modulepreloads + CSS) ~426 kB gzip; all chunks
  together ~605 kB gzip. Ionic core components are tree-shaken by a
  `moduleSideEffects` rule in `vite.config.ts`; the largest chunks are
  Ionic components (~183 kB) and recharts (~99 kB, lazy, analytics/products only).

## Known limits

- No page-transition animation and no iOS swipe-back gesture (no
  `IonRouterOutlet`).
- Light theme only.
- First load is larger than with Mantine (see above).
- Vite warns about a chunk over 500 kB minified (Ionic components).

## Review follow-up

- Touch targets: icon-only toolbar buttons («Фильтры», «Ещё») use
  `.pm-icon-button` (44×44, measured in Storybook; toolbar height unchanged).
- Back button tells the truth: a screen that opens another passes its address
  in router state (`returnTo` + browser-history index, `app/backNavigation.ts`).
  The opened screen names that screen (list screens by name, detail screens as
  «Назад») and returns there — stepping back through history when the index
  is known, otherwise opening the address. Without state (deep link, opened
  from elsewhere without origin) label and target are the fixed parent screen;
  history is never guessed. Used by purchase, product, receipt, capture and
  review-queue screens; covered by `app/backNavigation.test.tsx`.
- Receipt review and purchase editor: «Дата и время» value uses the text
  colour like the other fields (`DateField formValue`).
- Removed 77 i18n keys left unused by the migration from ru and en (ru/en key
  sets still identical); 46 keys that were already unused before the stage
  are kept.
- Checks: `npm run build` OK; `npx vitest run` 36 files, 314 tests passed.

## Next candidate step

Owner walk-through on a real iPhone (installed PWA), then deploy.
