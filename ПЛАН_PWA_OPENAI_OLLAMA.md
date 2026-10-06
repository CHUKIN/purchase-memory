# Purchase Memory — план перехода к PWA и сменным AI-провайдерам

Статус: техническое ядро (этапы 0–3) завершено и проверено; этапы 4–7 приостановлены до совместного продуктового этапа  
Дата: 31 июля 2026  
Цель завершённого цикла: текущая PWA загружает фотографию чека, backend получает проверяемый структурированный черновик через выбранного в конфигурации провайдера — OpenAI или локальный Ollama.

Фактический прогресс:

| Этап | Статус | Результат |
|---|---|---|
| 0. Контракт и ADR | Завершён и проверен 31 июля 2026 | [`ADR 0001`](docs/adr/0001-pwa-first-provider-neutral-receipt-analysis.md), [`receipt.extract` v1](docs/contracts/receipt-extract-v1.md), [JSON Schema v1](docs/contracts/receipt-extract-result-v1.schema.json) |
| 1. Общий receipt workflow | Завершён и проверен 31 июля 2026 | Provider-neutral Application workflow, общий prompt/schema/parser/validator, resolver, тонкий Ollama adapter и совместимый `IReceiptExtractor` facade |
| 2. OpenAI provider | Завершён и проверен 31 июля 2026 | OpenAI .NET 2.8.0, Responses API image input, strict Structured Outputs, status/error mapping, совместимый facade и opt-in smoke tool |
| 3. Ollama parity | Завершён и проверен 31 июля 2026 | Native API, общий workflow/schema, `/api/show` vision guard до изображения, 76/76 offline tests и реальные smoke-прогоны |
| Проверка ядра | Завершена 31 июля 2026 | [`Результаты`](РЕЗУЛЬТАТЫ_ТЕХНИЧЕСКОГО_ЯДРА.md): upload → queue → Ollama → `ready` draft; без автоматического сохранения |
| Совместный продуктовый этап | Следующий | [`Дизайн и набор функций`](СОВМЕСТНЫЙ_ЭТАП_ДИЗАЙН_И_ФУНКЦИИ.md) |
| 4–7 | Backlog | Перепланировать после совместного выбора сценариев, экранов и scope первого релиза |

## 1. Решение в двух словах

Направление правильное:

- основной интерфейс — уже существующая PWA;
- OpenAI — основной кандидат для размещённого в интернете приложения;
- Ollama — локальный провайдер для разработки, дешёвых прогонов и сравнения качества;
- API-ключ OpenAI хранится только на backend;
- PWA никогда не обращается к OpenAI или Ollama напрямую;
- выбор провайдера и модели делается конфигурацией backend, без пересборки frontend;
- результат модели всегда проходит общую JSON-схему, десериализацию и C#-валидацию;
- пользователь проверяет черновик перед сохранением покупки.

Полноценный автономный агент или агентный framework пока не нужен. Для первой версии достаточно управляемого сценария: изображение + тип задачи + необязательная инструкция → структурированный черновик → проверка человеком. Это уже ощущается как «мини-агент», но остаётся предсказуемым, тестируемым и безопасным.

## 2. Целевой пользовательский сценарий

```text
PWA
  ├─ сфотографировать или выбрать чек
  ├─ увидеть и при необходимости заменить фотографию
  ├─ необязательно написать:
  │    «это чек Lidl, внимательно проверь скидки»
  └─ нажать «Распознать»
          |
          v
ASP.NET Core
  ├─ проверить и нормализовать изображение
  ├─ сохранить задание и поставить его в очередь
  ├─ выбрать AI-провайдер из конфигурации
  ├─ выполнить общий Receipt workflow
  ├─ проверить структурированный результат в C#
  └─ сохранить черновик, предупреждения и технические метаданные
          |
          v
PWA
  ├─ показать распознанные позиции
  ├─ дать всё исправить
  └─ сохранить покупку только после явного подтверждения
```

