# Вход через Supabase Auth: настройка

Эта инструкция для владельца. Код приложения уже готов. Ниже шаги, которые делаются
один раз в панелях Supabase и Google. Ни ключи, ни пароли в репозиторий не попадают:
приложение получает два публичных значения (адрес проекта и publishable key)
из переменных окружения.

База данных и хранилище фото в том же проекте Supabase настраиваются по
отдельной инструкции: `docs/SUPABASE_БАЗА_И_ФОТО.md`. Какие ключи где хранить —
`docs/ПЕРЕМЕННЫЕ_И_СЕКРЕТЫ.md`.

## Как это устроено

- Вход: ссылка из письма (в том же письме есть код, его можно ввести вручную)
  и Google. Список кнопок берётся из настройки `Auth:Providers`.
- При первом входе Supabase создаёт пользователя, а наш триггер добавляет строку
  в таблицу `public.app_users` со статусом `pending`.
- Владелец меняет `status` на `approved` или `blocked` в **Table Editor**. SQL для
  этого не нужен.
- Хук Custom Access Token кладёт статус в токен как claim `app_access`. Сервер
  проверяет подпись токена по публичным ключам проекта (JWKS), issuer, audience
  `authenticated` и срок жизни, затем пускает только `approved`.
- Почему таблица, а не `user_metadata`: `user_metadata` пользователь может менять
  сам. `app_metadata` в панели неудобно редактировать. Таблица редактируется
  мышкой и позже естественно переедет в Postgres Supabase вместе с остальными
  данными.
- Задержка: новый статус попадает в токен при его обновлении. Кнопка
  «Проверить снова» на экране ожидания обновляет токен сразу. Блокировка
  срабатывает при следующем обновлении, то есть не позже срока жизни токена
  (по умолчанию 1 час, см. шаг 7).

## 1. Создать проект

1. Зарегистрируйтесь на https://supabase.com и создайте проект (Free plan подходит).
   Регион выбирайте ближе к себе, например Frankfurt.
2. **Project Settings → API Keys**: скопируйте **Publishable key**
   (`sb_publishable_…`). Секретный ключ (`sb_secret_…` / service_role) приложению
   не нужен, никуда его не вписывайте.
3. **Project Settings → Data API** (или главная страница проекта): скопируйте
   **Project URL** вида `https://abcd1234.supabase.co`.
4. **Project Settings → JWT Keys**: текущий ключ подписи должен быть
   асимметричным (ECC P-256 или RSA). У новых проектов так и есть. Если там
   указан только «Legacy JWT secret», создайте новый standby key и сделайте его
   текущим (Rotate). Сервер не принимает токены, подписанные общим секретом HS256.

## 2. Таблица пользователей, триггер и хук (один раз)

Откройте **SQL Editor → New query**, вставьте скрипт целиком и нажмите **Run**.
Это единственный SQL во всей настройке.

```sql
-- Статус доступа, который владелец меняет в Table Editor.
create table if not exists public.app_users (
  id uuid primary key references auth.users (id) on delete cascade,
  email text,
  status text not null default 'pending'
    check (status in ('pending', 'approved', 'blocked')),
  created_at timestamptz not null default now(),
  note text
);

-- Таблица не читается через публичный API: только хук ниже.
alter table public.app_users enable row level security;
revoke all on public.app_users from anon, authenticated;
grant select on public.app_users to supabase_auth_admin;
create policy "Auth server reads access status"
  on public.app_users as permissive for select
  to supabase_auth_admin using (true);

-- Первый вход: строка со статусом pending.
create or replace function public.handle_new_app_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
begin
  insert into public.app_users (id, email)
  values (new.id, new.email)
  on conflict (id) do nothing;
  return new;
end;
$$;

drop trigger if exists on_auth_user_created_app_user on auth.users;
create trigger on_auth_user_created_app_user
  after insert on auth.users
  for each row execute procedure public.handle_new_app_user();

-- Пользователи, созданные до скрипта (если были).
insert into public.app_users (id, email)
select id, email from auth.users
on conflict (id) do nothing;

-- Хук: кладёт статус в токен как claim app_access.
create or replace function public.custom_access_token_hook(event jsonb)
returns jsonb
language plpgsql
stable
as $$
declare
  claims jsonb;
  access text;
begin
  select status into access
  from public.app_users
  where id = (event ->> 'user_id')::uuid;

  claims := event -> 'claims';
  claims := jsonb_set(claims, '{app_access}', to_jsonb(coalesce(access, 'pending')));
  return jsonb_set(event, '{claims}', claims);
end;
$$;

grant usage on schema public to supabase_auth_admin;
grant execute on function public.custom_access_token_hook to supabase_auth_admin;
revoke execute on function public.custom_access_token_hook from authenticated, anon, public;
```

Затем **Authentication → Auth Hooks → Add hook → Customize Access Token (JWT) Claims**:
тип **Postgres**, схема `public`, функция `custom_access_token_hook`, **Enable**.

Если хук не включён, в токене нет `app_access`. Сервер тогда считает всех
ожидающими и никого не пускает: это сделано специально.

