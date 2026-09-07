import { useEffect, useMemo, useState } from 'react'
import { ApiError, getApplicationReport, getEmployees, getRenderReport, getSummaryReport } from '../api'
import {
  classificationLabels,
  formatDuration,
  formatFileSize,
  formatMoment,
  presetRange,
  processingLabels,
  toReportQuery,
  type DateRange,
} from '../reporting'
import type { ApplicationReport, Employee, RenderReport, SummaryReport } from '../types'
import { ExcelExportPanel } from './ExcelExportPanel'
import { ReportControls, ReportEmpty, ReportError, ReportHeading } from './ReportControls'

function useEmployees() {
  const [employees, setEmployees] = useState<Employee[]>([])
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    getEmployees(controller.signal).then(setEmployees).catch((caught: unknown) => {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught instanceof ApiError ? caught.message : 'Не удалось загрузить сотрудников.')
      }
    })
    return () => controller.abort()
  }, [])
  return { employees, employeeError: error }
}

function useFilterState() {
  const [employeeId, setEmployeeId] = useState('')
  const [range, setRange] = useState<DateRange>(() => presetRange('today'))
  const [applied, setApplied] = useState(() => ({ employeeId: '', range: presetRange('today') }))
  const apply = () => setApplied({ employeeId, range: { ...range } })
  return {
    applied,
    apply,
    employeeId,
    onEmployeeChange: setEmployeeId,
    onRangeChange: setRange,
    range,
  }
}

export function ApplicationsPage() {
  const { employees, employeeError } = useEmployees()
  const filter = useFilterState()
  const [data, setData] = useState<ApplicationReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    getApplicationReport(
      toReportQuery(filter.applied.range, filter.applied.employeeId),
      controller.signal,
    ).then((next) => {
      setData(next)
      setError(null)
    }).catch((caught: unknown) => {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught instanceof ApiError ? caught.message : 'Не удалось построить отчёт по приложениям.')
      }
    }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [filter.applied, retry])

  const total = useMemo(() => data?.applications.reduce((sum, item) => sum + item.durationSeconds, 0) ?? 0, [data])
  const apply = () => { setLoading(true); filter.apply() }

  return (
    <>
      <ReportHeading
        description="Только активное приложение: фоновые процессы не умножают рабочее время. Рендер и прокси учитываются в отдельном отчёте."
        eyebrow="Время в приложениях"
        title="Приложения"
      />
      <ReportControls {...filter} employees={employees} loading={loading} onSubmit={apply} />
      {(error || employeeError) && <ReportError message={error ?? employeeError!} onRetry={() => setRetry((value) => value + 1)} />}
      {loading && !data ? <div className="report-skeleton" aria-label="Загрузка приложений" aria-busy="true" />
        : data?.applications.length ? (
          <section className="report-surface" aria-labelledby="applications-title">
            <div className="report-surface-title">
              <div><p className="eyebrow">Итого в приложениях</p><h2 id="applications-title">{formatDuration(total)}</h2></div>
              <span>{data.applications.length} строк</span>
            </div>
            <div aria-label="Таблица приложений" className="responsive-table" tabIndex={0}>
              <table>
                <thead><tr><th>Приложение</th><th>Длительность</th><th>Классификация</th><th>Сотрудник</th><th>Дата</th></tr></thead>
                <tbody>{data.applications.map((item) => (
                  <tr key={`${item.employeeId}-${item.date}-${item.processName}`}>
                    <td><strong>{item.application}</strong><small>{item.processName}</small></td>
                    <td className="time-cell">{formatDuration(item.durationSeconds)}</td>
                    <td><span className={`classification classification-${item.classification.toLowerCase()}`}>{classificationLabels[item.classification]}</span></td>
                    <td>{item.employeeName}</td>
                    <td>{new Intl.DateTimeFormat('ru-RU').format(new Date(`${item.date}T12:00:00`))}</td>
                  </tr>
                ))}</tbody>
              </table>
            </div>
          </section>
        ) : <ReportEmpty>В выбранном периоде нет интервалов активных приложений.</ReportEmpty>}
    </>
  )
}

