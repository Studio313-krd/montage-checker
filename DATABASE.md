# PostgreSQL schema

Схема MontageMonitor создаётся последовательными EF Core migrations; актуальная миграция `RemoveAgentEnrollmentTokens` удаляет устаревшие одноразовые коды после перехода на выбор сотрудника и вход по паролю. Имена таблиц, колонок, ключей и индексов используют `snake_case`. Все моменты времени хранятся как PostgreSQL `timestamp with time zone`; .NET-модели используют `DateTimeOffset` и UTC.

## Таблицы

| Группа | Таблицы | Назначение |
| --- | --- | --- |
| Пользователи | `users`, `refresh_tokens`, `user_employee_access` | Авторизация, роли, безопасные refresh token hashes и ограничения видимости сотрудников |
| Организация | `employees`, `computers`, `agents`, `agent_credentials`, `agent_operator_sessions` | Сотрудники, рабочие станции, device-specific credentials и ежедневные входы монтажёров |
| Приём данных | `heartbeats`, `activity_events` | Срезы состояния и идемпотентные UUID-события с `jsonb` payload |
| Интервалы | `application_sessions`, `human_state_sessions`, `machine_state_sessions` | Foreground application и независимые human/machine интервалы |
| Производство | `render_sessions` | Render, proxy и background sessions с confidence/reason |
| Скриншоты | `screenshots` | Только metadata и относительный storage path, без бинарных данных |
| Правила | `application_rules`, `render_rules`, `watched_folders`, `system_settings` | Версионируемая серверная конфигурация агента |
| Аудит | `audit_logs` | Security- и admin-события |

## Целостность

- `activity_events.event_id` — первичный UUID: повторная отправка не создаёт дубликат.
- `heartbeats.event_id` имеет unique index; приём использует `ON CONFLICT DO NOTHING`, поэтому даже одновременные повторы UUID не создают дубликат.
- `screenshots.event_id` имеет unique index; повторная multipart-загрузка возвращает существующий результат, не создавая второй файл или metadata.
- Heartbeat хранит версию Agent, пользователя и имя Windows-компьютера, foreground process/path/title, idle и загрузку CPU/RAM. Для Render/Proxy дополнительно сохраняются process CPU/working set/I/O, дочерние процессы, выходной путь/размер, confidence и reason; GPU используется только Render и остаётся nullable.
- `employees.operator_pin_protected` — сохранённое для совместимости имя колонки, которая хранит четырёхзначный пароль Agent в обратимо зашифрованном ASP.NET Core Data Protection payload. Ключи находятся в отдельном постоянном Docker volume и не попадают в PostgreSQL или Git.
- `agent_operator_sessions` связывает device Agent, физический компьютер и фактически выбранного сотрудника на интервал до ежедневной границы 06:00. Исторические heartbeat и sessions продолжают хранить `employee_id`, поэтому последующее переключение не меняет прошлые данные.
- Имя компьютера уникально для сотрудника без учёта регистра через `computers.normalized_name`.
- Одноразовые enrollment tokens больше не используются; таблица удаляется миграцией `RemoveAgentEnrollmentTokens`.
- Логины пользователей и сотрудников уникальны в нормализованном виде.
- Для sessions база проверяет `ended_at_utc >= started_at_utc`.
- Частичные unique indexes разрешают не более одного открытого application-, human-, machine- и render-интервала на компьютер.
- CPU, memory и detection confidence ограничены диапазоном 0–100.
- Screenshot interval ограничен 1–60 минутами.
- Исторические сущности используют `Restrict`; сотрудник деактивируется, а не удаляется.
- Screenshot binary хранится в volume `/data/screenshots`, в таблице остаются размеры, экран, foreground metadata, состояния, относительный путь и отметка удаления файла. Retention удаляет binary, но сохраняет историческую metadata.

## Identity и авторизация

`users.password_hash` обязателен; пустое значение запрещено check constraint. Поля `failed_login_count` и `lockout_end_utc` обеспечивают блокировку учётной записи. Таблица `refresh_tokens` содержит только SHA-256 hashes, срок действия, отзыв и ссылку на replacement token; raw token существует только на клиенте. `user_employee_access` ограничивает видимость сотрудников для MANAGER/VIEWER.

Migration `AddUserAuthenticationState` деактивирует возможные legacy-записи без пароля перед включением обязательного hash, поэтому обновление не создаёт доступных учётных записей с пустым паролем.

## Индексы

Основные time-range индексы созданы для пар:

- `employee_id + timestamp/start`;
- `computer_id + timestamp/start`;
- `employee_id + process_name + start`;
- operator session по `agent_id`, `computer_id`, `employee_id` и времени начала; для одного Agent разрешена только одна незавершённая сессия;
- `type/state + start`;
- screenshot retention по `timestamp_utc + file_deleted_at_utc`;
- audit по пользователю, времени и entity.

## Seed data

Migration создаёт:

- 10 редактируемых application rules для Adobe, Resolve, FFmpeg, Blender, Chrome и privacy exclusions;
- 7 редактируемых render rules для Premiere Pro, Media Encoder, After Effects/aerender, Resolve, FFmpeg и Blender;
- 3 редактируемых proxy rules для Media Encoder, Resolve и FFmpeg с начальными filename patterns;
- 12 системных настроек: timezone, heartbeat/sync interval, idle threshold, screenshots (capture mode, width, quality, retention), backup и latest agent version.

Это начальные строки базы, а не зашитая в detector классификация. Admin API сможет менять их без обновления Agent.

## Команды

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations list `
  --project src/MontageMonitor.Server `
  --startup-project src/MontageMonitor.Server

dotnet tool run dotnet-ef database update `
  --project src/MontageMonitor.Server `
  --startup-project src/MontageMonitor.Server
```

Строка подключения передаётся только через `ConnectionStrings__PostgreSQL` или Portainer environment variables и не хранится в Git.
