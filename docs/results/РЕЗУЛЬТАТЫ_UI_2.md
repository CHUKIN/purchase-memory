# Purchase Memory — результаты реализации UI-2

Дата реализации и review: 3 августа 2026  
Статус: implementation и независимый review root-агента завершены; UI-2 готов к
приёмке владельцем. UI-3 не начат.

## Результат

Реализован минимальный production-поток добавления чека камерой или из галереи.
Один чек содержит от 1 до 8 изображений в явно заданном пользователем порядке.
После загрузки и постановки в локальную `Channel`-очередь можно уйти с экрана:
состояние восстанавливается с backend по устойчивому URL `/receipts/{id}` и на
Home. Terminal transition показывает одно ненавязчивое in-app уведомление.

Распознанный результат намеренно не сохраняется автоматически. Подтверждённой
confidence policy ещё нет, а локальные smoke-прогоны не являются безопасным
benchmark. Состояния `Ready` и `Needs attention` открывают сохранённый
review/save flow; transaction-level possible duplicate protection при save не
изменена.

## Capture UX

- отдельные действия `Сфотографировать` (`capture=environment`, один файл) и
  `Выбрать из галереи` (`multiple`);
- preview каждой части, удаление и доступное изменение порядка кнопками
  вверх/вниз с keyboard/focus/touch состояниями;
- client и server limits: 8 частей, 15 MiB на часть, 60 MiB на весь чек;
- endpoint request/form limits включают ещё 2 MiB multipart overhead, поэтому
  документированные 60 MiB полезной нагрузки не отсекаются default Kestrel limit;
- поддерживаются только JPEG, PNG и WebP с server-side magic-byte validation;
- выбранный порядок является порядком частей во всех последующих слоях;
- UI прямо сообщает, что crop, alignment и автоматическое определение порядка
  пока не выполняются.

Все строки нового production-потока доступны на русском и английском. Mobile
stories используют preset 390×844, desktop — 1440×960 через актуальный
Storybook 10 `globals.viewport`.

## Backend и данные

### Domain и storage

`ReceiptCapture` владеет ordered collection `ReceiptImagePart` с position,
storage key, content type, size и SHA-256. Старые поля первой фотографии
сохранены как compatibility mirror, чтобы миграция была только добавляющей;
прикладной workflow и API используют коллекцию частей.

`IReceiptImageStore` принимает position. Локальная реализация хранит
`part-000.*`, `part-001.*` и так далее за прежним интерфейсом object storage.
При ошибке любой части или превышении общего лимита удаляется весь временный
каталог нового capture. Бинарные данные и base64 в БД и лог не попадают.

### Exact duplicate

Для одной части fingerprint остаётся прежним SHA-256, поэтому старое поведение
не ломается. Для нескольких частей fingerprint вычисляется из последовательности
`position:partSha256`; перестановка частей является другим набором. Повторная
загрузка того же упорядоченного набора удаляет временные файлы, не создаёт новый
capture, возвращает explicit `exactDuplicate: true` и существующий stable URL.
Unique constraint race между двумя одновременными одинаковыми upload также
преобразуется в этот идемпотентный результат; удаляются только временные файлы
проигравшего capture, файлы победителя сохраняются.

### Provider-neutral recognition

Application-контракты `IReceiptExtractor`, `ReceiptAnalysisRequest` и
`StructuredVisionRequest` принимают ordered image collection.

- OpenAI Responses получает текст и все `input_image` parts в одном user message
  в порядке capture;
- Ollama direct vision получает все `images` в одном chat message;
- Ollama Hybrid запускает OCR для каждой части последовательно, затем передаёт
  parser одну JSON-размеченную ordered sequence transcripts;
- Apple compatibility pipeline обрабатывает все части по порядку и объединяет
  строки, не меняя provider-neutral границу.

Raw provider JSON, изображения, base64 и secrets не логируются. Структурированные
логи recovery содержат только число задач; worker errors — только receipt id и
обычную exception diagnostics.

