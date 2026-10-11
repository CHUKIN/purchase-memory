# Деплой на Render: настройки и обновление

Дата: 06.10.2026. Как устроен образ, какие переменные вписать в Render и как
выложить новую версию. Общий план и шаги владельца: `docs/ДЕПЛОЙ_ЧТО_НУЖНО_ОТ_ВАС.md`.

## Сервис

| Настройка | Значение |
|---|---|
| Тип | Web Service, **Existing image** |
| Образ | `ghcr.io/chukin/purchase-memory:latest` (закрытый пакет GHCR) |
| Доступ к образу | Registry Credential `ghcr-chukin` (токен GitHub только с `read:packages`) |
| Регион | Frankfurt |
| Тариф | Free (512 МБ, 0.1 CPU) |
| Порт | `10000` (Render передаёт его в `PORT`, приложение слушает `0.0.0.0:$PORT`) |
| Health Check Path | `/health` (без входа, отвечает `{"status":"healthy"}`, больше ничего не раскрывает) |

`/health` отвечает 200 только после того, как миграции базы применены: приложение
начинает слушать порт уже после них.

## Переменные окружения

### Несекретные (агент может вписать сам)

| Переменная | Значение |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` (так и по умолчанию; задаём явно для ясности) |
| `PORT` | `10000` (Render ставит сам; можно задать явно) |
| `ReverseProxy__TrustForwardedHeaders` | `true` |
| `Database__Provider` | `Postgres` |
| `Storage__Provider` | `S3` |
| `Storage__S3__ServiceUrl` | `https://nycrbyergcbadxxlyyoj.storage.supabase.co/storage/v1/s3` |
| `Storage__S3__Region` | `eu-west-2` |
| `Storage__S3__Bucket` | `receipts` |
| `Auth__SupabaseUrl` | `https://nycrbyergcbadxxlyyoj.supabase.co` |
| `Auth__SupabasePublishableKey` | `sb_publishable_K8AVpYIpXz4iwpHOG1bTZA_xfxvtKDd` |

Кнопки входа берутся из `appsettings.json` (`email`, `google`). Распознавание
(`anthropic`, модель `claude-haiku-5-5`; запасной путь `openai`, модель `gpt-6-sol`),
подсказки категорий и вопросы в «Аналитике» тоже уже настроены в
`appsettings.json`, вписывать их не нужно.

**Никогда не задавать на Render:** `Auth__DevelopmentBypass` (вне Development
приложение с ним не запустится), `ReceiptRecognition__BaseAddress` и прочие
локальные настройки Ollama.

### Секретные (вписывает владелец, в чат не присылать)

| Переменная | Откуда |
|---|---|
| `ANTHROPIC_API_KEY` | console.anthropic.com → API keys (основное распознавание; без него распознаёт OpenAI) |
| `OPENAI_API_KEY` | platform.openai.com → API keys |
| `ConnectionStrings__Postgres` | Supabase → Connect → **Session pooler** |
| `Storage__S3__AccessKeyId` | Supabase → Storage → S3 access keys |
| `Storage__S3__SecretAccessKey` | там же, показывается один раз |

Имена совпадают с тем, что читает код: ключи — именно переменные
`ANTHROPIC_API_KEY` и `OPENAI_API_KEY` (не `OpenAI__ApiKey`).

## Почему `ReverseProxy__TrustForwardedHeaders=true`

Render принимает HTTPS и передаёт запрос в контейнер по HTTP, дописывая настоящий
адрес клиента и схему в `X-Forwarded-For` / `X-Forwarded-Proto`. С этой
настройкой приложение берёт из этих заголовков **только последнюю запись** — ту,
что дописал сам Render; всё, что клиент подставил раньше, игнорируется. Адрес
прокси Render не публикует, поэтому список доверенных прокси не задаётся — это
безопасно только потому, что контейнер доступен лишь через прокси Render.

На защиту это не влияет: лимиты загрузок и вопросов считаются по пользователю,
а не по IP, и вход без Supabase-токена невозможен. Вместе с
`Auth__DevelopmentBypass` эта настройка запрещена: приложение не запустится.
Локально (Mac, `compose.yaml`) она выключена.

## Как выложить новую версию

```bash
./scripts/publish-image.sh
```

Скрипт:
1. логинится в `ghcr.io` токеном из `gh` (токен не печатается);
2. собирает образ `linux/amd64` (`docker buildx`, сборка идёт без эмуляции, только
   итоговый образ под amd64);
