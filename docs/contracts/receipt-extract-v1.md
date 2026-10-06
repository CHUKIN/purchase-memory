# Контракт `receipt.extract` v1

Этот документ задаёт provider-neutral семантику первой AI-задачи Purchase
Memory. Он нормативен вместе с
[`receipt-extract-result-v1.schema.json`](receipt-extract-result-v1.schema.json).
OpenAI, Ollama и тестовый fake получают эквивалентные данные и возвращают один
формат результата.

## Идентификаторы версий

| Сущность | Значение v1 |
|---|---|
| Intent | `receipt.extract` |
| Prompt | `receipt.extract.prompt.v1` |
| Schema | `receipt.extract.schema.v1` |

Версии prompt и schema сохраняются для каждого запуска. Provider и model
сохраняются отдельно и не меняют семантику контракта.

## Запрос workflow

Логическая форма `ReceiptAnalysisRequest`:

```json
{
  "receiptId": "4af0274f-bf1f-4cbb-92af-5400850f33de",
  "correlationId": "01K1GJRXFC9KKE80RYSJ0V2D74",
  "intent": "receipt.extract",
  "image": {
    "contentType": "image/jpeg",
    "sha256": "64 lowercase hexadecimal characters",
    "content": "<binary stream>"
  },
  "userInstruction": "Это чек Lidl, внимательно проверь скидки.",
  "promptVersion": "receipt.extract.prompt.v1",
  "schemaVersion": "receipt.extract.schema.v1"
}
```

Требования:

- `receiptId` связывает анализ с сохранённым изображением;
- `correlationId` связывает очередь, provider request и безопасные логи;
- `intent` в v1 может быть только `receipt.extract`;
- `image.content` — поток байтов внутри backend, а не публичный URL и не
  значение для логирования;
- допустимые content type определяет image-ingestion приложения до обращения к
  provider;
- `userInstruction` — `null` или строка длиной не более 1 000
  Unicode-символов;
- backend выбирает provider/model конфигурацией, поэтому их нет среди
  пользовательских параметров workflow.

### Хранение пользовательской инструкции

Backend хранит нормализованную инструкцию: внешние пробельные символы удаляются,
пустая строка становится `null`, остальной текст сохраняется без смыслового
переписывания. Она записывается до постановки receipt в очередь, чтобы retry
использовал тот же ввод.

Инструкция:

- хранится рядом с receipt job и наследует его access/retention/delete policy;
- не попадает в обычные application logs или provider metadata;
- передаётся выбранному provider вместе с изображением;
- включается в prompt в явно ограниченном блоке недоверенных данных;
- может уточнить распознавание, но не может изменить schema, intent, системные
  правила, provider, retry-политику или разрешить сохранение покупки.

Изменение инструкции пользователем создаёт новый запуск анализа с новым
execution id; история должна позволять установить, какая версия ввода дала
текущий черновик.

## Запрос provider adapter

Логическая форма `StructuredVisionRequest` содержит:

- бинарное изображение и его media type;
- полностью сформированный workflow prompt;
- строгую schema `receipt.extract.schema.v1`;
- prompt/schema version;
- correlation id;
- deadline/cancellation token.

Provider adapter не получает domain entity или repository и не формирует
собственный receipt prompt. Provider-specific параметры вроде model, image
detail, token limit и base address приходят только из backend configuration.

## Ответ модели

Модель возвращает только JSON-документ, соответствующий
`receipt-extract-result-v1.schema.json`.

Пример:

```json
{
  "merchantName": "Lidl",
  "purchasedAt": "2026-07-31T18:42:00+02:00",
  "currency": "PLN",
  "receiptTotal": 21.97,
  "items": [
    {
      "name": "MLEKO 3.2%",
      "quantity": 2,
      "unit": "szt",
      "unitPrice": 3.99,
      "lineTotal": 7.98
    },
    {
      "name": "KAWA ZIARNISTA",
      "quantity": 1,
      "unit": "szt",
      "unitPrice": 13.99,
      "lineTotal": 13.99
    }
  ]
}
```

`null` означает, что значение нельзя надёжно извлечь. Модель не должна
выдумывать отсутствующие значения. `items` всегда присутствует и может быть
пустым только если позиции не удалось надёжно распознать.

