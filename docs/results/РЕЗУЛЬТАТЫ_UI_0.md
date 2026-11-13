# Purchase Memory — результаты реализации UI-0

Дата реализации: 3 августа 2026  
Статус: реализация и независимая проверка root-агентом завершены; Storybook
подготовлен к пользовательскому walkthrough, поэтому этап ещё не считается
принятым владельцем.

## Результат

Создан работающий UI Lab для Purchase Memory и общая основа дизайн-системы для
существующей React 19 + TypeScript + Vite PWA. Production-приложение продолжает
использовать существующие capture/history и backend API, но теперь запускается
в тех же Mantine theme, глобальных стилях и providers, что и Storybook.

В UI Lab реализованы два согласованных применения одной системы:

- mobile home — более эмоциональный `Warm Journal` с главным действием камеры,
  обработкой чека и недавней историей;
- desktop home — более плотный `Warm Utility` со сводкой, очередью
  `NeedsAttention` и недавними покупками.

Mock-данные намеренно используют польские магазины Biedronka, Lidl, Żabka,
Carrefour и Auchan, адреса в Варшаве и суммы в PLN. Они не создают новый API и
не меняют продуктовую backend-модель.

## Зафиксированные версии

Все прямые версии указаны точно в `package.json` и зафиксированы
`package-lock.json`:

| Пакет | Версия |
|---|---:|
| `react` / `react-dom` | 19.2.7 |
| `@mantine/core` / `@mantine/hooks` | 9.5.1 |
| `storybook` / `@storybook/react-vite` | 10.5.5 |
| `@storybook/addon-docs` / `@storybook/addon-a11y` | 10.5.5 |
| `lucide-react` | 1.28.0 |
| `i18next` | 26.3.6 |
| `react-i18next` | 17.0.11 |
| `vite` | 8.1.4 |
| `typescript` | 7.0.2 |

Storybook 10.5.5 не потребовал downgrade: установленный
`@storybook/react-vite` явно поддерживает Vite 5–8, React 16.8–19 и TypeScript
от 4.9. PWA plugin исключается только из Storybook Vite build, потому что UI
Lab не должен генерировать собственный service worker; production PWA сохраняет
существующую конфигурацию.

После установки `npm audit` обнаружил два high-severity advisory в транзитивных
`brace-expansion` и `fast-uri`. Совместимый `npm audit fix` поднял только patch-
версии этих пакетов; итоговый audit сообщает 0 vulnerabilities.

## Архитектура UI-0

- `src/app/AppProviders.tsx` — единая граница Mantine и i18next для приложения,
  Storybook и тестов.
- `src/design-system/theme.ts` — Mantine theme: палитра, шрифты, spacing,
  radii, shadows, focus, pointer и respect reduced motion.
- `src/styles.css` — семантические CSS-токены и глобальные правила. Токены
  `content.muted` и `accent.text` темнее декоративных оттенков, чтобы маленький
  текст проходил WCAG AA.
- `src/i18n/i18n.ts` — изолируемая RU/EN-конфигурация с синхронными локальными
  ресурсами; секретов и сетевой загрузки переводов нет.
- `src/design-system/components` — базовые компоненты с собственными решениями
  по intent, density, semantic status и структуре empty state.
- `src/ui-lab` — feature-компоненты и композиции на явных mock-данных.
- `.storybook` — React/Vite-конфигурация, общие providers, locale toolbar,
  phone/desktop viewports, Controls, Docs и axe.

`Button`, `Field`, `Surface`, `StatusBadge` и `EmptyState` не являются пустыми
обёртками. Например, `Button` фиксирует intent и accessible loading behavior,
`Field` связывает label/description/error/success с input, `StatusBadge`
переводит доменный статус в локализованную метку, иконку и контрастный tone.

## Stories для walkthrough

### Foundations и базовые компоненты

- `Foundations / Theme / Purchase Memory Theme`;
- `Components / Button / Playground`, `With Icon`, `Intent And State Matrix`;
- `Components / Field / Playground`, `States`;
- `Components / Surface / Playground`;
- `Components / StatusBadge / Playground`, `All Statuses`;
- `Components / EmptyState / Purchase History`.

### Purchase Memory

- `ReceiptCapture / Playground`, `Empty`, `One Photo`, `Long Receipt`,
  `Processing`, `Provider Error`;
- `PurchaseCard / Playground`, `Verified`, `Needs Attention`,
  `Possible Duplicate`, `Compact`;