Первая задача имеет стабильный идентификатор `receipt.extract`. В будущем без изменения основного протокола можно добавить `product.identify`, `price_tag.extract` или `purchase.describe`, но они не входят в ближайшую реализацию.

## 3. Главный архитектурный принцип

Сейчас `AppleReceiptExtractor` и `OllamaReceiptExtractor` являются полными независимыми pipeline. Это удобно для эксперимента, но при добавлении OpenAI приведёт к копированию prompt, JSON-схемы, разбора дат и правил валидации.

Нужно разделить два уровня.

### Общий workflow приложения

`ReceiptAnalysisWorkflow` должен:

- формировать один версионируемый prompt;
- добавлять необязательную пользовательскую инструкцию как данные, а не как системную команду;
- передавать одну и ту же JSON-схему любому провайдеру;
- десериализовать ответ в общий DTO;
- выполнять общую C#-валидацию и нормализацию;
- при необходимости сделать не более одной исправляющей попытки;
- вернуть существующий `ReceiptExtractionResult`.

### Тонкие адаптеры провайдеров

`OpenAiStructuredVisionClient` и `OllamaStructuredVisionClient` должны отвечать только за:

- преобразование общего запроса в протокол конкретного API;
- отправку изображения, prompt и JSON-схемы;
- timeout, отмену запроса и классификацию ошибок;
- возврат JSON и технических метаданных;
- health/capability check.

Адаптеры не должны сохранять покупки, обращаться к базе, самостоятельно менять бизнес-правила или иметь разные версии receipt prompt.

Ориентировочные порты уровня Application:

```csharp
public interface IStructuredVisionProvider
{
    string Id { get; }

    Task<StructuredVisionResponse> GenerateAsync(
        StructuredVisionRequest request,
        CancellationToken cancellationToken);

    Task<AiProviderStatus> GetStatusAsync(
        CancellationToken cancellationToken);
}

public interface IReceiptAnalysisWorkflow
{
    Task<ReceiptExtractionResult> AnalyzeAsync(
        ReceiptAnalysisRequest request,
        CancellationToken cancellationToken);
}
```

Точные имена можно скорректировать при реализации. Важна граница ответственности, а не конкретное название интерфейса.

## 4. Предлагаемая конфигурация

Новая конфигурация должна описывать workflow отдельно от провайдеров:

```json
{
  "Ai": {
    "Provider": "ollama",
    "FallbackProviders": [],
    "Receipt": {
      "PromptVersion": "receipt.extract.prompt.v1",
      "SchemaVersion": "receipt.extract.schema.v1",
      "MaximumRepairAttempts": 1
    },
    "Providers": {
      "OpenAI": {
        "Model": "configured-openai-vision-model",
        "ImageDetail": "high",
        "RequestTimeoutSeconds": 90
      },
      "Ollama": {
        "BaseAddress": "http://127.0.0.1:11434",
        "Model": "configured-local-vision-model",
        "ContextLength": 16384,
        "MaximumOutputTokens": 2048,
        "RequestTimeoutSeconds": 120
      }
    }
  }
}
```

Правила:

- локальный Development по умолчанию использует `ollama`;
- облачное окружение по умолчанию использует `openai`;
- модель всегда задаётся конфигурацией, а не константой в коде;
- OpenAI key приходит через переменную окружения или secret manager и отсутствует в `appsettings*.json`;
- автоматический fallback выключен по умолчанию;
- fallback можно включить явно, но каждый переход должен логироваться;
- облачная установка не должна рассчитывать на Ollama по адресу `localhost`: это будет localhost облачного контейнера, а не Mac пользователя.

Для OpenAI сначала нужно получить корректностный baseline на актуальной vision-модели GPT-5.6, затем сравнить доступные более дешёвые модели семейства на нашем наборе чеков. Модель выбирается по результатам качества, задержки и стоимости, а не только по названию.

Для Ollama перед обработкой нужно проверять через API модели, что выбранная модель действительно объявляет vision capability. Наличие модели в списке установленных ещё не означает, что она принимает изображения.

## 5. Этапы реализации