## 3. Вход по ссылке из письма

1. **Authentication → Sign In / Providers → Email** включён по умолчанию.
   Оставьте **Enable Email provider** включённым. Пароли не нужны.
2. **Authentication → Emails → Magic Link**: в шаблоне **обязательно** должна
   быть строка с кодом. Без неё войти в приложение на экране «Домой» iPhone
   нельзя: ссылка из письма всегда открывается в браузере, а у приложения на
   экране «Домой» своё отдельное хранилище, и вход из браузера туда не попадает.
   ```html
   <p>Код для входа: <strong>{{ .Token }}</strong></p>
   ```
   Сделайте то же в шаблоне **Confirm signup** (первое письмо новому человеку).
   Длина кода задаётся в Supabase (от 6 до 10 цифр; сейчас в проекте 8).
   Поле «Код из письма» принимает любую длину от 6 до 10 цифр, менять код
   приложения при смене длины не нужно. Код и ссылка в одном письме — один и тот
   же одноразовый вход: если нажать ссылку, код из этого письма уже не подойдёт.
3. Встроенная почта Supabase отправляет всего несколько писем в час. Для
   домашнего круга этого хватает. Если писем будет не хватать, подключите свой
   SMTP в **Authentication → Emails → SMTP Settings**.

## 4. Google

1. https://console.cloud.google.com → создайте проект (или выберите существующий).
2. **APIs & Services → OAuth consent screen** (Google Auth Platform → Branding):
   тип **External**, название приложения, ваш email. В **Audience** добавьте
   себя и близких как Test users или опубликуйте приложение. В **Data Access**
   хватает стандартных `openid`, `email`, `profile`.
3. **Clients → Create client → Web application**:
   - **Authorized JavaScript origins**: `https://purchase-memory.onrender.com`
     (и для локальной разработки `http://localhost:5099`, `http://localhost:5173`);
   - **Authorized redirect URIs**: `https://nycrbyergcbadxxlyyoj.supabase.co/auth/v1/callback`
     (в общем виде `https://<project-ref>.supabase.co/auth/v1/callback`; точный
     адрес показан в Supabase на странице провайдера Google, поле Callback URL).
     Google возвращает человека в Supabase, а Supabase — в приложение по адресам
     из шага 5.
4. Скопируйте **Client ID** и **Client secret** в Supabase:
   **Authentication → Sign In / Providers → Google → Enable**. Секрет хранится только
   в панели Supabase, в наше приложение он не попадает.

## 5. Адреса возврата

**Authentication → URL Configuration**:

- **Site URL**: `https://purchase-memory.onrender.com`;
- **Redirect URLs** (Add URL):
  - `https://purchase-memory.onrender.com/**` — обязательно, иначе Supabase
    вернёт человека после Google на Site URL без кода или откажет;
  - для локальной разработки: `http://localhost:5099/**`,
    `http://127.0.0.1:5099/**`, `http://localhost:5173/**` (Vite dev server).

Приложение возвращает пользователя на корень текущего адреса (`<origin>/`).

## 5а. iPhone: приложение на экране «Домой»

- У приложения, добавленного на экран «Домой», своё хранилище: вход в Safari
  или Chrome туда не переносится. Каждый человек входит **внутри** приложения
  один раз.
- Внутри приложения экран входа сам предлагает «Получить код на почту» и поле
  «Код из письма». Ссылку из письма там нажимать не нужно — она откроется в
  браузере. Поэтому шаблоны писем из шага 3 должны содержать `{{ .Token }}`.
- Google внутри приложения: iOS открывает страницы Google/Supabase в окне
  поверх приложения, а возврат на `https://purchase-memory.onrender.com/…`
  отдаёт обратно в приложение, где лежит PKCE-ключ (проверено на симуляторе
  iOS с локальной заглушкой; на реальном телефоне с настоящим Google — см.
  `РЕЗУЛЬТАТЫ_IPHONE_И_ВХОД.md`). Если вход не вернулся, приложение так и
  пишет и предлагает войти по коду.
- После входа сессия хранится в приложении и обновляется сама (refresh-токен
  Supabase). Медленный первый ответ спящего сервера Render или пропавшая сеть
  не выкидывают из приложения: показывается «Нет связи с сервером» и кнопка
  «Повторить».
- Чтобы вход держался неделями, в Supabase не включайте ограничения сессии
  (**Authentication → Sessions**: Time-box / Inactivity timeout пустые — так
  по умолчанию).

## 6. Значения для приложения

| Переменная окружения | Значение |
|---|---|
| `Auth__SupabaseUrl` | Project URL, например `https://abcd1234.supabase.co` |
| `Auth__SupabasePublishableKey` | `sb_publishable_…` |
| `Auth__Providers__0`, `Auth__Providers__1`, … | необязательно, по умолчанию `email`, `google`; можно добавить `apple`, `github` после включения в Supabase |

Локальный запуск (из корня репозитория):