- `Home / Mobile Warm Journal` — viewport 390×844;
- `Home / Desktop Warm Utility` — viewport 1440×960.

Глобальный toolbar `Interface language` переключает `Русский` / `English` и
заметно меняет заголовки, действия, статусы, даты и accessible names. Storybook
10 включает viewport tool как core feature; для обычных stories он доступен в
toolbar, а две home stories закреплены за своими проверочными размерами.

## Команды

Из `src/PurchaseMemory.Web`:

```bash
npm install
npm run dev
npm run storybook
npm test
npm run build
npm run build-storybook
npm audit
```

- PWA dev: `http://localhost:5173`;
- Storybook: `http://localhost:6006`;
- production PWA build по существующей настройке записывается в
  `src/PurchaseMemory.Api/wwwroot`;
- статическая сборка Storybook создаётся в
  `src/PurchaseMemory.Web/storybook-static` и исключена из source control.

## Выполненные проверки

| Проверка | Результат |
|---|---:|
| `npm test` | 5 файлов, 8/8 тестов |
| `npm run build` | успешно, production PWA + service worker |
| `npm run build-storybook` | успешно |
| `npm audit` | 0 vulnerabilities |
| TypeScript project build | входит в `npm run build`, успешно |
| `.NET 10 integration tests` | 76/76 тестов |

Тесты дополнительно подтверждают:

- production `App` рендерится через общий provider и сохраняет начальный
  capture-поток;
- `Field` выставляет accessible error и не пробрасывает `success` неизвестным
  атрибутом в native input;
- `ReceiptCapture` локализуется, объявляет processing через live region и
  provider failure через alert;
- `PurchaseCard` раскрывает позиции с корректным `aria-expanded` /
  `aria-controls`;
- mobile home содержит четыре компактных раздела: Home, Purchases, Products и
  Analytics; NeedsAttention остаётся заметным на главной.

В локальном Storybook дополнительно проверены:

- RU→EN через реальный toolbar на mobile home;
- mobile preset 390×844 и desktop preset 1440×960;
- изменение `ReceiptCapture.state` через Controls из `empty` в `error`;
- axe: 0 violations для mobile home, desktop home и ReceiptCapture Playground;
- console stories: 0 application warnings/errors.

Root-агент затем независимо повторил ключевые проверки: frontend-тесты и обе
production-сборки, полный `npm audit`, визуальный осмотр mobile/desktop home,
проверку Controls на `ReceiptCapture` и весь `.NET 10` integration suite. Итог:
8/8 frontend-тестов, 76/76 integration-тестов, 0 известных npm-уязвимостей и
0 accessibility violations в проверенных stories.

Storybook manager 10.5.5 сам выводит предупреждение о том, что внутреннему
`PopoverProvider` понадобится обязательный `ariaLabel` в Storybook 11. Оно
возникает в `sb-manager/globals-runtime.js`, не в коде или iframe Purchase
Memory, и не влияет на статическую сборку.

## Известные ограничения

- Home, `ReceiptCapture` и `PurchaseCard` в UI Lab — изолированные mock-
  композиции. Они не утверждают новый backend contract и пока не заменяют
  существующие `CapturePage` / `HistoryPage`.
- Само production-приложение ещё не получило UI-1 AppShell, пять настоящих
  route-разделов или пользовательский переключатель языка.
- `ReceiptCapture` в UI Lab демонстрирует состояния и несколько частей чека,
  но не загружает реальные файлы; рабочая загрузка остаётся в существующем
  API-потоке.
- Dark theme, auth/household, durable queue, analytics, restaurants и deploy
  не входят в UI-0.
- Скриншотные regression-baselines и Playwright suite остаются кандидатами для
  следующих этапов, когда владелец утвердит визуальный вариант.

## Обязательный walkthrough перед принятием

Root-агенту нужно показать владельцу на `Button`, `ReceiptCapture` и
`PurchaseCard`:

1. как story импортирует компонент и его CSS Module;
2. как `.storybook/preview.tsx` подключает общий `AppProviders`, а theme и
   семантические токены приходят из `theme.ts` и `styles.css`;
3. как Controls меняют intent/loading, capture state и card density;
4. как toolbar меняет RU/EN и viewport;
5. почему mock story не делает API-вызов и чем отличается от production
   `CapturePage`;
6. как после выбора оставить одну утверждённую реализацию и удалить
   отклонённые экспериментальные stories/варианты.

Только после этого walkthrough и явного решения владельца UI-0 можно отметить
принятым. Переход к UI-1 требует отдельного согласия.
