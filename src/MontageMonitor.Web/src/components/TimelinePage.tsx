import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { ApiError, getEmployees, getTimeline } from '../api'
import {
  classificationLabels,
  formatDuration,
  formatMoment,
  humanLabels,
  machineLabels,
  presetRange,
  toReportQuery,
  type DateRange,
} from '../reporting'
import type { ApplicationSession, Employee, HumanState, MachineState, StateSession, TimelineData } from '../types'
import { ReportControls, ReportEmpty, ReportError, ReportHeading } from './ReportControls'

type TimelineSession = ApplicationSession | StateSession<HumanState> | StateSession<MachineState>

interface TimelineEvent {
  id: string
  kind: 'application' | 'human' | 'machine'
  label: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationSeconds: number
  detail: string
  stateClass: string
}

function sessionAt<T extends TimelineSession>(sessions: T[], timestamp: number): T | undefined {
  return sessions.find((session) => {
    const start = new Date(session.startedAtUtc).getTime()
    const end = session.endedAtUtc ? new Date(session.endedAtUtc).getTime() : Number.POSITIVE_INFINITY
    return start <= timestamp && timestamp < end
  })
}

function timelineTicks(fromUtc: string, toUtc: string): string[] {
  const start = new Date(fromUtc).getTime()
  const end = new Date(toUtc).getTime()
  const includeDate = end - start > 36 * 60 * 60 * 1_000
  return [0, .25, .5, .75, 1].map((position) =>
    formatMoment(new Date(start + (end - start) * position).toISOString(), includeDate))
}

function TimelineLane({
  fromUtc,
  label,
  sessions,
  toUtc,
  timeline,
}: {
  fromUtc: string
  label: string
  sessions: TimelineSession[]
  toUtc: string
  timeline: TimelineData
}) {
  const start = new Date(fromUtc).getTime()
  const end = new Date(toUtc).getTime()
  const total = Math.max(1, end - start)

  return (
    <div className="timeline-lane">
      <strong>{label}</strong>
      <div className="timeline-rail">
        {sessions.map((session) => {
          const segmentStart = Math.max(start, new Date(session.startedAtUtc).getTime())
          const segmentEnd = Math.min(
            end,
            new Date(session.endedAtUtc ?? timeline.generatedAtUtc).getTime(),
          )
          const midpoint = segmentStart + (segmentEnd - segmentStart) / 2
          const application = 'processName' in session
          const human = sessionAt(timeline.humanStates, midpoint)?.state
          const machine = sessionAt(timeline.machineStates, midpoint)?.state
          const state = 'state' in session ? session.state : session.classification
          const title = application ? session.processName : String(state)
          const detail = application ? session.windowTitle : null
          const style = {
            '--segment-left': `${Math.max(0, ((segmentStart - start) / total) * 100)}%`,
            '--segment-width': `${Math.max(.35, ((segmentEnd - segmentStart) / total) * 100)}%`,
          } as CSSProperties
          return (
            <button
              aria-label={`${title}, ${formatMoment(session.startedAtUtc)}–${formatMoment(session.endedAtUtc)}, ${formatDuration(session.durationSeconds)}`}
              className={`timeline-segment state-${String(state).toLowerCase()}`}
              key={session.id}
              style={style}
              type="button"
            >
              <span className="timeline-tooltip" role="tooltip">
                <b>{title}</b>
                {detail && <span>{detail}</span>}
                <time>{formatMoment(session.startedAtUtc)}–{formatMoment(session.endedAtUtc)} · {formatDuration(session.durationSeconds)}</time>
                <span>Человек: {human ? humanLabels[human] : '—'}</span>
                <span>Машина: {machine ? machineLabels[machine] : '—'}</span>
              </span>
            </button>
          )
        })}
      </div>
    </div>
  )
}

