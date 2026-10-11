# Purchase Memory

PWA для личной истории покупок: сфотографировать чек, распознать его через
выбранный backend-провайдер, проверить и исправить строки, сохранить в SQLite и
позже найти товар или магазин. Локально доступны системные модели Apple и
Ollama; OpenAI Responses API можно включить одной настройкой backend. Тот же
backend продолжает публиковать MCP tools для экспериментов с AI-клиентами.

## Что уже работает

- адаптивное PWA с камерой/выбором файла, предпросмотром, состоянием фоновой обработки и редактированием результата;
- REST API `upload → process → review → save` и поиск по истории;
- основной локальный pipeline на Mac: `Apple Vision OCR → Translation PL→EN → Foundation Models → C#-валидация`;
- Ollama `glm-ocr:latest → qwen3.5:4b` остаётся автоматическим fallback и основным вариантом в Docker/Linux;
- OpenAI Responses API принимает изображение и возвращает результат по общей
  строгой JSON Schema; SDK и experimental `OPENAI001` изолированы внутри
  Infrastructure;
- SQLite с EF Core migrations, хранение оригинала изображения на диске и защита от повторного сохранения;
- прежние MCP tools `create_purchase_draft`, `update_purchase_draft`, `search_purchases`;
- ежедневный человекочитаемый лог в `logs/`;
- native-запуск на macOS и воспроизводимая Docker-сборка.

Основной стек: .NET SDK 10.0.301, C# 14, ASP.NET Core/EF Core SQLite 10.0.9, React 19.2.7, TypeScript 7.0.2, Vite 8.1.4 и `vite-plugin-pwa` 1.3.0. Точные версии зафиксированы в `global.json`, `Directory.Packages.props` и `src/PurchaseMemory.Web/package-lock.json`.

## Быстрый запуск на Mac

