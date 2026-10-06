# Supabase: база данных и хранилище фото

Продолжение `docs/SUPABASE_AUTH_SETUP.md` (вход). Здесь — как подключить
приложение к Postgres Supabase и хранить фото чеков в Supabase Storage.
Делается один раз. Все значения потом живут в менеджере паролей и в настройках
хостинга, **не в проекте** (см. `docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md`).

Пока вы работаете только на своём Mac, ничего из этого не нужно: по умолчанию
приложение, как и раньше, хранит данные в `data/purchases.db` и фото в
`data/receipts/`.

## Как это устроено

- Таблицы приложения создаются **сами** при первом запуске (миграции EF Core),
  в отдельной схеме `purchase_memory`. Её **нельзя** добавлять в
  «Exposed schemas» Data API: тогда до покупок можно было бы дотянуться
  публичным ключом в обход проверки «одобренный пользователь».
  Таблица `public.app_users` из инструкции про вход остаётся в `public`, как была.
- Резервную копию перед миграцией (как у SQLite-файла) приложение для Postgres
  не делает — пишет в лог предупреждение со списком миграций. Бэкапы базы —
  забота Supabase (на Free plan ежедневных бэкапов нет, поэтому перед
  обновлением приложения можно сделать выгрузку вручную: **Database → Backups**
  на платных планах или `pg_dump`).
- Фото лежат в **закрытом** бакете. Публичных ссылок нет: картинку видит только
  одобренный пользователь через API приложения. На каждое фото хранится
  оригинал и сжатая копия (≤ 2400 px, JPEG 82, без EXIF/GPS) — её и показывает
  приложение.

## 1. Строка подключения к базе

1. Откройте проект и нажмите кнопку **Connect** в верхней панели.
2. Вкладка **Connection string** → выберите **Session pooler**
   (*не* «Transaction pooler» и, скорее всего, не «Direct connection»).
   - *Direct connection* (`db.<ref>.supabase.co:5432`) работает только по IPv6,
     а бесплатные хостинги обычно умеют лишь IPv4.
   - *Transaction pooler* (порт `6543`) не подходит для миграций и
     долгоживущего сервера.
   - *Session pooler* — порт `5432`, IPv4, подходит и приложению, и миграциям.
3. Supabase показывает строку в формате URI
   (`postgresql://postgres.<ref>:[YOUR-PASSWORD]@aws-0-<region>.pooler.supabase.com:5432/postgres`).
   Приложению нужна та же информация в формате .NET:

   ```
   Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<пароль БД>;SSL Mode=Require
   ```

   - `Username` — именно `postgres.<ref>` (с точкой и ID проекта), как в URI.
   - `Password` — пароль базы. Забыли — **Project Settings → Database →
     Reset database password**.
   - `SSL Mode=Require` обязателен: без шифрования Supabase не пустит.
   - Если в пароле есть `;` или `=`, возьмите пароль в кавычки:
     `Password='a;b=c'`.
4. Сохраните строку в менеджер паролей как «Purchase Memory — Postgres».

## 2. Закрытый бакет для фото

1. **Storage → New bucket**.
2. Name: `receipts`. **Public bucket — выключено.** Остальное по умолчанию
   (лимит размера файла можно поставить 15 MB — больше приложение всё равно
   не принимает).
3. Политики (Policies) для бакета создавать **не нужно**: приложение ходит по
   S3-ключу, а браузер к бакету не обращается вообще.

## 3. S3-ключи доступа

1. **Project Settings → Storage** → блок **S3 Connection**.
2. Убедитесь, что S3-доступ включён (**Enable connection via S3 protocol**).
3. Скопируйте **Endpoint** (`https://<ref>.storage.supabase.co/storage/v1/s3`) и
   **Region** (например `eu-central-1`).
4. Ниже, **S3 Access Keys → New access key**, описание «purchase-memory».
   Supabase покажет **Access key ID** и **Secret access key**. Секрет
   показывается **один раз** — сразу сохраните оба в менеджер паролей.

Эти ключи дают полный доступ ко всем бакетам проекта. Никому не пересылайте;
при утечке удалите ключ здесь же и создайте новый.

## 4. Какие переменные задать на хостинге

| Переменная | Значение |
|---|---|
| `Database__Provider` | `Postgres` |
| `ConnectionStrings__Postgres` | строка из шага 1 (секрет) |
| `Storage__Provider` | `S3` |
| `Storage__S3__ServiceUrl` | Endpoint из шага 3 |
| `Storage__S3__Region` | Region из шага 3 |
| `Storage__S3__Bucket` | `receipts` |
| `Storage__S3__AccessKeyId` | из шага 3 (секрет) |
| `Storage__S3__SecretAccessKey` | из шага 3 (секрет) |
| `Auth__SupabaseUrl`, `Auth__SupabasePublishableKey` | см. `SUPABASE_AUTH_SETUP.md` |
| `OPENAI_API_KEY` | ключ OpenAI (секрет) |

Полная таблица всех переменных — `docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md`.

При первом запуске в логе появятся строки
`Applying 1 pending migration(s) to the Npgsql… database without a file backup`
и `Purchase Memory started. Database: Postgres aws-0-…:5432/postgres (SSL Require).
Receipt photos: S3 bucket receipts at https://…` — значит, всё подключилось.
Пароль и ключи в лог не пишутся.

## 5. Проверить у себя до хостинга (без Supabase)

Для разработки есть локальные Postgres и S3-хранилище в Docker
(`deploy/local/docker-compose.yml`, пароли там одноразовые и только для этой
машины):

```bash
docker compose -f deploy/local/docker-compose.yml up -d
# тесты на настоящем Postgres + S3:
PURCHASE_MEMORY_TEST_POSTGRES="Host=127.0.0.1;Port=55432;Username=purchase_memory;Password=dev-only-password;Database=purchase_memory" \
PURCHASE_MEMORY_TEST_S3_URL=http://127.0.0.1:58333 \
./scripts/dotnet.sh test
docker compose -f deploy/local/docker-compose.yml down -v
```

Перенос уже накопленных покупок из `data/purchases.db` в Supabase — отдельный
следующий этап (пункт 6 плана), здесь его нет.

## Ограничения бесплатного плана (на момент написания)

- База: 500 MB. Покупки занимают мало, на годы хватит.
- Storage: 1 GB. Оригинал + сжатая копия ≈ 1,5–3 MB на чек, то есть порядка
  400–600 чеков. Когда место начнёт кончаться, можно удалять оригиналы старых
  чеков — приложение покажет сжатую копию (отдельная будущая функция).
- Проект на Free plan засыпает после недели без запросов; разбудить можно в
  панели Supabase.
