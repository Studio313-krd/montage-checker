# Timeline и отчёты

Этап 10 добавляет пять русскоязычных маршрутов интерфейса:

- `/timeline` — временная шкала приложений, человека и машины;
- `/applications` — foreground application time;
- `/renders` — Render, Proxy и Background Processing;
- `/screenshots` — защищённая галерея;
- `/reports` — сводка производственного времени.

Маршруты работают как deep links: ASP.NET Core отдаёт React SPA через fallback, а навигация внутри приложения не перезагружает документ.

## Период и часовой пояс

Доступны периоды «Сегодня», «Вчера», «Эта неделя», «Прошлая неделя», «Этот месяц» и произвольные даты. Browser передаёт серверу UTC-границы и IANA timezone текущего рабочего места. Production image включает `tzdata`; сервер разделяет переходящий через полночь ApplicationSession на локальные календарные даты. Диапазон положительный и не превышает 366 дней, верхняя граница исключающая.

Открытые sessions обрезаются текущим временем и границами отчёта. Поэтому длительность не выходит за выбранный период. Сервер работает с готовыми интервалами, а не суммирует фоновые процессы и не запрашивает сырую телеметрию каждую секунду.

## Timeline

`GET /api/employees/{employeeId}/timeline` возвращает тот же проверенный интервальный контракт, что и `/activity/sessions`. Desktop показывает три независимые дорожки: foreground-приложение, HumanState и MachineState. Hover и keyboard focus открывают process, window title, duration и оба состояния. На узком экране дорожки заменяет последовательный журнал событий без горизонтального скролла страницы.

## Applications

`GET /api/reports/applications` группирует по сотруднику, локальной дате, process и сохранённой productive classification. Display name берётся из активного ApplicationRule, иначе используется имя executable. Время строится только по `ApplicationSession`, поэтому десять фоновых процессов не могут одновременно увеличить результат. Render/Proxy сюда дополнительно не прибавляются.

## Renders

`GET /api/reports/renders` возвращает сотрудника, компьютер, тип, программу, начало, конец, duration, output folder/file, средний и максимальный CPU, file size, detection confidence и detection reason. Причина всегда видна в таблице — решение детектора можно проверить без чтения серверных логов.

## Summary

`GET /api/reports/summary` отдаёт Total, Productive, Active, Render, Proxy, Background, Idle, Locked и Offline. Productive — объединение интервалов Human Active и ненормальной машинной работы. Перед суммированием интервалы объединяются, поэтому одновременно идущие Active и Render учитываются один раз. Раздельные колонки состояний сохраняют свои фактические длительности.

## Screenshot gallery

`GET /api/screenshots` принимает employee, период, application, page и pageSize до 100. Фильтр приложения понимает process name и display name из ApplicationRule. Галерея доступна OWNER, ADMIN и MANAGER; employee scope проверяется до выборки. VIEWER не видит маршрут.

Каждое изображение выдаётся через защищённый `/api/screenshots/{id}/content`. Endpoint повторно проверяет scope, блокирует выход из storage root и пишет `screenshot.view` в AuditLog. Модальное окно поддерживает Previous/Next, клавиши стрелок, Escape, keyboard focus trap и возврат фокуса на исходную миниатюру.

## Excel

`GET /api/reports/export.xlsx` строит книгу по тем же серверным интервалам и правилам доступа. На странице `/reports` OWNER, ADMIN и MANAGER выбирают одного, нескольких или всех видимых сотрудников. `VIEWER` экспортировать данные не может.

В книге всегда создаются `SUMMARY`, `TIMELINE`, `APPLICATIONS`, `RENDERS` и `IDLE`. Даты и длительности являются типизированными значениями Excel, заголовки закреплены, включены фильтры. Скриншоты не встраиваются; в сводке присутствует только их количество. Полный контракт описан в [EXCEL.md](EXCEL.md).
