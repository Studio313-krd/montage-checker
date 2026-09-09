# Архитектура MontageMonitor

## Контекст и цели

MontageMonitor рассчитан на 4–50 Windows-агентов и один небольшой VPS. Главный архитектурный принцип — отдельно хранить человеческое состояние и фоновую машинную работу:

- `HumanState`: Active, Idle, Locked, Offline;
- `MachineState`: Normal, Render, Proxy, BackgroundProcessing.

Так заблокированный экран не прекращает реальный render, а открытый без нагрузки Premiere Pro не считается производственной операцией.

## Контейнеры и процессы

```text
Windows 10/11                         VPS / Docker Compose
┌────────────────────┐      HTTPS     ┌──────────────────────────┐
│ MontageMonitor.Agent├──────────────►│ Caddy :443               │
│ WinForms tray       │               └────────────┬─────────────┘
│ SQLite outbox        │                            │ HTTP/internal
└────────────────────┘               ┌────────────▼─────────────┐
                                     │ MontageMonitor.Server    │
Browser ─────────────────────────────►│ API + React static files │
                                     └───────┬──────────┬───────┘
                                             │          │ files
                                      ┌──────▼─────┐ ┌──▼─────────────┐
                                      │ PostgreSQL │ │ /data/screenshots│
                                      └──────┬─────┘ └────────┬─────────┘
                                             │ pg_dump         │ optional tar
                                      ┌──────▼─────────────────▼────────┐
                                      │ Backup / restore → /backups     │
                                      └─────────────────────────────────┘
```

## Границы solution

### MontageMonitor.Agent

Windows-only процесс интерактивной пользовательской сессии. WinForms выбран ради встроенного `NotifyIcon` и минимального количества зависимостей. Агент не является Windows Service: это позволяет корректно показывать статус и позднее получать снимки экранов пользователя.

Пользователь получает обычный self-contained GUI `.exe`: без консольного окна и без необходимости выполнять команды. Первый запуск регистрирует задачу Windows Task Scheduler с trigger «при входе пользователя» и restart-on-failure. Физический ПК автоматически регистрируется при входе по логину и четырёхзначному паролю, а ежедневная operator session описывает сотрудника, фактически работающего на нём. После 06:00 по часовому поясу компании Agent блокирует сбор полноэкранным окном логина и пароля. Штатный выход после проверки служебного пароля или через **Я НЕ МОНТАЖЕР** завершается с кодом `0` и не перезапускается; crash/аварийное завершение получает ненулевой код и запускается снова.

Модули физически разделены по каталогам `Activity`, `Capture`, `Configuration`, `Idle`, `Metrics`, `Processes`, `Proxy`, `Rendering`, `Screenshots`, `Security`, `Storage`, `Sync`. В RenderDetector и ProxyDetector Windows process API, файловый монитор и необязательный GPU provider скрыты за интерфейсами; обе scoring/state machine не зависят от Windows API и тестируются на fake telemetry. Арбитр выбирает Render для явного файла из Render-папки, иначе подтверждённый Proxy имеет приоритет над Render, определённым только по нагрузке encoder.

### MontageMonitor.Server

Модульный монолит ASP.NET Core. Прикладные возможности будут сгруппированы вертикальными feature-модулями внутри одного процесса; EF Core infrastructure останется общей. Это проще в эксплуатации, чем микросервисы, и достаточно для заданной нагрузки.

### MontageMonitor.Web

React SPA работает через Vite в development. Весь пользовательский интерфейс и системные сообщения — на русском; технические enum/идентификаторы локализуются на UI boundary. Production Dockerfile собирает SPA и помещает `dist` в `wwwroot` publish-каталога ASP.NET Core. Поэтому Caddy проксирует один application-контейнер, CORS между UI и API не нужен.

### MontageMonitor.Shared

Содержит только wire-контракты и общие enums для Agent ↔ Server. Здесь не будет EF Core entities, серверной бизнес-логики или Windows-specific API. Контракты имеют версию схемы; изменения должны оставаться обратно совместимыми для агентов, обновляемых вручную.

## Потоки данных (целевое состояние MVP)