## Background, resume и retry

- `ChannelReceiptProcessingDispatcher` остаётся единственной локальной очередью;
  Redis/RabbitMQ не добавлялись;
- `GET /api/receipts?limit=N` возвращает recent captures, включая active jobs;
- `GET /api/receipts/{id}` и `/receipts/{id}` восстанавливают progress/review
  после navigation или reload;
- worker при старте восстанавливает `Queued` и прерванные `Processing` записи,
  переводит последние обратно в `Queued` и повторно ставит их в Channel;
- consumer запускается до startup recovery, поэтому восстановление более чем 32
  записей не блокируется на заполненной bounded Channel;
- `POST /api/receipts/{id}/process` идемпотентно используется для retry с уже
  сохранёнными изображениями;
- если initial `/process` недоступен уже после успешного upload, UI всё равно
  открывает stable URL, обновляет recent list и показывает явное действие
  `Начать распознавание` для durable состояния `Uploaded`;
- React Query polling работает только пока есть `uploaded`, `queued` или
  `processing` captures и прекращается на terminal state/unmount;
- notification создаётся только для наблюдаемого перехода active → terminal,
  с детерминированным id и доступной ссылкой на stable URL, поэтому повторный
  poll не создаёт spam;
- ready editor инициализируется серверным snapshot один раз на receipt id:
  focus/refetch не перезаписывает несохранённые пользовательские правки.
- detail header различает active, ready, failed и saved состояния и не сообщает
  о фоновой обработке после её завершения;
- Home и detail progress используют корректные plural forms количества частей
  на RU и EN.

Home показывает только реальные recent/active receipts. Mock metrics не
добавлялись.

## REST-контракты

- `POST /api/receipts`: multipart field `images` в пользовательском порядке;
  ответ `{ receipt, exactDuplicate }`, `201` для нового и `200` для exact duplicate;
- `GET /api/receipts?limit=20`: recent captures;
- `GET /api/receipts/{id}`: resumable state;
- `GET /api/receipts/{id}/images/{position}`: конкретная часть;
- `POST /api/receipts/{id}/process`: initial queue и retry;
- review/save endpoints сохранены.

## Миграция и rollback

Миграция `20260803170000_AddReceiptImageParts` создаёт
`receipt_image_parts`, копирует каждый существующий single-image capture как
part 0 и создаёт unique index `(ReceiptCaptureId, Position)`. Старые колонки и
файлы не удаляются и не переименовываются. Миграционный integration test сначала
создаёт initial schema и legacy row, затем обновляет БД и проверяет part 0.

Rollback migration удаляет только новую таблицу. Поскольку compatibility mirror
остаётся, старый код снова видит первую часть. Для новых multipart captures при
rollback сохраняется mirror первой части, но дополнительные части становятся
недоступны старой версии приложения; перед rollback после реального multipart
использования нужен backup БД и `data/receipts` и решение о сохранении этих
дополнительных файлов.

## Новые exact-pinned зависимости

Версии предварительно проверены в npm Registry на 3 августа 2026 и зафиксированы
в `package.json` и `package-lock.json`:

| Пакет | Версия |
|---|---:|
| `@tanstack/react-query` | `5.101.4` |
| `@mantine/notifications` | `9.5.1` |

Несвязанные зависимости не обновлялись.

## Storybook

Production `ReceiptCapture`, `ReceiptProgress` и duplicate notice используются
реальным приложением и stories. Старый расходящийся UI-0 mock ReceiptCapture
удалён. Представлены empty, single, multipart, uploading, queued, processing,
ready, needs-attention, provider error и exact duplicate на RU/EN и phone/desktop
viewport. Viewport presets задаются через Storybook 10 `globals.viewport`, а
single/multipart previews используют валидный компактный PNG fixture.

## Проверки implementation-агента

