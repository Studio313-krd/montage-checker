# Развёртывание MontageMonitor

Production-стенд разворачивается из Git-репозитория как Portainer Stack. Внешний трафик принимает только Caddy на портах 80/443; ASP.NET Core и PostgreSQL доступны исключительно во внутренней Docker-сети.

## 1. DNS и VPS

1. Создайте `A`-запись домена, например `monitor.example.com`, на публичный IPv4 VPS. Создавайте `AAAA` только при рабочем IPv6 и корректной маршрутизации.
2. Дождитесь обновления DNS и проверьте имя из внешней сети.
3. Установите на актуальную Ubuntu LTS Docker Engine, Docker Compose plugin и Portainer.
4. Разрешите во входящем firewall только `80/tcp`, `443/tcp` и `443/udp` для приложения. SSH ограничьте доверенными адресами или VPN.
5. Не публикуйте наружу порты PostgreSQL `5432` и Server `8080`.

Caddy автоматически получает и продлевает TLS-сертификат, если домен указывает на VPS, а 80/443 доступны из интернета. Его сертификаты и служебные данные сохраняются в named volumes.

## 2. Git-репозиторий

Production-версия должна находиться в отдельной ветке или release tag. Репозиторий содержит `docker-compose.yml`, Dockerfile и исходный код, но не production-секреты и не `.env`.

Для private repository добавьте в Portainer Git credential с минимальными правами чтения. Не помещайте personal access token в URL репозитория. Git submodules не используются: Portainer не загружает их при Git deployment.

## 3. Переменные окружения

Скопируйте перечень из [.env.example](.env.example), но задайте реальные значения в разделе **Environment variables** стека Portainer.

Обязательные значения:

| Переменная | Назначение |
| --- | --- |
| `MONITOR_DOMAIN` | Публичное DNS-имя без `https://` и завершающего `/` |
| `ACME_EMAIL` | Адрес для уведомлений центра сертификации |
| `POSTGRES_DB` | Имя базы, обычно `montage_monitor` |
| `POSTGRES_USER` | Внутренний пользователь PostgreSQL |
| `POSTGRES_PASSWORD` | Длинный случайный пароль базы |
| `JWT_SIGNING_KEY` | Случайный ключ подписи, не менее 32 байт |
| `OWNER_LOGIN` | Логин первого владельца |
| `OWNER_PASSWORD` | Временный пароль первого владельца |
| `OWNER_DISPLAY_NAME` | Отображаемое имя владельца |

Пароли можно создать на доверенной машине командой `openssl rand -base64 48`. Не используйте пробелы, `;` и перевод строки в `POSTGRES_PASSWORD`, потому что Compose формирует из него строку подключения. `OWNER_PASSWORD` должен иметь не менее 12 символов, заглавную и строчную буквы, цифру и специальный символ.

Эксплуатационные настройки:

| Переменная | Значение по умолчанию | Назначение |
| --- | ---: | --- |
| `APP_VERSION` | `latest` | Локальный tag собираемых образов |
| `COMPANY_TIME_ZONE` | `Europe/Moscow` | Часовой пояс отчётов |
| `DAILY_OPERATOR_SELECTION_HOUR` | `6` | Локальный час обязательного ежедневного выбора монтажёра |
| `SCREENSHOT_MAX_UPLOAD_BYTES` | `8388608` | Максимальный размер JPEG, байт |
| `BACKUP_RETENTION_DAYS` | `14` | Срок хранения архивов |
| `BACKUP_INTERVAL_SECONDS` | `86400` | Интервал между архивами, 24 часа |
| `BACKUP_RETRY_SECONDS` | `300` | Повтор после ошибки |
| `BACKUP_HEALTH_GRACE_SECONDS` | `1800` | Допустимая задержка backup healthcheck |
| `BACKUP_ON_START` | `true` | Создать архив сразу после запуска |
| `BACKUP_SCREENSHOTS` | `false` | Дополнительно архивировать screenshots |

`JWT_ISSUER` и `JWT_AUDIENCE` обычно оставляют как в `.env.example`. Для разных production-инсталляций используйте разные секреты.

## 4. Установка через Portainer

### Вариант A: собственный Caddy на свободных 80/443

В Portainer для Docker Standalone:

1. Откройте **Stacks → Add stack** и задайте имя `montage-monitor`.
2. Выберите **Git repository**.
3. Укажите Repository URL, credential для private repository и production branch/tag в Repository reference.
4. Укажите Compose path: `docker-compose.yml`.
5. Добавьте переменные из предыдущего раздела вручную либо загрузите подготовленный `.env` через **Load variables from .env file**.
6. Нажмите **Deploy the stack**.