1. Agent обращается к фиксированному production URL, проверяет логин и четырёхзначный пароль сотрудника, регистрирует или повторно использует физический компьютер и получает device-specific credentials.
2. Вход создаёт operator session до следующего 06:00; список активных сотрудников не раскрывается Agent.
3. Agent локально агрегирует события и сохраняет их в SQLite outbox вместе с ID operator session.
4. Каждые 30–60 секунд Agent отправляет небольшую idempotent batch с UUID событий.
5. Server проверяет, что время события попадает в подтверждённую operator session, дедуплицирует и сохраняет событие с фактическим `employee_id`, затем строит интервальные sessions.
6. Скриншоты загружаются отдельно; в PostgreSQL хранится только metadata и безопасный относительный путь.
7. Web опрашивает dashboard раз в 10–15 секунд.

## Данные и время

- В PostgreSQL все timestamps хранятся в UTC (`timestamptz`).
- На границах .NET используется `DateTimeOffset`.
- UI и Excel конвертируют время в настроенный часовой пояс компании.
- Сырые повторы telemetry не сохраняются каждую секунду; основная модель — события и интервалы.
- `activity_events.event_id` является idempotency key; повторная batch-загрузка не создаёт строку-дубликат.
- PostgreSQL хранит screenshot metadata и относительный путь, сами изображения находятся в named volume.

При одном server-instance migrations применяются на старте под флагом `Database:ApplyMigrationsOnStartup`. Горизонтальное масштабирование Server потребует отдельной migration-задачи и раздельных schema/runtime database identities.

## Безопасность и приватность

- Наружу публикуются только Caddy 80/443; PostgreSQL и ASP.NET Core находятся во внутренней Docker network.
- Первичный вход по логину и четырёхзначному паролю ограничен пятью попытками в минуту по IP; последующая аутентификация привязана к устройству.
- Смена монтажёра проверяет логин и пароль только после действительного device token и ограничена десятью попытками в минуту по IP; пароль хранится через Data Protection и не сохраняется Agent.
- Секреты поступают через environment/Docker secrets и не коммитятся.
- Агент видим пользователю; keylogging и захват содержимого файлов запрещены.
- Screenshot capture проверяет интерактивную и незаблокированную сессию, флаг сотрудника и privacy exclusion до создания изображения.
- Web использует короткий JWT access token; refresh token ротируется в `HttpOnly + Secure + SameSite=Strict` cookie, а в PostgreSQL хранится только его hash.
- Пароли хешируются versioned PBKDF2 реализацией ASP.NET Core Identity. Login защищён rate limit по IP и временной account lockout.
- `OWNER`/`ADMIN` видят всех сотрудников; область `MANAGER`/`VIEWER` задаётся связями `user_employee_access`.

## Зафиксированные решения (ADR summary)

| Решение | Причина |
| --- | --- |
| Модульный монолит | Простая эксплуатация и достаточный запас до 50 агентов |
| Один production app-контейнер для API + SPA | Меньше компонентов, same-origin security, нет отдельной CORS-конфигурации |
| WinForms tray, не Windows Service | Доступ к интерактивной сессии и явный статус агента |
| Task Scheduler для Agent lifecycle | Автозапуск и restart-on-failure без отдельного watchdog-сервиса |
| PostgreSQL + файловое screenshot storage | Транзакционные metadata без раздувания базы бинарными файлами |
| SQLite outbox на агенте | Доставка после offline без дополнительной серверной инфраструктуры |
| Физический компьютер отдельно от operator session | Общий ПК можно передавать сотрудникам, а один сотрудник может корректно работать на нескольких ПК |
| Непересекающееся человеко-время + суммарное машино-время | Excel не утраивает часы человека и одновременно сохраняет реальную стоимость параллельных рендеров |
| Polling UI | Для 4–50 агентов WebSocket не даёт оправданной сложности |
| Portainer Stack из Git | Репозиторий остаётся источником истины, обновление воспроизводимо |
| Отдельный backup-контейнер | `pg_dump` проверяется до публикации архива и не усложняет Server |
| Restore как Compose profile `tools` | Разрушительная операция не запускается в обычном lifecycle stack |

## Последовательность реализации

Все 13 этапов из ТЗ завершены. Финальный test layer включает детерминированные unit-тесты бизнес-логики и SQLite offline queue, а также disposable Compose integration-стенд с настоящими ASP.NET Core и PostgreSQL. Integration runner публикует Server только на случайном loopback-порту через отдельный override, проверяет критические API/permission/Excel-сценарии и всегда удаляет контейнеры, сети, volumes и локальный test image.