export function TimelinePage() {
  const [employees, setEmployees] = useState<Employee[]>([])
  const [employeeId, setEmployeeId] = useState('')
  const [range, setRange] = useState<DateRange>(() => presetRange('today'))
  const [applied, setApplied] = useState<DateRange>(() => presetRange('today'))
  const [data, setData] = useState<TimelineData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    getEmployees(controller.signal).then((items) => {
      setEmployees(items)
      setEmployeeId((current) => current || items[0]?.id || '')
      if (items.length === 0) setLoading(false)
    }).catch((caught: unknown) => {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught instanceof ApiError ? caught.message : 'Не удалось загрузить сотрудников.')
        setLoading(false)
      }
    })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!employeeId) {
      return
    }
    const controller = new AbortController()
    getTimeline(employeeId, toReportQuery(applied), controller.signal).then((next) => {
      setData(next)
      setError(null)
    }).catch((caught: unknown) => {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught instanceof ApiError ? caught.message : 'Не удалось построить временную шкалу.')
      }
    }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [applied, employeeId, employees.length, retry])

  const events = useMemo<TimelineEvent[]>(() => {
    if (!data) return []
    return [
      ...data.applications.map((item) => ({
        id: item.id,
        kind: 'application' as const,
        label: item.processName.replace(/\.exe$/i, ''),
        startedAtUtc: item.startedAtUtc,
        endedAtUtc: item.endedAtUtc,
        durationSeconds: item.durationSeconds,
        detail: item.windowTitle ?? classificationLabels[item.classification],
        stateClass: item.classification.toLowerCase(),
      })),
      ...data.humanStates.filter((item) => item.state !== 'Active').map((item) => ({
        id: item.id,
        kind: 'human' as const,
        label: humanLabels[item.state],
        startedAtUtc: item.startedAtUtc,
        endedAtUtc: item.endedAtUtc,
        durationSeconds: item.durationSeconds,
        detail: 'Состояние человека',
        stateClass: item.state.toLowerCase(),
      })),
      ...data.machineStates.filter((item) => item.state !== 'Normal').map((item) => ({
        id: item.id,
        kind: 'machine' as const,
        label: machineLabels[item.state],
        startedAtUtc: item.startedAtUtc,
        endedAtUtc: item.endedAtUtc,
        durationSeconds: item.durationSeconds,
        detail: item.detectionReason ?? 'Состояние машины',
        stateClass: item.state.toLowerCase(),
      })),
    ].sort((left, right) => left.startedAtUtc.localeCompare(right.startedAtUtc))
  }, [data])
  const ticks = data ? timelineTicks(data.fromUtc, data.toUtc) : []

  const apply = () => {
    setLoading(true)
    setApplied({ ...range })
  }

  return (
    <>
      <ReportHeading
        description="День разложен на три независимые дорожки: активное приложение, состояние человека и работа компьютера."
        eyebrow="Монтажная линейка"
        title="Хронология дня"
      />
      <ReportControls
        employeeId={employeeId}
        employeeRequired
        employees={employees}
        loading={loading}
        onEmployeeChange={setEmployeeId}
        onRangeChange={setRange}
        onSubmit={apply}
        range={range}
      />
      {error && <ReportError message={error} onRetry={() => setRetry((value) => value + 1)} />}
      {loading && !data ? <div className="report-skeleton" aria-label="Строим хронологию" aria-busy="true" />
        : data && (data.applications.length || data.humanStates.length || data.machineStates.length) ? (
          <>
            <section className="timeline-board" aria-labelledby="timeline-board-title">
              <div className="timeline-board-heading">
                <div>{ticks.map((tick, index) => <span key={`${tick}-${index}`}>{tick}</span>)}</div>
                <h2 id="timeline-board-title">Шкала выбранного периода</h2>
              </div>
              <TimelineLane fromUtc={data.fromUtc} label="Приложение" sessions={data.applications} timeline={data} toUtc={data.toUtc} />
              <TimelineLane fromUtc={data.fromUtc} label="Человек" sessions={data.humanStates} timeline={data} toUtc={data.toUtc} />
              <TimelineLane fromUtc={data.fromUtc} label="Машина" sessions={data.machineStates} timeline={data} toUtc={data.toUtc} />
              <div className="timeline-legend" aria-label="Легенда состояний">
                <span><i className="legend-productive" /> Приложение</span>
                <span><i className="legend-idle" /> Простой</span>
                <span><i className="legend-render" /> Рендер</span>
                <span><i className="legend-proxy" /> Прокси</span>
              </div>
            </section>
            <section className="event-ledger" aria-labelledby="event-ledger-title">
              <div className="section-heading"><div><p className="eyebrow">Расшифровка</p><h2 id="event-ledger-title">События по времени</h2></div></div>
              <ol>
                {events.map((event) => (
                  <li key={`${event.kind}-${event.id}`}>
                    <time>{formatMoment(event.startedAtUtc)}</time>
                    <i className={`event-mark state-${event.stateClass}`} aria-hidden="true" />
                    <div><strong>{event.label}</strong><span>{event.detail}</span></div>
                    <span>{formatDuration(event.durationSeconds)}</span>
                  </li>
                ))}
              </ol>
            </section>
          </>
        ) : <ReportEmpty>Выберите другой день или сотрудника — интервалы появятся после первых сигналов агента.</ReportEmpty>}
    </>
  )
}
