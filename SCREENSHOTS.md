# Скриншоты

Этап 8 реализует снимки экрана как отдельный от heartbeat поток. Это не скрытое наблюдение: Agent остаётся видимым в tray, не перехватывает клавиатуру и не читает содержимое пользовательских файлов.

## Когда выполняется захват

Для сотрудника администратор отдельно задаёт `ScreenshotEnabled` и интервал от 1 до 60 минут. Захват возможен только при одновременном выполнении условий:

- Agent работает в интерактивной пользовательской сессии;
- Windows-сессия не заблокирована;
- снимки включены для сотрудника;
- foreground process не входит в privacy-исключения.

Глобальная конфигурация выбирает `PrimaryMonitor` или `AllMonitors`, максимальную ширину 320–7680 пикселей и JPEG quality 55–65. Высота уменьшается пропорционально; увеличение маленького изображения не выполняется. Для каждого монитора создаётся отдельное событие с `screenIndex`.

Если активен исключённый процесс, изображение даже не создаётся. Agent помещает `SCREENSHOT_SKIPPED_PRIVACY` с UUID, временем, процессом и заголовком окна в надёжный heartbeat outbox. Начальный список включает `1Password.exe` и `KeePass.exe`; OWNER/ADMIN может изменить его через `/api/admin/screenshots/privacy-processes`.

## Offline-доставка

Сжатый JPEG сначала атомарно сохраняется в `%LocalAppData%\MontageMonitor\screenshots`, а metadata — в таблицу `outgoing_screenshots` локального `queue.db`. Multipart upload выполняется отдельно от heartbeat, максимум по три готовых файла за цикл. При временной ошибке применяется backoff до пяти минут; после подтверждения Server локальная запись и файл удаляются.

Metadata содержит UUID события, Agent/employee/computer IDs, UTC-время, номер и реальные размеры экрана, foreground process/title, `HumanState` и `MachineState`.

## Приём и хранение

`POST /api/agent/screenshots` доступен только с device credential. Server:

- сверяет Agent/employee/computer IDs с credential;
- повторно проверяет, что снимки включены для сотрудника и сессия не `Locked`;
- ограничивает multipart и размер файла;
- принимает только `image/jpeg` и проверяет JPEG-маркеры и реальные размеры, не доверяя имени/расширению;
- строит путь только из разобранных UUID и UTC-даты и проверяет его нахождение внутри storage root;
- обеспечивает идемпотентность по `screenshots.event_id`.

Файл размещается в `/data/screenshots/{employeeId}/YYYY/MM/DD/{eventId}.jpg`. PostgreSQL хранит metadata и относительный путь, но не binary.

## Настройки и retention

Глобальные параметры доступны OWNER/ADMIN через `GET/PUT /api/admin/screenshots/settings`. Значения по умолчанию: основной монитор, `maxWidth=1600`, quality `60`, retention `30` дней.

`ScreenshotRetentionWorker` стартует вместе с Server и затем выполняется ежедневно. Он удаляет истёкший файл безопасного пути и записывает `file_deleted_at_utc`, оставляя metadata для истории и аудита. Volume `screenshots_data` должен резервироваться отдельно от PostgreSQL.
