# Tests

Этап 13 завершён. Набор из 52 unit-тестов покрывает activity session aggregation, смену оператора на одном ПК, устранение пересечений человека на трёх ПК, ежедневную границу 06:00, формат/защиту PIN, обратную совместимость версий Agent, Offline gaps, idle threshold/lock priority, Render/Proxy state machines, screenshot policy, проверку JPEG, dashboard, timezone conversion, Excel aggregation/workbook и permissions. SQLite integration-тест открывает очередь повторно, имитируя восстановление Agent после offline/restart, и проверяет UUID-дедупликацию и удаление подтверждённого события.

Полная проверка одной командой:

```powershell
.\tests\Stage13.Full.ps1
```

Она выполняет restore/build/test/format, `npm ci`, frontend lint/build и disposable Docker integration. Для быстрого прогона без Docker используйте `-SkipIntegration`.

Отдельный integration-runner:

```powershell
.\tests\Stage13.Integration.ps1
```

Runner выбирает свободный loopback-порт, создаёт уникальный Compose project, поднимает настоящие PostgreSQL и Server, применяет все migrations и проверяет одноразовый enrollment token, выдачу четырёхзначного PIN, обязательный выбор монтажёра, неверный PIN, привязку общего ПК к другому сотруднику, совместимость Agent `0.8.0`, heartbeat upload, повтор/конфликт UUID, область VIEWER и реальный Excel download. При ошибке перед очисткой выводятся логи Server; в `finally` удаляются только созданные этим запуском контейнеры, volumes, networks и test image. Production Compose не изменяется.

Smoke-тест Этапа 8 запускается против временного Compose-стенда:

```powershell
.\tests\Stage8.Smoke.ps1 `
  -BaseUri "http://127.0.0.1:18080" `
  -OwnerLogin "owner" `
  -OwnerPassword "<bootstrap-пароль>"
```

Он проверяет регистрацию устройства, выдачу screenshot-конфигурации, privacy rules, валидную и повторную multipart-загрузку, отклонение поддельного JPEG и Locked-сессии, `SCREENSHOT_SKIPPED_PRIVACY`, per-employee отключение и физическое удаление файла retention-worker с сохранением metadata.

Smoke-тест Этапа 9 создаёт четыре тестовые карточки и проверяет Active, Idle, Render, Proxy, Offline, ссылку на последний защищённый JPEG и отказ неавторизованному запросу:

```powershell
.\tests\Stage9.Smoke.ps1 `
  -BaseUri "http://127.0.0.1:18080" `
  -OwnerLogin "owner" `
  -OwnerPassword "<bootstrap-пароль>"
```

После него на том же временном стенде запускается smoke-тест Этапа 10:

```powershell
.\tests\Stage10.Smoke.ps1 `
  -BaseUri "http://127.0.0.1:18080" `
  -OwnerLogin "owner" `
  -OwnerPassword "<bootstrap-пароль>"
```

Он проверяет timeline, агрегацию четырёх foreground-приложений, Render/Proxy, сводные длительности, фильтр галереи, защищённую выдачу JPEG, аудит просмотра и валидацию диапазона.

После него на том же временном стенде запускается smoke-тест Этапа 11:

```powershell
.\tests\Stage11.Smoke.ps1 `
  -BaseUri "http://127.0.0.1:18080" `
  -OwnerLogin "owner" `
  -OwnerPassword "<bootstrap-пароль>"
```

Он скачивает реальный `.xlsx`, проверяет MIME/type, ZIP-структуру Open XML, пять листов, отказ без авторизации, валидацию employee IDs и запись `report.excel.downloaded` в аудит.

Smoke-тест Этапа 12 проверяет production Compose, отсутствие публичных портов у PostgreSQL и Server, создание и health архива, обязательную подтверждающую фразу и фактическое восстановление тестовой строки:

```powershell
.\tests\Stage12.Smoke.ps1 -AcknowledgeDatabaseReplacement
```

Тест намеренно удаляет и пересоздаёт базу, указанную в текущем Compose-окружении. Запускайте его только на одноразовом тестовом стенде; параметр подтверждения обязателен. Скрипт останавливает `server` и `backup` на время restore, затем запускает их снова и удаляет созданную проверочную строку.