Portainer клонирует весь репозиторий и собирает три локальных образа: Server, backup utility и Caddy. Относительные bind mounts не применяются; конфигурация Caddy включена в образ, а постоянные данные размещены в named volumes.

Порядок готовности контролируется healthcheck-зависимостями:

1. `postgres` запускается и принимает подключения;
2. `server` применяет EF Core migrations, создаёт первого OWNER и открывает HTTP listener;
3. `backup` создаёт первый проверяемый архив;
4. `caddy` публикует HTTPS.

В production держите одну реплику `server`: migrations выполняются при старте приложения.

### Вариант B: Portainer за существующим reverse proxy

Если порты `80/443` уже заняты Nginx, OpenResty или другой общей прокси, используйте Compose path `docker-compose.portainer.yml`. Этот вариант не запускает Caddy, публикует только Server на `${APP_HTTP_PORT:-9999}` и оставляет PostgreSQL во внутренней Docker-сети.

В существующей прокси задайте upstream `http://127.0.0.1:9999` либо адрес Docker-host, доступный самой прокси. Прокси должна завершать TLS для публичного домена и передавать `Host`, `X-Forwarded-For` и `X-Forwarded-Proto`. После запуска проверьте, что `https://<домен>/health` возвращает `Healthy`, а прямой HTTP-порт ограничьте firewall или доступом только со стороны reverse proxy, если он не нужен извне.

Для этого варианта в Portainer обязательны `APP_HTTP_PORT`, `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `OWNER_LOGIN`, `OWNER_PASSWORD` и `OWNER_DISPLAY_NAME`. `MONITOR_DOMAIN` и `ACME_EMAIL` использует только вариант с Caddy.

## 5. Проверка первого запуска

В Portainer дождитесь статуса `healthy` у `postgres`, `server`, `backup` и `caddy`. Затем проверьте:

```text
https://monitor.example.com/health
https://monitor.example.com/
```

Интерфейс и пользовательские сообщения должны быть на русском. Войдите под первым OWNER. Bootstrap повторно не меняет уже созданного владельца и не выводит его пароль в лог.

После успешного входа:

1. очистите `OWNER_PASSWORD` в переменных стека;
2. выполните **Pull and redeploy**;
3. повторно проверьте health и вход.

Если база будет случайно создана заново, пустой `OWNER_PASSWORD` остановит Server с понятной ошибкой вместо неявного создания учётной записи.

## 6. Миграции

`Database__ApplyMigrationsOnStartup=true` задано в Compose. Server ожидает healthy PostgreSQL и применяет все отсутствующие EF Core migrations до готовности HTTP. Уже применённые migrations пропускаются.

Перед обновлением всегда создавайте ручной архив. Не запускайте одновременно несколько экземпляров Server, пока migration startup не заменён отдельной release-задачей.

При переходе на Agent `0.10.0` сначала обновите Server/SPA через **Pull and redeploy** и убедитесь, что в **Управление → Сотрудники** видны логины и пароли Agent. Только после этого заменяйте EXE на рабочих ПК. Старые Agent `0.9.1` продолжают отправлять данные по действующему device credential, поэтому сервер можно обновить заранее без остановки учёта. Пошаговая замена уже установленных агентов описана в [OPERATOR_SELECTION.md](OPERATOR_SELECTION.md).

## 7. Резервное копирование

Постоянные данные Compose:

| Volume | Содержимое |
| --- | --- |
| `postgres_data` | PostgreSQL 18 data directory |
| `screenshots_data` | JPEG-файлы скриншотов |
| `backups` | Проверенные database и optional screenshot archives |
| `data_protection_keys` | Ключи ASP.NET Core Data Protection |
| `caddy_data`, `caddy_config` | TLS-сертификаты и состояние Caddy |

Контейнер `backup` сразу после готовности Server и затем каждые `BACKUP_INTERVAL_SECONDS` выполняет:

- `pg_dump` в custom format с Zstandard compression;
- проверку архива через `pg_restore --list`;
- атомарное перемещение готового файла в `/backups/postgres`;
- удаление архивов старше `BACKUP_RETENTION_DAYS`;
- обновление health marker только после успешной проверки.

Имя файла имеет вид `montage_monitor_YYYYMMDDTHHMMSSZ.dump`. Ошибка не заменяет последний корректный архив; операция повторяется через `BACKUP_RETRY_SECONDS`.

Ручной архив перед обновлением:

```bash
docker compose --env-file .env exec backup /usr/local/bin/montage-backup --once
```

Список архивов:

```bash
docker compose --env-file .env exec backup \
  find /backups/postgres -maxdepth 1 -type f -name 'montage_monitor_*.dump'
