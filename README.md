# MontageMonitor

Self-hosted система учёта производственного времени небольшого отдела видеомонтажа. Она разделяет активность сотрудника (`HumanState`) и машинную работу (`MachineState`), чтобы длительный рендер или создание proxy не превращались в фиктивный простой.

> Проект предназначен для прозрачного корпоративного учёта с уведомлением сотрудников. Он не перехватывает нажатия клавиш, пароли или переписку. Windows-агент всегда виден в системном tray.

## Состояние разработки

Все этапы 1–13 завершены. Помимо solution, PostgreSQL-схемы и ролевой авторизации реализован рабочий Windows Agent: регистрация физического ПК одноразовым кодом, ежедневный выбор фактического монтажёра по имени и четырёхзначному PIN, device-specific доступ, heartbeat, foreground application, idle, CPU/RAM и SQLite outbox. Один сотрудник может работать на нескольких ПК, а общий ПК — переходить между сотрудниками без повторной регистрации. Независимые RenderDetector и ProxyDetector отличают просто открытый монтажный софт от реальной машинной работы по совокупности process-нагрузки и роста файлов. Excel устраняет пересечения человеко-времени между ПК, но сохраняет суммарное машино-время параллельных рендеров. Agent делает сжатые JPEG-снимки разрешённых интерактивных сессий, учитывает privacy-исключения и доставляет файлы отдельной offline-очередью. Server проверяет реальное содержимое JPEG, хранит только metadata и безопасный путь в PostgreSQL, а файлы — в отдельном volume с настраиваемым retention. Русскоязычное React-приложение включает live dashboard, дневной timeline, отчёты, защищённую галерею скриншотов, выгрузку Excel и полноценный раздел управления. Production Compose добавляет Caddy с автоматическим HTTPS, внутреннюю сеть без публичных портов базы/API, проверяемый ежедневный backup и защищённую процедуру restore.

## Компоненты

| Компонент | Назначение |
| --- | --- |
| `MontageMonitor.Agent` | Лёгкое WinForms tray-приложение для Windows 10/11 |
| `MontageMonitor.Server` | ASP.NET Core Web API и раздача собранного React UI |
| `MontageMonitor.Web` | React + TypeScript + Vite интерфейс |
| `MontageMonitor.Shared` | Версионируемые C# контракты Agent ↔ Server |

## Требования для разработки

- .NET SDK 10.0.300 или совместимый feature band
- Node.js 24+ и npm 11+
- Docker Engine с Docker Compose plugin — только для контейнерного запуска
- Windows 10/11 — для запуска Agent

## Быстрый старт

Backend:

```powershell
dotnet restore
dotnet build
$env:ConnectionStrings__PostgreSQL = "Host=localhost;Port=5432;Database=montage_monitor;Username=montage_monitor;Password=<пароль>"
$env:Security__JwtSigningKey = "<криптографически-случайный-секрет-не-менее-32-байт>"
$env:Bootstrap__OwnerLogin = "owner"
$env:Bootstrap__OwnerPassword = "<надёжный-пароль>"
$env:Bootstrap__OwnerDisplayName = "Владелец"
dotnet run --project src/MontageMonitor.Server
```

API будет доступен по `http://localhost:5147`. Проверки: `/health/live`, `/health/ready`; Swagger UI: `/swagger`. Кнопка **Authorize** в Swagger принимает access JWT из `POST /api/auth/login`. Для подключения локального процесса PostgreSQL должен быть доступен на `localhost:5432`. При запуске через Compose база остаётся только во внутренней Docker network.