1. Нужны macOS 26.4+, Apple silicon и включённый Apple Intelligence. На проверенной машине польско-английская системная модель Translation уже установлена.
2. Для автоматического fallback установите и запустите [Ollama](https://ollama.com/download), затем один раз загрузите модели:

   ```bash
   ./scripts/setup-models.sh
   ```

3. Соберите Apple-sidecar, PWA и запустите backend одной командой:

   ```bash
   ./scripts/run-local.sh
   ```

4. Откройте [http://127.0.0.1:5099](http://127.0.0.1:5099).

`run-local.sh` на Mac компилирует небольшой Swift-sidecar в `.artifacts/apple-intelligence/`, затем использует локальный .NET SDK через `scripts/dotnet.sh`. База создаётся в `data/purchases.db`, изображения — в `data/receipts/`, логи — в `logs/purchase-memory-YYYYMMDD.log`.

Если frontend уже собран, достаточно:

```bash
./scripts/dotnet.sh run --project src/PurchaseMemory.Api
```

## Переключаемые pipeline

Backend выбирает полный способ распознавания через строковый ID, а не через
зависимость от конкретного AI SDK. Локальная конфигурация Mac находится в
`src/PurchaseMemory.Api/appsettings.Development.json`:

```json
{
  "ReceiptRecognition": {
    "Pipeline": "apple",
    "FallbackPipelines": ["ollama"]
  }
}
```

В продакшне (`appsettings.json`) основной путь — `anthropic` (Claude Haiku 5.5,
ключ `ANTHROPIC_API_KEY`), запасные — `openai`, затем `ollama`.

- `anthropic` — общий receipt workflow поверх Claude Messages API (официальный SDK `Anthropic`);
- `apple` — Vision OCR → Translation → Foundation Models → C#-валидация;
- `ollama` — локальный Ollama pipeline; его можно сделать основным, поменяв только `Pipeline`;
- `openai` — общий receipt workflow поверх OpenAI Responses API;
- fallback-пути вызываются по порядку, если предыдущая реализация корректно отклонила результат распознавания.

Prompt, JSON Schema, parser и C#-валидация общие для Claude, OpenAI и Ollama
(для Claude из схемы убираются неподдерживаемые ограничения, parser проверяет их сам).
Провайдеры отвечают только за свой транспорт. Существующий
`IReceiptRecognitionPipeline` остаётся совместимым фасадом для worker и router.

### Ollama

Быстрый локальный baseline — `Hybrid`: `glm-ocr:latest` получает изображение,
а `qwen3.5:4b` преобразует недоверенный OCR-текст в общий контракт. Перед первой
отправкой изображения backend проверяет `/api/tags` и `/api/show`: все модели
должны существовать, а модель, получающая изображение, должна объявлять
capability `vision`. Успешная проверка кэшируется на время жизни provider, чтобы
repair-попытка не выполняла повторный preflight.

Native Ollama на Apple Silicon использует Metal и является рекомендуемым
вариантом для Mac. Контейнер API обращается к нему через
`http://host.docker.internal:11434`; Ollama не является обязательным тяжёлым
сервисом Compose.

### OpenAI

Ключ читается только backend-процессом из `OPENAI_API_KEY`. Его нельзя добавлять
в `appsettings*.json`, PWA, Docker image, query string или логи. Пример
конфигурации без секрета:

```json
{
  "ReceiptRecognition": {
    "Pipeline": "openai",
    "FallbackPipelines": [],
    "OpenAiModel": "gpt-5.6",
    "OpenAiImageDetail": "High",
    "OpenAiReasoningEffort": "Low",
    "OpenAiRequestTimeoutSeconds": 90,
    "OpenAiMaximumOutputTokens": 2048
  }
}
```

Те же значения можно передать переменными окружения с двойным подчёркиванием,
например `ReceiptRecognition__Pipeline` и
`ReceiptRecognition__OpenAiModel`. При отсутствующем ключе или модели endpoint
статуса возвращает понятное состояние без обращения к платной модели.

Официальная библиотека `OpenAI` закреплена на версии 2.8.0. Responses surface в
этой версии помечен `OPENAI001`; предупреждение подавлено только в одном
внутреннем transport-файле Infrastructure и не затрагивает Application.

## Docker

Системные Apple frameworks недоступны внутри Linux-контейнера. Поэтому Compose явно выбирает Ollama; сам Ollama на Apple Silicon лучше оставить нативным ради Metal-ускорения. API/PWA и SQLite обращаются к нему на host:

```bash
./scripts/setup-models.sh
docker compose up --build
```

Compose привязывает порт только к `127.0.0.1`, а `data/` и `logs/` оставляет в проекте. Остановить стек:

```bash
docker compose down
```

## Проверки

```bash
./scripts/dotnet.sh build PurchaseMemory.slnx
./scripts/dotnet.sh test PurchaseMemory.slnx
./scripts/dotnet.sh format PurchaseMemory.slnx --no-restore --verify-no-changes
npm --prefix src/PurchaseMemory.Web test -- --run
npm --prefix src/PurchaseMemory.Web run build
```

Повторить локальное сравнение моделей на публичных примерах:

```bash
./scripts/dotnet.sh run --project tools/PurchaseMemory.ReceiptBenchmarks
```

Перезапустить только Apple pipeline и объединить свежие результаты с сохранёнными Ollama-прогонами:

```bash
./scripts/dotnet.sh run --project tools/PurchaseMemory.ReceiptBenchmarks -- \
  apple-vision-translation-foundation --merge
```

Эталонные поля, происхождение изображений и ограничения лицензий находятся в `test-data/receipts/public/`; свежие измерения — в `test-data/receipts/benchmarks/latest.md` и `.json`.

Реальный OpenAI smoke-test не входит в обычный test suite и выполняет платный
model call. Сначала передайте `OPENAI_API_KEY` процессу безопасным способом,
затем явно задайте модель:

```bash
OPENAI_MODEL=gpt-5.6 ./scripts/dotnet.sh run \
  --project tools/PurchaseMemory.OpenAiSmoke
```

По умолчанию используется публичный пример Lidl. Другой локальный файл можно
передать первым аргументом после `--`. Полный receipt JSON печатается в
терминал, поэтому не используйте этот инструмент с чувствительным чеком в
записываемой CI-консоли.

Отдельный Ollama smoke-test также не входит в обычный suite, не скачивает модели
и обрабатывает ровно один публичный пример Lidl через общий workflow:

```bash
./scripts/dotnet.sh run --project tools/PurchaseMemory.OllamaSmoke
```

Адрес и модели можно переопределить переменными `OLLAMA_BASE_ADDRESS`,
`OLLAMA_MODE`, `OLLAMA_OCR_MODEL`, `OLLAMA_PARSER_MODEL` и
`OLLAMA_VISION_MODEL`. По умолчанию используются
`127.0.0.1:11434`, `glm-ocr:latest` и `qwen3.5:4b`. Для диагностики только на
несекретных изображениях можно задать `OLLAMA_SMOKE_INCLUDE_RAW=true`: при
ошибке строгого контракта tool напечатает последний сырой JSON модели.

Пример прямого vision-прогона:

```bash
OLLAMA_MODE=Vision OLLAMA_VISION_MODEL=qwen3.5:9b \
  ./scripts/dotnet.sh run --project tools/PurchaseMemory.OllamaSmoke
```

Фактические smoke- и end-to-end результаты текущего ядра находятся в
[`РЕЗУЛЬТАТЫ_ТЕХНИЧЕСКОГО_ЯДРА.md`](РЕЗУЛЬТАТЫ_ТЕХНИЧЕСКОГО_ЯДРА.md).

## HTTP и MCP

- PWA: `http://127.0.0.1:5099/`
- health: `GET /health`
- статус выбранного pipeline и fallback: `GET /api/recognition/status`
- загрузка: `POST /api/receipts` (`multipart/form-data`, поле `image`)
- MCP Streamable HTTP: `http://127.0.0.1:5099/mcp`

Регистрация MCP в Codex CLI/Desktop:

```bash
codex mcp add purchase_memory --url http://127.0.0.1:5099/mcp
```

## Телефон и безопасность

PWA/service worker и надёжный доступ к камере на телефоне требуют HTTPS. Backend пока не имеет пользователей и авторизации, поэтому его нельзя открывать напрямую в интернет с настоящими чеками. Для личного теста используйте приватную сеть (например, Tailscale Serve поверх `127.0.0.1:5099`) либо временный tunnel только с обезличенными изображениями и выключайте его сразу после теста.

Прямое подключение ChatGPT к локальному `127.0.0.1` не работает: облачный клиент не видит Mac. Поэтому PWA является основным интерфейсом прототипа, а MCP остаётся дополнительным экспериментальным входом.

## Архитектура

```text
src/
  PurchaseMemory.Domain/          сущности и инварианты
  PurchaseMemory.Application/     сценарии и интерфейсы портов
  PurchaseMemory.Infrastructure/  SQLite, файлы, очередь и реализации recognition pipeline
  PurchaseMemory.AppleIntelligence/ Swift-sidecar для Vision/Translation/Foundation Models
  PurchaseMemory.Api/             REST, MCP, worker и раздача PWA
  PurchaseMemory.Web/             React PWA
tests/
  PurchaseMemory.IntegrationTests/
tools/
  PurchaseMemory.ReceiptBenchmarks/
  PurchaseMemory.OpenAiSmoke/
```

Зависимости направлены внутрь: Domain/Application не знают о HTTP, MCP, Ollama, Apple frameworks, EF Core или React. Контракт `IReceiptRecognitionPipeline` находится в Application, реализации — снаружи, а router получает их коллекцией через DI. Фоновая очередь в этой итерации находится в памяти, но состояние чека хранится в SQLite, поэтому после перезапуска обработку можно поставить в очередь повторно.

## Текущие ограничения

- один локальный пользователь, без OAuth и синхронизации между устройствами;
- перед сохранением результат обязательно нужно проверить: OCR ошибается в сокращениях, а очень маленькие изображения теряют строки и цифры;
- Apple pipeline доступен только нативно на совместимом Mac; Docker/Linux используют Ollama fallback;
- in-memory очередь подходит для прототипа, но не для нескольких backend-реплик;
- поиск пока обычный SQL `LIKE`, без каталога эквивалентных товаров и embeddings;
- benchmark состоит только из трёх легально пригодных публичных примеров и не доказывает production-качество.

План: `ИТЕРАЦИЯ_2.md`; фактические результаты и советы: `РЕЗУЛЬТАТЫ_ИТЕРАЦИИ_2.md`. Долгосрочное направление: `ПЛАН_ПРОЕКТА.md`.