```

В Portainer те же команды можно выполнить через console контейнера `backup`, опустив `docker compose ... exec backup`.

Named volume `backups` на том же VPS защищает от ошибки обновления, но не от потери VPS или диска. Регулярно копируйте его зашифрованным способом во внешнее хранилище и периодически проверяйте восстановление на отдельном стенде.

### Скриншоты и ключи

`pg_dump` содержит metadata, но не JPEG-файлы. При `BACKUP_SCREENSHOTS=true` backup дополнительно создаёт `screenshots_YYYYMMDDTHHMMSSZ.tar.gz`; это может значительно увеличить место и длительность операции. Для больших объёмов предпочтительнее snapshot/репликация volume `screenshots_data` с тем же графиком хранения.

Для полного аварийного восстановления отдельно сохраняйте:

- архив PostgreSQL;
- `screenshots_data`;
- `data_protection_keys`;
- значения production secrets.

Ключи Data Protection хранятся в Docker volume и защищены доступом к Docker host, но не отдельным ключом шифрования внутри volume. Ограничьте доступ администраторов VPS и резервным копиям. Сертификаты Caddy можно получить заново, поэтому `caddy_data` не является обязательной бизнес-копией.

## 8. Восстановление PostgreSQL

Восстановление полностью заменяет целевую базу и принудительно завершает её активные подключения. Выполняйте его только после проверки имени архива и наличия внешней копии текущих данных.

1. Поместите проверенный `montage_monitor_*.dump` в `/backups/postgres` volume.
2. Остановите процессы, работающие с базой:

   ```bash
   docker compose --env-file .env stop server backup
   ```

3. Задайте имя файла и точную подтверждающую фразу:

   ```bash
   export RESTORE_FILE=montage_monitor_20260907T061540Z.dump
   export RESTORE_CONFIRM=ERASE_AND_RESTORE
   docker compose --env-file .env run --rm restore
   unset RESTORE_FILE RESTORE_CONFIRM
   ```

   Сервис `restore` находится в profile `tools`; явный `docker compose run restore` включает его без постоянного запуска profile. Без точной фразы операция завершается до изменения базы.

4. Запустите приложение и backup:

   ```bash
   docker compose --env-file .env up -d server backup
   docker compose --env-file .env ps
   ```

5. Проверьте `healthy`, вход OWNER, dashboard, отчёты и несколько последних событий.

Для Portainer остановите `server` и `backup`, откройте console/host shell и выполните эквивалентные Compose-команды в рабочем каталоге stack. После восстановления снова запустите контейнеры. Восстановление JPEG выполняется отдельно в `screenshots_data`; структура путей должна совпадать с metadata из того же момента времени.

## 9. Обновление и откат

1. Зафиксируйте проверенную версию в Git и обновите production branch/tag.
2. Выполните ручной backup и убедитесь, что контейнер `backup` остаётся healthy.
3. В Portainer откройте stack и нажмите **Pull and redeploy** с повторной сборкой образов.
4. Дождитесь health всех сервисов и проверьте `/health`, вход и dashboard.

Можно включить Git polling или webhook Portainer, но для production безопаснее сначала создавать backup и осознанно подтверждать развёртывание. Compose-файл Git stack редактируется в репозитории, а deployment-specific переменные — в Portainer.

Откат приложения выполняйте на предыдущий проверенный Git tag. Если новая версия уже применила несовместимую migration, одного отката образа недостаточно: остановите Server и восстановите сделанный перед обновлением dump.

## 10. Логи и диагностика

Server и backup пишут структурированные JSON-сообщения в stdout. Caddy пишет JSON access log. Docker logging driver `local` ограничен пятью файлами по 10 MiB на контейнер.

```bash
docker compose --env-file .env ps
docker compose --env-file .env logs --tail=200 server backup caddy postgres
docker compose --env-file .env logs -f server caddy
```

Типовые причины проблем:

- `caddy` не получает сертификат: проверьте A/AAAA, внешний доступ к 80/443 и отсутствие другого listener на этих портах;
- `server` unhealthy: сначала проверьте PostgreSQL, migration error и обязательные secrets;
- `backup` unhealthy: проверьте свободное место, соединение с PostgreSQL и возраст `/backups/last-success`;
- после restore нет JPEG: восстановите совместимую копию `screenshots_data`;
- изменён `POSTGRES_PASSWORD` у существующего volume: значение внутри PostgreSQL автоматически не меняется; верните прежний пароль или измените роль штатной SQL-командой.

## 11. Локальная проверка Compose

Для администратора или CI вне Portainer:

```bash
cp .env.example .env
chmod 600 .env
docker compose --env-file .env config --quiet
docker compose --env-file .env up -d --build
docker compose --env-file .env ps
```

Не используйте placeholder-секреты из `.env.example` в доступной из интернета установке.
