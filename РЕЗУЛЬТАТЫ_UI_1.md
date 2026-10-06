# Purchase Memory — результаты реализации UI-1

Дата реализации: 3 августа 2026  
Статус: реализация и независимый review завершены; этап готов к пользовательской
приёмке. UI-2 не начат.

## Результат

Production PWA перенесена в общий адаптивный `AppShell` из утверждённой тёплой
светлой дизайн-системы UI-0. На ширине от 56rem используется постоянный desktop
sidebar, ниже — mobile header и нижняя навигация с safe-area отступами.

Появились настоящие URL-разделы:

- `/` — честная production Home без mock-покупок и выдуманных показателей;
- `/purchases` — существующая реальная история покупок;
- `/purchases/new` — существующий capture/review flow;
- `/products` — локализованная граница будущего UI-4;
- `/analytics` — локализованная граница будущего UI-5 без fake analytics;
- `/needs-attention` — локализованная граница будущего UI-3;
- неизвестный URL безопасно возвращает на Home.

Desktop sidebar содержит все пять продуктовых разделов. Mobile navigation
содержит четыре подтверждённых компактных пункта: Home, Purchases, Products и
Analytics. `Needs attention` явно доступен на Home отдельной заметной карточкой
и действием; фиктивный count не показывается, поскольку backend пока его не
предоставляет.

## Навигация и сохранённый поток

Добавлен официальный `react-router` 8.3.0 в минимальном declarative режиме.
Навигация использует History API, поэтому работают URL, deep links,
back/forward и PWA fallback на `index.html`. Самописного router нет.

Существующие `CapturePage`, `HistoryPage` и API-контракты сохранены. CTA Home и
desktop header открывают `/purchases/new`; успешный callback сохранения
переходит на `/purchases`. Capture UX и backend не перерабатывались.

## Локализация

RU/EN переключатель находится в mobile header и desktop sidebar. Все видимые
production-строки и accessible names нового shell и страниц локализованы;
существующие CapturePage и HistoryPage также переведены, чтобы при смене языка
экран не становился смешанным.

Выбор записывается в `localStorage` под ключом `purchase-memory.locale`,
восстанавливается при новом mount/reload и синхронизирует `html[lang]`.
`AppProviders` остаётся единой границей для PWA, Storybook и тестов; переданный
Storybook locale продолжает переключать общий i18n instance.

Статус распознавания имеет три согласованных состояния: нейтральное
`Проверяем…` до ответа backend, available после успешной проверки и unavailable
после ошибки или явного ответа `available: false`. Capture не разрешает запуск,
пока availability не подтверждена; после ошибки status-запроса он получает
явный unavailable status и локализованное объяснение.

## UI Lab и stories

Production `AppShell` вынесен в общий компонент с CSS Module. UI Lab Home больше
не содержит собственные расходящиеся sidebar/mobile nav/header: обе home-
композиции используют тот же `AppShell` через `MemoryRouter`.

Добавлены stories:

- `Purchase Memory / AppShell / Mobile Home Active`;
- `Purchase Memory / AppShell / Mobile Purchases Active`;
- `Purchase Memory / AppShell / Desktop Home Active`;
- `Purchase Memory / AppShell / Desktop Recognition Checking`;
- `Purchase Memory / AppShell / Desktop Needs Attention Active`.

Существующие UI-0 stories сохранены.

## Зафиксированные версии

Все прямые версии по-прежнему exact-pinned в `package.json` и lockfile. Новая
прямая runtime-зависимость:

| Пакет | Версия |
|---|---:|
| `react-router` | 8.3.0 |

React 19.2.7, Mantine 9.5.1, Storybook 10.5.5, Vite 8.1.4 и TypeScript 7.0.2
не менялись.

## Выполненные проверки implementation-агента

Из `src/PurchaseMemory.Web`:

| Проверка | Результат |
|---|---:|
| `npm test` | 7 файлов, 16/16 тестов |
| `npm run build` | успешно, production PWA + service worker |
| `npm run build-storybook` | успешно |
| `npm audit` | 0 vulnerabilities |

Тесты проверяют deep link, переходы через History API и back, active nav state,
все пять desktop-разделов, четыре mobile-пункта, доступность Needs attention с
Home, locale switch/persistence/remount, отсутствие production mock-метрик и
контракт перехода capture → purchases после save.

Отдельные regression-тесты подтверждают нейтральный shell indicator во время
pending-запроса и disabled capture после ошибки `getRecognitionStatus`, включая
случай, когда пользователь уже выбрал файл.

Во встроенном браузере дополнительно проверены:

- mobile 390×844: mobile header, четыре пункта bottom nav, Home action в review,
  RU→EN, переход в Analytics, back/forward;
- reload на `/analytics`: английский язык и deep link сохранились;
- desktop 1440×960 на `/needs-attention`: sidebar виден, bottom nav скрыт,
  один `main`, active state корректен, горизонтального overflow нет;
- warning/error console: 0 на проверенных mobile и desktop экранах.

## Независимый review root-агента

Root-агент отдельно проверил архитектурные границы, маршруты, локализацию,
доступность, responsive-поведение, отделение production-данных от Storybook
mock-данных и сохранность существующих CapturePage/HistoryPage.

Во время review обнаружена регрессия обработки ошибки status-запроса: после
выбора файла Capture мог разрешить распознавание при неизвестной доступности
backend, а shell преждевременно показывал unavailable во время загрузки. Тот же
implementation-агент получил follow-up и исправил поведение: Capture теперь
разрешает запрос только при `available === true`, ошибка преобразуется в явный
unavailable status, а pending-состояние отображается нейтрально. Добавлены
регрессионные тесты и отдельная Storybook story.

Финальный независимый прогон:

| Проверка | Результат |
|---|---:|
| `npm test` | 7 файлов, 16/16 тестов |
| `npm run build` | успешно, production PWA + service worker |
| `npm run build-storybook` | успешно |
| `npm audit` | 0 vulnerabilities |
| `.NET integration tests` | 76/76 тестов |
| `dotnet format --verify-no-changes` | успешно |

В реальном браузере root-агент повторно проверил размеры 390×844 и 1440×960,
персистентность RU/EN после reload, прямой deep link, back/forward,
горизонтальный overflow и console errors. Для mobile и desktop stories AppShell
проверка доступности Storybook завершилась с 0 violations (23 passes,
1 inconclusive).

## Как открыть и проверить

```bash
cd src/PurchaseMemory.Web
npm run dev
```

Открыть `http://localhost:5173`. Backend API в dev-режиме ожидается на
`http://127.0.0.1:5099`; если он не запущен, shell честно показывает
недоступность распознавания, а маршруты продолжают работать.

Для UI Lab:

```bash
npm run storybook
```

Открыть `http://localhost:6006` и раздел `Purchase Memory / AppShell`.

## Известные ограничения и граница UI-2

- Products, Analytics и Needs attention намеренно являются качественными
  empty/coming-later страницами: настоящие workflows относятся к UI-4, UI-5 и
  UI-3 соответственно.
- У Home нет серверных recent purchases, review count или metrics: UI-1 не
  добавляет новые backend-контракты и не выдаёт mock-данные за production.
- Текущий CapturePage по-прежнему принимает одну фотографию, использует старый
  foreground polling и требует review/save. Камера/галерея с несколькими
  частями, фоновая обработка, notification, retry UX и защита от дублей — это
  UI-2.
- Auth/household, offline upload, deploy, dark theme и реальные аналитические
  расчёты не входят в UI-1.