Каждый этап рассчитан на отдельную задачу агента. Следующий этап начинается после проверки предыдущего.

### Этап 0. Зафиксировать контракт и ADR

Зависимости: нет.

Фактический статус: **завершён 31 июля 2026**. Этап является
документационным: код, endpoint-ы, база, PWA и пользовательское поведение не
изменялись.

Артефакты:

- [`ADR 0001: PWA-first и provider-neutral анализ чеков`](docs/adr/0001-pwa-first-provider-neutral-receipt-analysis.md);
- [`Контракт receipt.extract v1`](docs/contracts/receipt-extract-v1.md);
- [`Строгая JSON Schema результата v1`](docs/contracts/receipt-extract-result-v1.schema.json).

Задача:

- добавить короткий ADR о PWA-first и provider-neutral архитектуре;
- зафиксировать `receipt.extract` как единственный intent первой версии;
- описать общий request/response и JSON Schema;
- определить, какие поля пользовательской инструкции сохраняются;
- определить версионирование prompt и схемы;
- записать политику: модель создаёт только черновик, сохранение всегда подтверждает человек.

Критерии приёмки:

- в документации нет двух разных контрактов для OpenAI и Ollama;
- явно описаны privacy, timeout, retry и failure behavior;
- Apple pipeline пока не удаляется и считается legacy/experimental до завершения сравнения;
- этап не меняет пользовательское поведение приложения.

### Этап 1. Выделить общий receipt workflow

Зависимости: этап 0.

Фактический статус: **завершён и проверен 31 июля 2026**.

Реализовано:

- provider-neutral контракты `IStructuredVisionProvider`,
  `IStructuredVisionProviderResolver` и `IReceiptAnalysisWorkflow` находятся в
  Application и не зависят от Ollama SDK/HTTP;
- `receipt.extract` v1 имеет единые runtime prompt, JSON Schema, строгий parser,
  C#-валидацию и одну ограниченную repair-попытку;
- автоматический тест семантически сравнивает runtime schema с нормативным
  `docs/contracts/receipt-extract-result-v1.schema.json`;
- Ollama transport выделен в тонкий `OllamaStructuredVisionProvider`; режимы
  Vision и существующий Hybrid используют один workflow-контракт;
- `OllamaReceiptExtractor` оставлен совместимым фасадом для текущего
  `ReceiptProcessingService` и legacy router, Apple pipeline не изменён;
- provider выбирается resolver-ом по стабильному регистронезависимому `Id`;
- startup options validation проверяет зарегистрированные pipeline id, модели,
  URI, timeout и лимиты; каталог id расширяется регистрацией descriptor рядом с
  новым adapter;
- полный test suite после этапа работал без сети и реальной Ollama: **47/47**;
- `dotnet build` завершён без warnings/errors, `dotnet format
  --verify-no-changes` не нашёл изменений.

Задача:

- добавить provider-neutral запрос и ответ в `PurchaseMemory.Application`;
- выделить общий receipt prompt, JSON Schema, парсинг и C#-валидацию;
- добавить resolver провайдера по стабильному строковому `Id`;
- сохранить `IReceiptExtractor` как фасад для существующего `ReceiptProcessingService`;
- перенести Ollama-специфичный transport из полного pipeline в адаптер;
- не ломать текущие REST endpoints и состояния чека;
- добавить options validation: неизвестный provider, пустая model, некорректный timeout.

Рекомендуемый поток:

```text
ReceiptProcessingService
  → IReceiptExtractor compatibility facade
  → ReceiptAnalysisWorkflow
  → IStructuredVisionProviderResolver
  → configured provider adapter
  → shared deserialization and C# validation
```

Критерии приёмки:

- prompt и receipt JSON Schema существуют в одном месте;
- provider adapters не ссылаются на EF Core, repositories или domain entities;
- общий workflow тестируется через fake provider без запуска модели;
- все существующие тесты проходят;
- поведение текущего Ollama pipeline не регрессировало;
- в логах остаются provider, model и duration, но нет полного изображения или секретов.