| Проверка | Результат |
|---|---:|
| `npm test` | 11 файлов, 32/32 |
| `npm run build` | успешно |
| `npm run build-storybook` | успешно |
| `npm audit` | 0 vulnerabilities |
| `dotnet build PurchaseMemory.slnx` | успешно, 0 warnings/errors |
| .NET integration tests | 84/84 |
| `dotnet format --verify-no-changes` | успешно |

Frontend tests покрывают selection order/remove, отдельные camera/gallery inputs,
duplicate outcome, stable URL resume, retry с сохранёнными фото, review/save,
active polling, notification once с доступной stable-ссылкой, initial queue
failure после durable upload, видимую 9-ю часть с validation, сохранение грязного
editor state при focus refetch и status-specific progress guidance.
Также проверены plural forms частей на Home и странице чека и
статус-зависимое описание detail header для active/ready/failed/saved.
.NET tests покрывают multipart domain/storage/workflow, exact ordered-set
duplicate, recent listing, migration legacy → part 0, OpenAI/Ollama multi-image
payloads и repair prompt с overlap de-duplication, retry/save idempotence,
partial storage cleanup, endpoint multipart limit, recovery более 32 задач и
принудительный concurrent unique race существующими и новыми проверками.

## Независимый review root-агента

Root-агент повторно просмотрел архитектурные границы, миграцию, очередь,
идемпотентность, upload limits, provider payloads, конфигурацию, логи и
production UI. В двух follow-up циклах implementation-агент исправил найденные
при review проблемы: startup recovery для очереди больше 32 задач, durable
upload при ошибке initial queue, видимую проверку 9-й части, сохранение грязного
editor state при refetch, Kestrel multipart limit, ordered/overlap prompt,
доступную ссылку notification, status-specific guidance, concurrent exact
duplicate race, валидные Storybook previews и plural/status copy.

Root-агент независимо выполнил:

| Проверка | Результат |
|---|---:|
| `npm test` | 11 файлов, 32/32 |
| `npm run build` | успешно; PWA/service worker собраны |
| `npm run build-storybook` | успешно |
| `npm audit --omit=dev` | 0 vulnerabilities |
| `dotnet build PurchaseMemory.slnx` | успешно, 0 warnings/errors |
| .NET integration tests | 84/84 |
| `dotnet format --verify-no-changes` | успешно |

В браузере проверены production Home, capture и stable saved receipt на RU/EN,
mobile Storybook 390×844, desktop Storybook 1440×960, multipart previews,
provider-error/retry и обновление PWA. На проверенных production stories axe
показывает 0 accessibility violations; console production PWA чистая. Единственное
сообщение Storybook — известное предупреждение самого manager о будущем API
Storybook 11, а не код Purchase Memory.

## Как открыть

```bash
./scripts/run-local.sh
```

Открыть `http://127.0.0.1:5099`, затем:

- `/purchases/new` — новый capture;
- `/receipts/{id}` — progress/retry/review;
- `/` — реальные active/recent jobs.

Для UI Lab:

```bash
cd src/PurchaseMemory.Web
npm run storybook
```

Открыть разделы `Purchase Memory / ReceiptCapture / Production` и
`Purchase Memory / ReceiptProgress / Production`.

## Ограничения

- auth/household, offline/PWA upload queue, web push, Redis/RabbitMQ и UI-3 не
  входят в этап;
- автоматические crop/alignment/order, PDF и confidence autosave отсутствуют;
- Channel queue рассчитана на один локальный instance; multi-replica recovery
  требует отдельного будущего решения;
- issue-based review и автоматическая привязка `Needs attention` к конкретным
  фрагментам относятся к UI-3;
- Apple multipart aggregation является compatibility path; основной проверенный
  multipart-контракт для production-кандидатов — OpenAI и Ollama.

Следующий кандидатный этап — UI-3, но он не начат и требует отдельного явного
согласия владельца после review и приёмки UI-2.
