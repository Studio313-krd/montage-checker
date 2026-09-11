import { useCallback, useEffect, useMemo, useState } from 'react'
import { ApiError, getDashboard, getScreenshot } from '../api'
import type {
  DashboardData,
  DashboardEmployee,
  DashboardScreenshot,
  HumanState,
  MachineState,
} from '../types'
import { Icon } from './Icon'

const humanLabels: Record<HumanState, string> = {
  Active: 'Активная работа',
  Idle: 'Нет ввода',
  Locked: 'Экран заблокирован',
  Offline: 'Нет связи',
}

const machineLabels: Record<MachineState, string> = {
  Normal: 'Обычный режим',
  Render: 'Рендер',
  Proxy: 'Создание прокси',
  BackgroundProcessing: 'Фоновая обработка',
}

function formatTimecode(startedAtUtc: string | null, now: Date): string {
  if (!startedAtUtc) return '—'
  const seconds = Math.max(0, Math.floor((now.getTime() - new Date(startedAtUtc).getTime()) / 1000))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const rest = seconds % 60
  return [hours, minutes, rest].map((part) => String(part).padStart(2, '0')).join(':')
}

function formatMoment(value: string | null): string {
  if (!value) return 'ещё не было'
  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}

function getInitials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('')
}

function useScreenshot(screenshot: DashboardScreenshot | null) {
  const contentUrl = screenshot?.contentUrl ?? null
  const [loaded, setLoaded] = useState<{
    contentUrl: string | null
    failed: boolean
    source: string | null
  }>({ contentUrl: null, failed: false, source: null })

  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | null = null
    let active = true
    if (!contentUrl) {
      return () => controller.abort()
    }

    getScreenshot(contentUrl, controller.signal)
      .then((blob) => {
        objectUrl = URL.createObjectURL(blob)
        if (active) setLoaded({ contentUrl, failed: false, source: objectUrl })
      })
      .catch((error: unknown) => {
        if (active && !(error instanceof DOMException && error.name === 'AbortError')) {
          setLoaded({ contentUrl, failed: true, source: null })
        }
      })

    return () => {
      active = false
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [contentUrl])

  const isCurrent = loaded.contentUrl === contentUrl
  return {
    loading: contentUrl !== null && !(isCurrent && (loaded.source || loaded.failed)),
    source: isCurrent ? loaded.source : null,
  }
}

function EmployeeCard({ employee, now }: { employee: DashboardEmployee; now: Date }) {
  const { loading, source } = useScreenshot(employee.latestScreenshot)
  const primaryLabel = employee.isOnline && employee.machineState !== 'Normal'
    ? machineLabels[employee.machineState]
    : humanLabels[employee.humanState]

  return (
    <article className="employee-card" data-online={employee.isOnline}>
      <header className="employee-card-header">
        <div className="employee-identity">
          <span className="avatar" aria-hidden="true">{getInitials(employee.name)}</span>
          <div>
            <h2>{employee.name}</h2>
            <p>{employee.department ?? 'Отдел не указан'}</p>
          </div>
        </div>
        <span className={`presence ${employee.isOnline ? 'is-online' : 'is-offline'}`}>
          <i aria-hidden="true" />
          {employee.isOnline ? 'В сети' : 'Не в сети'}
        </span>
      </header>

      <div className="employee-card-body">
        <div className="current-state">
          <div className="application-heading">
            <Icon name="monitor" />
            <div>
              <span>Сейчас</span>
              <strong title={employee.currentApplication ?? undefined}>
                {employee.currentApplication ?? (employee.isOnline ? 'Нет активного окна' : 'Нет связи')}
              </strong>
            </div>
          </div>
          <p className="window-title" title={employee.currentWindowTitle ?? undefined}>
            {employee.currentWindowTitle ?? employee.currentProcess ?? 'Данные появятся после heartbeat'}
          </p>
          <div className="state-timecode">
            <span>{primaryLabel}</span>
            <time dateTime={employee.primaryStateStartedAtUtc ?? undefined}>
              {formatTimecode(employee.primaryStateStartedAtUtc, now)}
            </time>
          </div>
        </div>

        <figure className="screenshot-frame">
          {source ? (
            <img
              alt={`Последний снимок экрана сотрудника ${employee.name}`}
              height={employee.latestScreenshot?.height}
              loading="lazy"
              src={source}
              width={employee.latestScreenshot?.width}
            />
          ) : (
            <div className={loading ? 'screenshot-empty is-loading' : 'screenshot-empty'}>
              <Icon name="camera" size={24} />
              <span>{loading ? 'Загружаем снимок…' : 'Снимков пока нет'}</span>
            </div>
          )}
          <figcaption>
            {employee.latestScreenshot
              ? `Последний снимок · ${formatMoment(employee.latestScreenshot.timestampUtc)}`
              : 'Последний снимок'}
          </figcaption>
        </figure>
      </div>

      <div className="state-channels" aria-label="Состояния человека и компьютера">
        <div className={`state-channel human-${employee.humanState.toLowerCase()}`}>
          <span>Человек</span>
          <strong>{humanLabels[employee.humanState]}</strong>
          <time>{formatTimecode(employee.humanStateStartedAtUtc, now)}</time>
        </div>
        <div className={`state-channel machine-${employee.machineState.toLowerCase()}`}>
          <span>Машина</span>
          <strong>{machineLabels[employee.machineState]}</strong>
          <time>{formatTimecode(employee.machineStateStartedAtUtc, now)}</time>
        </div>
      </div>

      <footer className="employee-card-footer">
        <span><Icon name="monitor" size={16} /> {employee.computerName ?? 'Компьютер не подключён'}</span>
        <span><Icon name="clock" size={16} /> Heartbeat: {formatMoment(employee.lastHeartbeatAtUtc)}</span>
      </footer>
    </article>
  )
}