Что не делать:

- не добавлять OpenAI;
- не менять UI;
- не вводить Semantic Kernel или другой агентный framework;
- не делать generic-платформу для любых AI-задач.

### Этап 2. Добавить OpenAI provider

Зависимости: этап 1.

Фактический статус: **завершён 31 июля 2026**.

Реализовано:

- официальная библиотека `OpenAI` закреплена на стабильной версии 2.8.0;
- internal `OpenAiResponsesTransport` использует typed Responses API,
  in-memory image input и общий prompt, а strict Structured Outputs получает
  нормативную `receipt.extract` JSON Schema;
- `OPENAI001` подавлен только внутри одного Infrastructure transport-файла;
  Application, PWA, REST API и общий workflow не зависят от SDK;
- конфигурируются model, image detail (`Low`/`High`/`Original`), reasoning effort,
  timeout и maximum output tokens; reasoning по умолчанию явно `Low`;
- singleton provider лениво и потокобезопасно создаёт один переиспользуемый
  SDK client/HTTP transport; при завершении приложения transport освобождается;
- стабильный ID `openai` подключён к существующим
  `ReceiptRecognition:Pipeline`, fallback router и
  `IReceiptRecognitionPipeline` facade без второго selector;
- `OPENAI_API_KEY` читается только из environment backend-процесса и отсутствует
  в `appsettings`, request body, API/PWA и логируемых полях;
- status при отсутствующей модели или ключе не делает model call;
- безопасно классифицируются 401, 429, другие HTTP-ошибки, timeout, network,
  caller cancellation, refusal, incomplete, failed, empty и invalid output;
- request id, input/output usage, status и duration возвращаются в общие
  metadata, насколько их предоставляет SDK;
- contract test проверяет фактический JSON, созданный официальным SDK:
  model, prompt, image data URL/content type, detail, strict schema, reasoning,
  store=false и token limit;
- отдельный `PurchaseMemory.OpenAiSmoke` выполняет реальный платный запрос
  только по явной команде с `OPENAI_API_KEY` и `OPENAI_MODEL`;
- полный suite работает без сети и ключа: **63/63**; build — без
  warnings/errors; format — без изменений.

Задача:

- подключить актуальную стабильную версию официального C# SDK OpenAI;
- изолировать SDK полностью внутри Infrastructure adapter;
- использовать Responses API с image input;
- требовать Structured Outputs по общей строгой JSON Schema;
- передавать изображение с явно выбранным detail, не полагаясь на меняющийся `auto`;
- обрабатывать refusal, incomplete response, invalid schema, 401, 429, timeout и сетевые ошибки;
- сохранять request id, token usage, provider, model и latency, если API их вернул;
- добавить health/status, который не отправляет чек и не тратит значимые токены;
- добавить конфигурацию через `OPENAI_API_KEY`/secret provider.

Примечание: если Responses API в текущей версии C# SDK всё ещё помечен как experimental, это допустимо только внутри адаптера. Application и тесты не должны зависеть от experimental типов SDK.

Критерии приёмки:

- переключение `ReceiptRecognition:Pipeline` с `ollama` на `openai` не требует изменения frontend или workflow;
- ключ отсутствует в репозитории, логах, API response и PWA bundle;
- contract tests работают с подменённым `HttpMessageHandler` или test transport;
- реальный smoke-test запускается только по явному флагу и при наличии ключа;
- некорректный ответ модели не создаёт сохранённую покупку;
- README содержит команды локального запуска с OpenAI без значения самого ключа.

Что не делать:

- не вызывать OpenAI напрямую из браузера;
- не добавлять произвольный tool loop;
- не включать неограниченные автоматические retry;
- не логировать body запроса с изображением.

### Этап 3. Привести Ollama к тому же контракту

Фактический статус 31 июля 2026: **завершён и проверен**.

- `Hybrid` использует `glm-ocr:latest → qwen3.5:4b`; `Vision` остаётся
  переключаемым конфигурацией;