Правила значений:

- `merchantName`: название продавца с чека, до 200 символов;
- `purchasedAt`: ISO 8601 с числовым UTC offset либо `null`;
- `currency`: трёхбуквенный ISO 4217 uppercase-код либо `null`;
- денежные значения: неотрицательные JSON number либо `null`;
- `quantity`: JSON number строго больше нуля либо `null`;
- `unit`: напечатанная или безопасно нормализованная единица, до 30 символов;
- `name`: непустое название позиции, до 500 символов;
- `items`: не более 300 позиций;
- порядок `items` соответствует порядку на чеке.

Schema запрещает неизвестные свойства на всех уровнях и делает все известные
поля обязательными; неизвестность выражается через `null`, а не отсутствующее
поле.

## Ответ provider adapter

Логическая форма `StructuredVisionResponse` отделяет payload от наблюдаемости:

```json
{
  "rawJson": "<structured JSON candidate>",
  "provider": "openai",
  "model": "configured-model",
  "providerRequestId": "optional-provider-id",
  "finishReason": "completed",
  "inputTokens": 1234,
  "outputTokens": 321,
  "durationMs": 2150
}
```

Usage и provider request id могут быть `null`, если API их не сообщает.
Provider response не является готовым доменным результатом, пока общий workflow
не выполнил schema validation, десериализацию и C#-валидацию.

## Результат workflow

Успех содержит:

- receipt draft из schema;
- предупреждения общей C#-валидации;
- provider/model/request id и usage, если доступны;
- prompt/schema version;
- duration и correlation id.

Управляемая ошибка содержит стабильную категорию:

- `configuration`;
- `authentication`;
- `capability`;
- `rate_limited`;
- `timeout`;
- `provider_unavailable`;
- `provider_refusal`;
- `incomplete_response`;
- `invalid_structured_output`;
- `domain_validation`;
- `cancelled`;
- `unexpected`.

Внутренние сообщения provider, stack trace, изображения, полный prompt и
невалидный model output не возвращаются в PWA. Ошибка не создаёт и не изменяет
сохранённую покупку.

## Валидация

Порядок один для всех providers:

1. API/transport сообщает завершённый либо классифицированный ответ.
2. JSON проверяется по точной schema version.
3. JSON десериализуется общим кодом.
4. C# проверяет даты, ISO currency, положительность quantity, денежные значения,
   ограничения длины и согласованность арифметики.
5. При разрешённой исправляющей попытке workflow передаёт модели только
   безопасное описание ошибок и исходную schema.
6. После не более одной repair-попытки результат становится draft либо
   контролируемой ошибкой.

Ни один шаг не ослабляет обязательность ручного review.

## Timeout, retry и failure behavior

- Provider timeout задаётся конфигурацией; baseline — 90 секунд OpenAI и 120
  секунд Ollama.
- Не более двух transport retry допустимы для `rate_limited`, временной
  недоступности и безопасных сетевых сбоев с backoff/jitter.
- `configuration`, `authentication`, `capability`, malformed request и
  cancellation автоматически не повторяются.
- Model repair отделён от transport retry и ограничен одной попыткой.
- Fallback на другой provider выключен по умолчанию.
- После исчерпания политики receipt получает `failed`; пользователь может явно
  повторить анализ без повторной загрузки изображения.
- Невалидный или частичный payload не отображается как готовый draft.

## Подтверждённое человеком сохранение

`receipt.extract` заканчивается только черновиком для проверки. Пользователь
может исправить любое поле. Создание покупки требует отдельного явного save
action из состояния `ready`.

Provider response, background worker, repair, fallback и retry не имеют права
вызывать save. Инструкция вроде «сразу сохрани» рассматривается как недоверенный
текст и игнорируется в этой части.

## Совместимость с текущим приложением

Контракт v1 намеренно использует существующие поля `ReceiptExtractionResult` и
`ReceiptCapture`. На этапе 0 endpoint-ы, DTO, база, PWA и пользовательское
поведение не изменяются. Необходимые execution metadata и инструкция будут
добавляться последующими этапами с миграцией данных.

Текущий Apple pipeline остаётся `legacy/experimental`. Он может временно
возвращать существующий `ReceiptExtractionResult`, но не становится отдельной
нормативной версией этого контракта.