3. выкладывает `ghcr.io/chukin/purchase-memory:<ГГГГММДД-ЧЧММ по UTC>` и `:latest`;
4. печатает digest;
5. при любом завершении — успех, ошибка или Ctrl+C — делает `docker logout ghcr.io`,
   чтобы токен с правом записи не оставался в хранилище учётных данных Docker.
   Если вы до запуска уже были залогинены в `ghcr.io`, после скрипта этот вход
   тоже пропадёт; для `docker pull` закрытого образа войдите заново.

Затем в Render: сервис → **Manual Deploy → Deploy latest reference** (или
попросить агента сделать это через Render MCP). Пакет остаётся закрытым,
видимость скрипт не меняет.

Нужно один раз: `gh auth refresh -s write:packages,read:packages`.

## Что внутри образа

- Сборка frontend и .NET на платформе сборщика, публикация под `linux/amd64`
  с ReadyToRun (быстрее холодный старт после засыпания).
- Запуск от непривилегированного пользователя `app` (uid 1654); писать можно
  только в `/app/data` и `/app/logs`. На Render диск временный: файловые логи
  пропадают при перезапуске, смотреть логи — в панели Render (консольный вывод).
- `DOTNET_gcServer=0` — workstation GC, см. замеры в `РЕЗУЛЬТАТЫ_ХОСТИНГ.md`.
- Адреса локального Ollama в образе нет; для локального запуска он задан в
  `compose.yaml`.
- В образ не попадают файлы с переменными и секретами: `.dockerignore`
  исключает `.env`, `*.env` и `.env.*` в любой папке (проверено пробной сборкой:
  в контексте сборки таких файлов нет). Самой сборке они не нужны.

## Шум в логах от проверки распознавания

Каждое открытие приложения запрашивает `/api/recognition/status`. Раньше проверка
опрашивала все пути распознавания сразу, включая запасной Ollama
(`127.0.0.1:11434`), которого в облаке нет, и фабрика HTTP-клиентов писала в лог
полный стек ошибки соединения. Теперь пути проверяются по порядку и проверка
останавливается на первом доступном: при настроенном ключе OpenAI до Ollama дело
не доходит. Служебные строки фабрики HTTP-клиентов (`System.Net.Http.HttpClient`)
пишутся только с уровня Warning. С ключом Anthropic экран показывает
«Распознавание доступно» и режим `anthropic · fallback: openai, ollama`; без него —
`openai fallback`.

## Локальный запуск через `compose.yaml`: база в Docker-томе, не в `./data`

Настоящая база владельца лежит в `./data` и используется только
`./scripts/run-local.sh` (приложение без Docker). `compose.yaml` её не трогает:
база и логи контейнера живут в Docker-томах `purchase-memory-data` и
`purchase-memory-logs`. Docker сам отдаёт новый том пользователю образа `app`
(uid 1654), настраивать права не нужно. Логи контейнера: `docker compose logs`.
Удалить данные контейнера вместе с томами: `docker compose down -v`.

Почему не `./data`. Проверено на этом Mac (Docker Desktop, папки с Mac
передаются через virtiofs) с папкой из временного каталога вместо `./data`:

- обычные файлы в смонтированной папке пользователь 1654 создаёт и
  перезаписывает без ошибок, логи пишутся;
- SQLite в смонтированной папке падает при старте с `attempt to write a readonly
  database`. Точно так же падает и от `root`, и в постороннем образе с Python
  `sqlite3`: первая запись проходит, вторая — нет, если база в обычном режиме
  журнала (DELETE/TRUNCATE/PERSIST). В режиме WAL и в Docker-томе всё работает.
  То есть дело не во владельце папки, а в том, как Docker Desktop на macOS
  отдаёт SQLite файлы с Mac. С настоящей базой это значило бы миграцию,
  оборванную на середине.

Если на Linux всё же нужна папка с хоста вместо тома (там этой прослойки нет),
не используйте `./data` с настоящей базой и выберите один из вариантов:

- отдать папку пользователю образа:
  `mkdir -p pm-data && sudo chown -R 1654:1654 pm-data`, затем в `compose.yaml`
  `- ./pm-data:/app/data`;
- или запускать контейнер от своего пользователя: добавить в сервис
  `user: "${HOST_UID}:${HOST_GID}"` и запускать
  `HOST_UID=$(id -u) HOST_GID=$(id -g) docker compose up -d`.
  Готовые `$UID`/`$GID` не подходят: bash и zsh их не экспортируют, а `UID`
  ещё и только для чтения, так что `UID=… docker compose up` не сработает.