- `/api/tags` проверяет наличие всех нужных моделей, `/api/show` проверяет
  `vision` у модели, которая получает изображение; guard выполняется до
  `/api/generate`/`api/chat` и кэширует успешный результат;
- `/api/chat` получает общую строгую schema и в `format`, и в prompt, поскольку
  это заметно улучшило соблюдение контракта `qwen3.5:4b`;
- offline suite после реализации: **76/76** без Ollama и сети;
- opt-in smoke на публичном `lidl-2025-official-sample.png` вернул валидный
  черновик за **25 351 мс**. Структурный контракт пройден, но качество baseline
  пока слабое: итог `102.62 PLN` распознан, список товаров пуст. Выбор default
  модели по качеству остаётся задачей общего benchmark, а не этого этапа.
- high-resolution Biedronka через тот же быстрый baseline вернула магазин, дату,
  валюту и 3/3 товарных строк. Модель пропустила скидку и ошиблась в итоге;
  общий C#-слой сохранил draft с предупреждением, а не покупку.

Зависимости: этапы 1 и 2.

Задача:

- реализовать Ollama adapter через native `/api/chat`;
- отправлять изображение и ту же JSON Schema через поле `format`;
- выставить `temperature: 0`;
- проверять наличие модели и vision capability через Ollama API;
- вернуть унифицированные metadata и ошибки;
- оставить model name, context length и limits в конфигурации;
- использовать тот же workflow, prompt, schema и валидатор, что OpenAI;
- при необходимости оставить старый hybrid OCR pipeline как отдельный legacy provider, а не смешивать его с новым vision adapter.

Первая локальная модель выбирается benchmark-тестом среди уже совместимых с Mac моделей. Предварительный кандидат можно взять из текущей конфигурации проекта, но он принимается только если:

- Ollama объявляет vision capability;
- модель стабильно возвращает JSON по схеме;
- она укладывается в приемлемые RAM и время;
- качество на польских чеках не хуже заранее согласованного порога.

Критерии приёмки:

- один и тот же fake/golden contract suite применяется к OpenAI и Ollama;
- отсутствие Ollama или модели отображается как понятный status, а не 500;
- модель без vision capability отклоняется до отправки изображения;
- для смены локальной модели достаточно изменить конфигурацию;
- приложение работает без OpenAI key при выборе Ollama.

### Обязательная пауза: совместно определить дизайн и функции

До реализации расширенного UI выполняется
[`совместный продуктовый этап`](СОВМЕСТНЫЙ_ЭТАП_ДИЗАЙН_И_ФУНКЦИИ.md).
На нём владелец и Codex выбирают главный пользовательский цикл, терминологию,
набор данных, карту экранов и scope первого релиза. Текущая PWA до этого
остаётся проверочным стендом технического ядра.

### Этап 4. Превратить текущий Capture UI в PWA «мини-агента»

Зависимости: этапы 1–3.

Задача:

- добавить необязательное поле «Что учесть при распознавании?»;
- передавать `intent=receipt.extract` и инструкцию вместе с upload;
- сохранять инструкцию до помещения задания в очередь;
- показывать понятные этапы: загрузка, ожидание, анализ, проверка;
- добавить retry после ошибки без повторного выбора фотографии;
- отображать фактически использованные provider и model после обработки;
- сохранить существующее ручное редактирование и явное подтверждение;
- восстановить незаконченный черновик после перезагрузки страницы;
- добавить клиентскую обработку orientation и согласованную политику HEIC.

Ограничения пользовательской инструкции:

- разумный предел длины, например 1 000 символов;
- инструкция уточняет разбор чека, но не меняет формат ответа;
- она не может отключить валидацию или автоматически сохранить покупку;
- текст на самом чеке также считается недоверенным вводом.

Критерии приёмки:

- сценарий проходит с камеры и из галереи на узком экране;
- reload страницы не теряет уже загруженный receipt id;
- пользователь видит ошибку и может повторить обработку;
- provider выбирается backend-конфигурацией, а не доверенным параметром браузера;
- ключ OpenAI отсутствует в Network requests браузера;
- frontend tests покрывают upload с инструкцией, polling, retry и review.