Миграции:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/MontageMonitor.Server --startup-project src/MontageMonitor.Server
```

Frontend в режиме разработки (Vite проксирует `/api` на backend):

```powershell
cd src/MontageMonitor.Web
npm install
npm run dev
```

Windows Agent:

```powershell
dotnet run --project src/MontageMonitor.Agent
```

При первом запуске открывается русское окно настройки: укажите HTTPS-адрес сервера и одноразовый код регистрации, выданный администратором. После регистрации агент остаётся видимым в tray, отправляет heartbeat с текущим foreground-приложением и состоянием idle, а при отсутствии сети сохраняет события в локальную SQLite-очередь. Автозапуск и перезапуск после аварийного завершения настраиваются через Windows Task Scheduler автоматически. Штатный выход защищён паролем `IDDQD` и не вызывает перезапуск.

Сборка единственного GUI `.exe` для передачи пользователю:

```powershell
dotnet publish src/MontageMonitor.Agent -p:PublishProfile=Windows-x64 -o artifacts/agent/win-x64
```

Результат: единственный файл `artifacts/agent/win-x64/MontageMonitor.Agent.exe`. Пользователю не нужны командная строка и отдельно установленный .NET Runtime.

Agent `0.9.0` ежедневно после 06:00 требует выбрать сотрудника и подтвердить четырёхзначный PIN. Порядок работы на общих ПК и безопасное обновление уже установленного `0.8.0` описаны в [OPERATOR_SELECTION.md](OPERATOR_SELECTION.md).

Пошаговая установка и поведение агента описаны в [AGENT.md](AGENT.md).

Интервалы сотрудника доступны авторизованному Web/API-клиенту через `GET /api/employees/{employeeId}/activity/sessions`. Диапазон ограничен 31 днём, а права доступа совпадают с областью видимости сотрудников для OWNER/ADMIN/MANAGER/VIEWER.

Правила определения render и контролируемые папки настраиваются через admin API, после чего Agent получает их через device configuration без пересборки EXE. Алгоритм, веса confidence и ограничения описаны в [RENDER.md](RENDER.md).

Proxy настраивается независимо для Adobe Media Encoder, DaVinci Resolve, FFmpeg и дополнительных encoder-процессов администратора. Поддерживаются wildcard-папки и шаблоны наподобие `*_Proxy.mov`; одного совпавшего имени недостаточно. Детали: [PROXY.md](PROXY.md).

Снимки включаются отдельно для каждого сотрудника с интервалом 1–60 минут. Глобально задаются основной или все мониторы, `maxWidth`, JPEG quality 55–65, срок хранения и список процессов, при которых захват запрещён. Детали: [SCREENSHOTS.md](SCREENSHOTS.md).

После входа корневая страница открывает live dashboard. Access token остаётся только в памяти вкладки, а восстановление сеанса выполняется через защищённую refresh-cookie. Панель адаптирована для телефона и рабочего монитора, поддерживает светлую и тёмную тему, при скрытой вкладке не выполняет лишний polling. Детали: [DASHBOARD.md](DASHBOARD.md).

Timeline и отчёты доступны через постоянные маршруты `/timeline`, `/applications`, `/renders`, `/screenshots` и `/reports`. Период можно выбрать готовым пресетом или вручную; сервер обрезает открытые интервалы границами отчёта, разделяет данные по локальным датам и не считает одновременно Active и Render дважды. Детали: [REPORTS.md](REPORTS.md).

На странице `/reports` роли OWNER, ADMIN и MANAGER могут выбрать нескольких сотрудников и скачать `.xlsx` с листами `SUMMARY`, `TIMELINE`, `APPLICATIONS`, `RENDERS` и `IDLE`. Даты, время и длительности остаются типизированными значениями Excel, а скачивание фиксируется в аудите. Детали: [EXCEL.md](EXCEL.md).

## Проверка

Полный локальный прогон, включая одноразовый Docker-стенд PostgreSQL + Server:

```powershell
.\tests\Stage13.Full.ps1
```

Только быстрые проверки без Docker integration:

```powershell
.\tests\Stage13.Full.ps1 -SkipIntegration
```

Подробности и отдельные команды находятся в [tests/README.md](tests/README.md).

## Установка VPS через Git и Portainer

Репозиторий является источником конфигурации deployment. В Portainer откройте **Stacks → Add stack → Git repository**, укажите URL/branch репозитория и Compose path `docker-compose.yml`. Если на VPS уже работает общий Nginx/OpenResty и он проксирует приложение на отдельный порт, используйте `docker-compose.portainer.yml` и задайте `APP_HTTP_PORT`. Добавьте переменные из `.env.example` в секции Environment variables, заменив placeholder secrets, затем нажмите **Deploy the stack**. Первый `OWNER` создаётся автоматически из `OWNER_LOGIN`, `OWNER_PASSWORD` и `OWNER_DISPLAY_NAME`; после первого входа очистите `OWNER_PASSWORD` в Portainer и повторно разверните stack.

Для локальной проверки того же stack:

```bash
docker compose --env-file .env config --quiet
docker compose --env-file .env up -d --build
```

Относительные bind mounts не используются: application images собираются прямо из клонированного Git-репозитория, а данные находятся в named volumes. Server ждёт healthy PostgreSQL и применяет EF migrations перед приёмом запросов. Контейнер `backup` сразу и затем раз в сутки создаёт проверенный custom-format `pg_dump`, хранит архивы 14 дней и предоставляет одноразовый restore-сервис из profile `tools`. Точные процедуры DNS, Portainer, первого OWNER, backup, restore и обновления описаны в [DEPLOY.md](DEPLOY.md).

## Документация

- [ARCHITECTURE.md](ARCHITECTURE.md) — границы компонентов, потоки данных и принятые решения.
- [DATABASE.md](DATABASE.md) — схема PostgreSQL, индексы и работа с migrations.
- [AUTHORIZATION.md](AUTHORIZATION.md) — вход, refresh-ротация, роли и bootstrap OWNER.
- [AGENT.md](AGENT.md) — сборка, регистрация и эксплуатация Windows Agent.
- [OPERATOR_SELECTION.md](OPERATOR_SELECTION.md) — ежедневный PIN, общие ПК и обновление старого EXE.
- [ACTIVITY.md](ACTIVITY.md) — правила построения интервалов и Offline-разрывов.
- [RENDER.md](RENDER.md) — сигналы, confidence, гистерезис и настройка RenderDetector.
- [PROXY.md](PROXY.md) — определение Proxy, приоритет папок и admin API.
- [SCREENSHOTS.md](SCREENSHOTS.md) — захват, privacy, offline upload, хранение и retention.
- [DASHBOARD.md](DASHBOARD.md) — live dashboard, polling, состояния карточек и модель доступа.
- [REPORTS.md](REPORTS.md) — timeline, приложения, машинные операции, сводка и галерея.
- [EXCEL.md](EXCEL.md) — права, состав и форматы пятилистовой выгрузки Excel.
- [DEPLOY.md](DEPLOY.md) — каркас инструкции для VPS.