```bash
export Auth__SupabaseUrl="https://abcd1234.supabase.co"
export Auth__SupabasePublishableKey="sb_publishable_..."
cd src/PurchaseMemory.Api && ../../scripts/dotnet.sh run --launch-profile http
```

Docker Compose читает `SUPABASE_URL` и `SUPABASE_PUBLISHABLE_KEY` из окружения
или из файла `.env` рядом с `compose.yaml`. Такой `.env` не коммитьте.

Оба значения публичные: браузер получает их с сервера через `/api/auth/config`.
Их всё равно не пишут в `appsettings.json`, чтобы каждое окружение смотрело в свой проект.

## 7. Одобрить или заблокировать человека

1. Человек открывает приложение, входит и видит «Заявка отправлена».
2. Вы открываете **Table Editor → app_users**, находите строку по `email`,
   дважды щёлкаете ячейку `status`, пишете `approved` (или `blocked`) и
   нажимаете **Save**. Неправильное слово таблица не примет.
3. Человек нажимает «Проверить снова». Токен обновляется, и открываются покупки.

### Важно про блокировку

`blocked` (как и возврат в `pending`) **не выкидывает человека мгновенно**.
Уже выданный токен остаётся действительным до конца срока. По умолчанию это
до 1 часа, после чего supabase-js обновит токен и получит новый статус. В
течение этого времени у человека остаётся доступ.

Чтобы закрыть доступ сразу:

1. Поставьте `status = blocked` в `app_users`, иначе человек просто войдёт снова.
2. **Authentication → Users** → строка пользователя → меню **⋯** →
   **Sign out user**. Это отзывает refresh-токены и все сессии, и новый токен
   получить уже нельзя. Можно выбрать **Delete user**: строка в `app_users`
   удалится каскадом, а при новом входе появится снова как `pending`.
3. Токен, который уже на руках, отзыв сессии не отменяет: наш сервер проверяет
   только подпись и срок, в Supabase не ходит. Поэтому остаток доступа не больше
   срока жизни токена. Сократите его: **Project Settings → JWT Keys** (раньше
   Authentication → Settings) → **Access token expiry**, например `600` секунд.
   Слишком маленькие значения (меньше 5 минут) заставят приложение чаще
   обновлять токен, но работать оно будет.

## Пока проекта Supabase нет: локальный режим без входа

Без `Auth__SupabaseUrl` приложение закрыто: API и MCP отвечают 401, а PWA
показывает «Вход ещё не настроен». Чтобы пользоваться приложением на своём
компьютере до создания проекта:

```bash
export Auth__DevelopmentBypass=true
cd src/PurchaseMemory.Api && ../../scripts/dotnet.sh run --launch-profile http
```

Флаг работает только если выполнены все условия:

- окружение `Development` (профиль `http` из `launchSettings.json`);
- TCP-соединение пришло с loopback и `Host` указывает на `localhost`,
  `127.0.0.1` или `[::1]`;
- у запроса нет заголовков `Forwarded` и `Via`, а все адреса в `X-Forwarded-For`
  loopback. Vite-прокси настроен с `xfwd: true` и **дописывает** в
  `X-Forwarded-For` настоящий адрес клиента. Устройство в сети может подделать
  свои заголовки, но убрать адрес, который дописал прокси, не может. Поэтому
  через `http://<LAN-IP>:5173` bypass не срабатывает, а через
  `http://localhost:5173` срабатывает. Тест в `src/auth/viteProxy.test.ts` не
  даёт случайно убрать `xfwd`.

**Не открывайте API наружу при включённом bypass.** Туннели и прокси, которые не добавляют `X-Forwarded-For`/`Forwarded`/`Via` (ngrok, TCP-туннель к `127.0.0.1:5099` и т.п.), выглядят для приложения как loopback — любой снаружи получит полный доступ.

С телефона по сети, через LAN-адрес Vite-прокси и в Docker флаг не действует.
В любом окружении, кроме Development, приложение с этим флагом не запустится.
При старте в лог пишется предупреждение. Все действия записываются в аудит
от имени `local-development-user` / `dev@localhost`.

## Что защищено

| Адрес | Доступ |
|---|---|
| `/`, `/assets/*`, страницы SPA, `index.html` | все (там нет данных, это экран входа) |
| `/health` | все, ответ только `{"status":"healthy"}` |
| `/api/auth/config` | все: адрес проекта, publishable key, список провайдеров |
| `/api/me` | любой вошедший (нужно, чтобы показать «ожидайте» или «заблокирован») |
| остальные `/api/*`, включая `/api/recognition/status` и фото чеков | только `approved` |
| `/mcp` | только `approved` (Bearer-токен) |

Загрузка чеков (`POST /api/receipts`) и повторное распознавание
(`POST /api/receipts/{id}/process`) ограничены для каждого пользователя:
до 10 подряд, затем 1 раз в 3 минуты (около 20 в час). Лимиты задаются в
`RateLimiting:ReceiptUploads`. При превышении сервер отвечает 429 с
`Retry-After`, а PWA пишет, через сколько минут можно повторить.