### Этап 5. Надёжность очереди, состояние и наблюдаемость

Зависимости: этап 4.

Задача:

- формализовать состояния `uploaded → queued → processing → ready/failed → saved`;
- сохранять instruction, intent, provider, model, prompt version, schema version, duration и provider request id;
- сохранять token usage отдельно, если оно доступно;
- добавить correlation id задания;
- сделать постановку в очередь идемпотентной;
- при старте приложения находить зависшие `queued/processing` задания и безопасно восстанавливать их;
- ограничить параллелизм и размер очереди;
- классифицировать ошибки на retryable и terminal;
- оставить не более одной model-repair попытки и ограниченное число transport retry с backoff.

Для локальной одноэкземплярной версии текущий `Channel` допустим. Абстракция `IReceiptProcessingDispatcher` сохраняется. Durable queue нужна только перед несколькими репликами backend или если восстановление через SQLite окажется недостаточным.

Критерии приёмки:

- двойное нажатие не запускает две параллельные обработки одного чека;
- перезапуск backend не оставляет задание навечно в `processing`;
- логи позволяют найти полный путь одного задания;
- API не отдаёт пользователю stack trace или секреты;
- retry не создаёт дубликат покупки.

### Этап 6. Общий benchmark и критерии выбора модели

Зависимости: этапы 2–5.

Задача:

- переделать `PurchaseMemory.ReceiptBenchmarks`, чтобы он запускал одинаковый набор через любой provider/model;
- оставить существующие легальные публичные чеки и постепенно довести набор минимум до 20–30 примеров;
- хранить expected result отдельно от model response;
- считать метрики по магазину, дате, итогу, числу позиций, названиям и line total;
- отдельно фиксировать JSON validity, latency, tokens и ориентировочную стоимость;
- сохранить provider, model, prompt version, schema version и дату прогона;
- добавить regression gate для prompt/schema changes.

Начальные go/no-go критерии:

- 100% ответов либо валидны по схеме, либо завершаются контролируемой ошибкой;
- ни один тест не сохраняет покупку автоматически;
- магазин, дата и итог правильно извлекаются не менее чем в 90% пригодных примеров;
- связка «название позиции + сумма строки» достигает не менее 85% на согласованном golden dataset;
- OpenAI и Ollama сравниваются на одинаковых входных изображениях без ручной подстройки prompt под провайдера.

Пороги можно уточнить после первого baseline, но нельзя выбирать модель по одному красивому чеку.

Критерии приёмки:

- benchmark создаёт человекочитаемый Markdown и машинный JSON;
- результаты воспроизводимы одной командой;
- из отчёта понятно, какая модель быстрее, точнее и дешевле;
- модель по умолчанию выбирается в ADR на основании отчёта.

### Этап 7. Подготовить безопасный первый deploy

Зависимости: этапы 5 и 6.

Задача:

- раздавать PWA и API с одного HTTPS origin;
- добавить минимальную авторизацию до публикации настоящих чеков;
- добавить лимиты размера/типа изображения, rate limit и политику хранения;
- вынести OpenAI key в secret manager хостинга;
- решить судьбу SQLite и файлов: persistent disk для личного single-instance deploy либо PostgreSQL + object storage для переносимого варианта;
- добавить `/health/live` и `/health/ready`;
- подготовить production-конфигурацию только с OpenAI;
- задокументировать backup/restore и удаление пользовательских данных.

Render, Heroku и похожие платформы подходят для размещения лёгкого ASP.NET backend с OpenAI provider. Ollama внутри такого маленького web-контейнера обычно не нужна: она увеличивает требования к памяти/GPU и время запуска. Локальный Ollama остаётся dev/test provider, а облачная PWA использует OpenAI.

Критерии приёмки:

- приложение недоступно посторонним;
- после restart/deploy не теряются база и изображения;
- PWA устанавливается и камера работает через HTTPS;
- никакой production secret не находится в образе или репозитории;
- есть проверенный сценарий backup/restore.

## 6. Что оставить на потом

До получения стабильного receipt extraction не добавлять:

- автономное выполнение произвольных tools;
- автоматическое сохранение без review;
- чат с неограниченной историей;
- embeddings и vector database;
- мобильное native-приложение;
- обработку видео;
- распознавание упаковки товара;
- несколько пользователей и семьи;
- сложную durable queue;
- микросервисы.

После успешного этапа 6 можно добавить ограниченные tools:

- найти похожую прошлую покупку;
- показать предыдущую цену товара;
- предложить возможный дубликат;
- найти магазин в истории.

Даже тогда `save` остаётся отдельным действием с подтверждением пользователя.

## 7. Проверки после каждого этапа

Минимальный набор:

```bash
./scripts/dotnet.sh build PurchaseMemory.slnx
./scripts/dotnet.sh test PurchaseMemory.slnx
./scripts/dotnet.sh format PurchaseMemory.slnx --no-restore --verify-no-changes
npm --prefix src/PurchaseMemory.Web test -- --run
npm --prefix src/PurchaseMemory.Web run build
```

Для этапов с реальными моделями нужны отдельные opt-in проверки:

```text
Ollama integration:
  запускается только когда доступен локальный Ollama и установлена модель

OpenAI integration:
  отдельный PurchaseMemory.OpenAiSmoke запускается только явной командой
  при наличии OPENAI_API_KEY и OPENAI_MODEL

Обычный test suite:
  никогда не требует сеть, Ollama или оплачиваемый OpenAI-запрос
```

## 8. Порядок задач для агентов

Рекомендуемый порядок:

1. этап 0 — ADR и контракты;
2. этап 1 — общий workflow и provider port;
3. этап 2 — OpenAI adapter;
4. этап 3 — Ollama adapter и parity;
5. этап 4 — PWA mini-agent UX;
6. этап 5 — очередь, recovery и telemetry;
7. этап 6 — benchmark и выбор default model;
8. этап 7 — deploy hardening.

Шаблон задания агенту:

```text
Реализуй только этап N из ПЛАН_PWA_OPENAI_OLLAMA.md.
Сначала изучи текущий код и связанные тесты. Не начинай следующий этап.
Сохрани обратную совместимость, если этап явно не разрешает её нарушить.
Добавь тесты и обнови документацию, относящуюся к этому этапу.
В конце запусти все перечисленные проверки и опиши изменения, риски и незакрытые вопросы.
```

Перед началом следующего этапа отдельно проверяем:

- соблюдены ли границы Application/Infrastructure;
- не продублированы ли prompt и schema;
- не попал ли ключ в frontend или логи;
- не появилось ли скрытое автоматическое сохранение;
- проходят ли тесты и formatter;
- соответствует ли фактический код критериям приёмки этапа.

## 9. Официальные технические ориентиры

- OpenAI рекомендует Responses API для современных мультимодальных и агентных сценариев.
- Image input можно передавать как URL, file id или binary/base64 content; для мелкого текста нужно явно выбирать подходящий detail и учитывать стоимость изображения.
- Structured Outputs нужно задавать строгой JSON Schema, а не надеяться на «верни JSON» в prompt.
- Ollama native chat API принимает изображения в `messages[].images` и JSON Schema в `format`.
- Ollama умеет vision со structured outputs, но capability выбранной модели нужно проверять отдельно.

Полезные ссылки:

- [OpenAI vision and image inputs](https://developers.openai.com/api/docs/guides/images-vision)
- [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)
- [OpenAI migration guide for GPT-5.6](https://developers.openai.com/api/docs/guides/upgrading-to-gpt-5p6-sol)
- [Ollama vision capability](https://github.com/ollama/ollama/blob/main/docs/capabilities/vision.mdx)
- [Ollama structured outputs](https://github.com/ollama/ollama/blob/main/docs/capabilities/structured-outputs.mdx)