function DashboardSkeleton() {
  return (
    <div className="employee-grid" aria-label="Загрузка сотрудников" aria-busy="true">
      {[0, 1, 2, 3].map((item) => <div className="employee-card skeleton-card" key={item} />)}
    </div>
  )
}

export function DashboardPage() {
  const [data, setData] = useState<DashboardData | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [now, setNow] = useState(() => new Date())

  const load = useCallback(async (signal?: AbortSignal, manual = false) => {
    if (manual) setRefreshing(true)
    try {
      const next = await getDashboard(signal)
      setData(next)
      setError(null)
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') return
      setError(caught instanceof ApiError ? caught.message : 'Нет связи с сервером. Проверяем снова каждые 15 секунд.')
    } finally {
      setLoading(false)
      if (manual) setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const initialLoad = window.setTimeout(() => void load(controller.signal), 0)
    const polling = window.setInterval(() => {
      if (document.visibilityState === 'visible') void load(controller.signal)
    }, 15_000)
    const clock = window.setInterval(() => setNow(new Date()), 1_000)
    const onVisibilityChange = () => {
      if (document.visibilityState === 'visible') void load(controller.signal)
    }
    document.addEventListener('visibilitychange', onVisibilityChange)
    return () => {
      controller.abort()
      window.clearTimeout(initialLoad)
      window.clearInterval(polling)
      window.clearInterval(clock)
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [load])

  const summary = useMemo(() => {
    const employees = data?.employees ?? []
    return {
      all: employees.length,
      online: employees.filter((item) => item.isOnline).length,
      machineWork: employees.filter((item) => item.isOnline && item.machineState !== 'Normal').length,
      attention: employees.filter((item) => !item.isOnline || item.humanState === 'Locked').length,
    }
  }, [data])

  return (
    <>
        <header className="dashboard-header">
          <div>
            <p className="eyebrow"><span className="live-dot" aria-hidden="true" /> Прямой эфир отдела</p>
            <h1>Монтажная диспетчерская</h1>
            <p>Человек и машина учитываются раздельно — без потери рендера во время простоя.</p>
          </div>
          <div className="header-actions">
            <button
              className="secondary-button"
              disabled={refreshing}
              onClick={() => void load(undefined, true)}
              type="button"
            >
              <Icon name="refresh" /> {refreshing ? 'Обновляем…' : 'Обновить'}
            </button>
          </div>
        </header>

        <section className="summary-strip" aria-label="Оперативная сводка">
          <div className="summary-title"><span>Оперативная сводка</span><strong>{formatMoment(data?.generatedAtUtc ?? null)}</strong></div>
          <dl>
            <div><dt>В отделе</dt><dd>{summary.all}</dd></div>
            <div><dt>В сети</dt><dd>{summary.online}</dd></div>
            <div><dt>Машинная работа</dt><dd>{summary.machineWork}</dd></div>
            <div><dt>Требуют внимания</dt><dd>{summary.attention}</dd></div>
          </dl>
        </section>

        {error && (
          <div className="connection-warning" role="status">
            <span><strong>Данные могут быть неактуальны.</strong> {error}</span>
            <button onClick={() => void load(undefined, true)} type="button">Повторить</button>
          </div>
        )}

        <section className="employee-section" aria-labelledby="employees-title">
          <div className="section-heading">
            <div><p className="eyebrow">Текущая смена</p><h2 id="employees-title">Сотрудники</h2></div>
            <p>Автообновление каждые 15 секунд</p>
          </div>
          {loading && !data ? (
            <DashboardSkeleton />
          ) : data?.employees.length ? (
            <div className="employee-grid">
              {data.employees.map((employee) => (
                <EmployeeCard employee={employee} key={employee.employeeId} now={now} />
              ))}
            </div>
          ) : (
            <div className="empty-state">
              <Icon name="users" size={30} />
              <h3>Сотрудников пока нет</h3>
              <p>Создайте сотрудника и подключите его компьютер — карточка появится здесь автоматически.</p>
            </div>
          )}
        </section>
    </>
  )
}