export function RendersPage() {
  const { employees, employeeError } = useEmployees()
  const filter = useFilterState()
  const [data, setData] = useState<RenderReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    getRenderReport(toReportQuery(filter.applied.range, filter.applied.employeeId), controller.signal)
      .then((next) => { setData(next); setError(null) })
      .catch((caught: unknown) => {
        if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught instanceof ApiError ? caught.message : 'Не удалось загрузить машинные операции.')
        }
      }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [filter.applied, retry])

  const apply = () => { setLoading(true); filter.apply() }
  return (
    <>
      <ReportHeading
        description="Каждое решение детектора можно проверить: программа, нагрузка, выходной файл, confidence и причина сохранены вместе."
        eyebrow="Машинное время"
        title="Рендеры и прокси"
      />
      <ReportControls {...filter} employees={employees} loading={loading} onSubmit={apply} />
      {(error || employeeError) && <ReportError message={error ?? employeeError!} onRetry={() => setRetry((value) => value + 1)} />}
      {loading && !data ? <div className="report-skeleton" aria-label="Загрузка рендеров" aria-busy="true" />
        : data?.renders.length ? (
          <section className="report-surface" aria-labelledby="renders-title">
            <div className="report-surface-title"><div><p className="eyebrow">Журнал детектора</p><h2 id="renders-title">Машинные операции</h2></div><span>{data.renders.length} операций</span></div>
            <div aria-label="Таблица машинных операций" className="responsive-table render-table" tabIndex={0}>
              <table>
                <thead><tr><th>Тип</th><th>Сотрудник / компьютер</th><th>Программа</th><th>Начало / конец</th><th>Длительность</th><th>Результат</th><th>ЦП</th><th>Уверенность</th><th>Причина</th></tr></thead>
                <tbody>{data.renders.map((item) => {
                  const confidence = item.detectionConfidence >= 80 ? 'Высокая' : item.detectionConfidence >= 50 ? 'Средняя' : 'Низкая'
                  return (
                    <tr key={item.id}>
                      <td><span className={`processing processing-${item.type.toLowerCase()}`}>{processingLabels[item.type]}</span></td>
                      <td><strong>{item.employeeName}</strong><small>{item.computerName}</small></td>
                      <td>{item.program}</td>
                      <td><time>{formatMoment(item.startedAtUtc, true)}</time><small>{item.endedAtUtc ? formatMoment(item.endedAtUtc, true) : 'Продолжается'}</small></td>
                      <td className="time-cell">{formatDuration(item.durationSeconds)}</td>
                      <td><span className="path-cell" title={item.outputFile ?? item.outputFolder ?? undefined}>{item.outputFile ?? item.outputFolder ?? '—'}</span><small>{formatFileSize(item.fileSizeBytes)}</small></td>
                      <td>{item.averageCpuPercent?.toFixed(0) ?? '—'}%<small>макс. {item.maxCpuPercent?.toFixed(0) ?? '—'}%</small></td>
                      <td><strong>{item.detectionConfidence}%</strong><small>{confidence}</small></td>
                      <td className="reason-cell">{item.detectionReason}</td>
                    </tr>
                  )
                })}</tbody>
              </table>
            </div>
          </section>
        ) : <ReportEmpty>Рендер, прокси и фоновая обработка за этот период не обнаружены.</ReportEmpty>}
    </>
  )
}

export function SummaryPage({ canExport }: { canExport: boolean }) {
  const { employees, employeeError } = useEmployees()
  const filter = useFilterState()
  const [data, setData] = useState<SummaryReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    getSummaryReport(toReportQuery(filter.applied.range, filter.applied.employeeId), controller.signal)
      .then((next) => { setData(next); setError(null) })
      .catch((caught: unknown) => {
        if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught instanceof ApiError ? caught.message : 'Не удалось собрать сводный отчёт.')
        }
      }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [filter.applied, retry])

  const apply = () => { setLoading(true); filter.apply() }
  return (
    <>
      <ReportHeading
        description="Сводка производственного времени без рейтинга активности мыши. Пересекающиеся активная работа и рендер не считаются дважды."
        eyebrow="Производственная сводка"
        title="Отчёты"
      />
      <ReportControls {...filter} employees={employees} loading={loading} onSubmit={apply} />
      {canExport && <ExcelExportPanel employees={employees} range={filter.applied.range} />}
      {(error || employeeError) && <ReportError message={error ?? employeeError!} onRetry={() => setRetry((value) => value + 1)} />}
      {loading && !data ? <div className="report-skeleton" aria-label="Загрузка сводки" aria-busy="true" />
        : data?.employees.some((item) => item.totalSeconds > 0) ? (
          <section className="summary-report" aria-labelledby="summary-report-title">
            <div className="section-heading"><div><p className="eyebrow">Без двойного счёта</p><h2 id="summary-report-title">Время по сотрудникам</h2></div><p>Часовой пояс: {data.timeZone}</p></div>
            <div className="summary-report-list">{data.employees.map((item) => {
              const base = Math.max(1, item.totalSeconds)
              return (
                <article className="summary-person" key={item.employeeId}>
                  <header><div><h3>{item.employeeName}</h3><span>{formatMoment(item.firstEventAtUtc)}–{formatMoment(item.lastEventAtUtc)}</span></div><strong>{formatDuration(item.productiveSeconds)}</strong><small>продуктивно</small></header>
                  <div className="production-strip" aria-label={`Структура времени сотрудника ${item.employeeName}`}>
                    <i className="strip-active" style={{ flexGrow: item.activeSeconds / base }} title={`Активная работа ${formatDuration(item.activeSeconds)}`} />
                    <i className="strip-render" style={{ flexGrow: item.renderSeconds / base }} title={`Рендер ${formatDuration(item.renderSeconds)}`} />
                    <i className="strip-proxy" style={{ flexGrow: item.proxySeconds / base }} title={`Прокси ${formatDuration(item.proxySeconds)}`} />
                    <i className="strip-idle" style={{ flexGrow: (item.idleSeconds + item.lockedSeconds + item.offlineSeconds) / base }} title={`Простой и отсутствие ${formatDuration(item.idleSeconds + item.lockedSeconds + item.offlineSeconds)}`} />
                  </div>
                  <dl>
                    <div><dt>Всего</dt><dd>{formatDuration(item.totalSeconds)}</dd></div>
                    <div><dt>Активно</dt><dd>{formatDuration(item.activeSeconds)}</dd></div>
                    <div><dt>Рендер</dt><dd>{formatDuration(item.renderSeconds)}</dd></div>
                    <div><dt>Прокси</dt><dd>{formatDuration(item.proxySeconds)}</dd></div>
                    <div><dt>Фоновая работа</dt><dd>{formatDuration(item.backgroundSeconds)}</dd></div>
                    <div><dt>Простой</dt><dd>{formatDuration(item.idleSeconds)}</dd></div>
                    <div><dt>Блокировка</dt><dd>{formatDuration(item.lockedSeconds)}</dd></div>
                    <div><dt>Нет связи</dt><dd>{formatDuration(item.offlineSeconds)}</dd></div>
                  </dl>
                </article>
              )
            })}</div>
          </section>
        ) : <ReportEmpty>За выбранный период нет агрегированных состояний.</ReportEmpty>}
    </>
  )
}
