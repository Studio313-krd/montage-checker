import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  addScreenshotPrivacyProcess,
  removeScreenshotPrivacyProcess,
  updateEmployee,
  updateScreenshotSettings,
} from '../api'
import type { Employee, ScreenshotPrivacyProcess, ScreenshotSettings } from '../types'
import { Icon } from './Icon'

function messageFrom(error: unknown): string {
  return error instanceof Error ? error.message : 'Не удалось сохранить настройки. Повторите попытку.'
}

function ScheduleDialog({ employee, onClose, onSaved }: {
  employee: Employee
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [enabled, setEnabled] = useState(employee.screenshotEnabled)
  const [interval, setInterval] = useState(employee.screenshotIntervalMinutes)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [onClose])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await updateEmployee(employee.id, {
        name: employee.name,
        login: employee.login,
        department: employee.department,
        isActive: employee.isActive,
        screenshotEnabled: enabled,
        screenshotIntervalMinutes: interval,
        idleThresholdSeconds: employee.idleThresholdSeconds,
      })
      await onSaved()
      onClose()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="management-modal-backdrop" onMouseDown={(event) => {
      if (event.currentTarget === event.target) onClose()
    }}>
      <section aria-labelledby="schedule-title" aria-modal="true" className="management-modal screenshot-schedule-modal" role="dialog">
        <header><div><p className="eyebrow">Расписание кадров</p><h2 id="schedule-title">{employee.name}</h2></div><button aria-label="Закрыть окно" className="icon-button" onClick={onClose} type="button"><Icon name="x" /></button></header>
        <form className="management-form" onSubmit={(event) => void submit(event)}>
          <div className="management-switches screenshot-switch"><label><input autoFocus checked={enabled} onChange={(event) => setEnabled(event.target.checked)} type="checkbox" /><span><strong>Делать скриншоты</strong><small>Снимки создаются только в активной и разблокированной сессии Windows.</small></span></label></div>
          <label className="management-single-field screenshot-interval-field"><span>Интервал между снимками</span><select disabled={!enabled} onChange={(event) => setInterval(Number(event.target.value))} value={interval}><option value={1}>Каждую минуту</option><option value={2}>Каждые 2 минуты</option><option value={5}>Каждые 5 минут</option><option value={10}>Каждые 10 минут</option><option value={15}>Каждые 15 минут</option><option value={30}>Каждые 30 минут</option><option value={60}>Каждый час</option></select></label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <footer className="management-form-actions"><button className="secondary-button" onClick={onClose} type="button">Отмена</button><button className="primary-button" disabled={saving} type="submit">{saving ? 'Сохраняем…' : 'Сохранить расписание'}</button></footer>
        </form>
      </section>
    </div>
  )
}

export function ScreenshotManagement({ employees, onReload, privacyProcesses, settings }: {
  employees: Employee[]
  onReload: () => Promise<void>
  privacyProcesses: ScreenshotPrivacyProcess[]
  settings: ScreenshotSettings
}) {
  const [draft, setDraft] = useState(settings)
  const [scheduleEmployee, setScheduleEmployee] = useState<Employee | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [processName, setProcessName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [privacyBusy, setPrivacyBusy] = useState(false)
  const scheduleTriggerRef = useRef<HTMLButtonElement | null>(null)

  function closeSchedule() {
    setScheduleEmployee(null)
    window.requestAnimationFrame(() => scheduleTriggerRef.current?.focus())
  }

  async function saveSettings(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    setNotice(null)
    try {
      await updateScreenshotSettings(draft)
      setNotice('Общие настройки скриншотов сохранены. Agent получит их не позднее чем через 5 минут.')
      await onReload()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  async function addPrivacy(event: FormEvent) {
    event.preventDefault()
    setPrivacyBusy(true)
    setError(null)
    setNotice(null)
    try {
      await addScreenshotPrivacyProcess(processName, displayName)
      setProcessName('')
      setDisplayName('')
      setNotice('Приложение добавлено в список приватности.')
      await onReload()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setPrivacyBusy(false)
    }
  }

  async function removePrivacy(rule: ScreenshotPrivacyProcess) {
    if (!window.confirm(`Разрешить скриншоты, когда открыто приложение «${rule.displayName}»?`)) return
    setPrivacyBusy(true)
    setError(null)
    setNotice(null)
    try {
      await removeScreenshotPrivacyProcess(rule.id)
      setNotice('Приложение удалено из списка приватности.')
      await onReload()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setPrivacyBusy(false)
    }
  }

  return (
    <div className="screenshot-settings-stack">
      {error && <div className="connection-warning" role="alert"><span><strong>Настройки не сохранены.</strong> {error}</span><button onClick={() => setError(null)} type="button">Закрыть</button></div>}
      {notice && <div className="management-notice" role="status"><Icon name="check" /> {notice}</div>}

      <section className="management-surface screenshot-global-surface">
        <header><div><p className="eyebrow">Общие правила</p><h2>Кадр и хранение</h2><p>Эти параметры применяются ко всем Agent. Частота задаётся отдельно для каждого сотрудника.</p></div></header>
        <form className="screenshot-settings-form" onSubmit={(event) => void saveSettings(event)}>
          <label><span className="screenshot-setting-number">01</span><span><strong>Какие мониторы</strong><small>Отдельный файл для каждого выбранного экрана.</small></span><select onChange={(event) => setDraft({ ...draft, captureMode: event.target.value as ScreenshotSettings['captureMode'] })} value={draft.captureMode}><option value="PrimaryMonitor">Только основной монитор</option><option value="AllMonitors">Все подключённые мониторы</option></select></label>
          <label><span className="screenshot-setting-number">02</span><span><strong>Максимальная ширина</strong><small>Большие кадры уменьшаются перед отправкой.</small></span><select onChange={(event) => setDraft({ ...draft, maxWidth: Number(event.target.value) })} value={draft.maxWidth}><option value={1280}>1280 px · экономно</option><option value={1600}>1600 px · оптимально</option><option value={1920}>1920 px · подробно</option><option value={2560}>2560 px · очень подробно</option><option value={3840}>3840 px · 4K</option></select></label>
          <label><span className="screenshot-setting-number">03</span><span><strong>Качество JPEG</strong><small>Выше качество — больше место на сервере.</small></span><input aria-label="Качество JPEG" max={65} min={55} onChange={(event) => setDraft({ ...draft, jpegQuality: event.target.valueAsNumber })} type="range" value={draft.jpegQuality} /><output>{draft.jpegQuality}%</output></label>
          <label><span className="screenshot-setting-number">04</span><span><strong>Хранить на сервере</strong><small>Более старые файлы удаляются автоматически.</small></span><select onChange={(event) => setDraft({ ...draft, retentionDays: Number(event.target.value) })} value={draft.retentionDays}><option value={7}>7 дней</option><option value={14}>14 дней</option><option value={30}>30 дней</option><option value={60}>60 дней</option><option value={90}>90 дней</option><option value={180}>180 дней</option><option value={365}>1 год</option></select></label>
          <footer><span>Agent проверяет новые настройки каждые 5 минут.</span><button className="primary-button" disabled={saving} type="submit">{saving ? 'Сохраняем…' : 'Сохранить общие настройки'}</button></footer>
        </form>
      </section>

      <section className="management-surface">
        <header><div><p className="eyebrow">Персональное расписание</p><h2>Частота по сотрудникам</h2><p>Отключите снимки полностью или задайте интервал от одной минуты до часа.</p></div></header>
        <div className="screenshot-employee-list">{employees.map((employee) => <article key={employee.id}><div><span className={`management-status ${employee.screenshotEnabled ? 'status-online' : 'is-inactive'}`}>{employee.screenshotEnabled ? 'Включены' : 'Выключены'}</span><h3>{employee.name}</h3><p>{employee.department ?? employee.login}</p></div><div className="screenshot-frequency"><span>Период</span><strong>{employee.screenshotEnabled ? `${employee.screenshotIntervalMinutes} мин` : '—'}</strong></div><button className="secondary-button" onClick={(event) => { scheduleTriggerRef.current = event.currentTarget; setScheduleEmployee(employee) }} type="button">Настроить</button></article>)}</div>
      </section>

      <section className="management-surface privacy-surface">
        <header><div><p className="eyebrow">Приватность</p><h2>Не снимать эти приложения</h2><p>Если приложение активно, Agent отправит только отметку о пропуске без изображения.</p></div></header>
        <form className="privacy-add-form" onSubmit={(event) => void addPrivacy(event)}><label><span>Имя процесса *</span><input maxLength={255} onChange={(event) => setProcessName(event.target.value)} placeholder="Например, KeePass.exe" required value={processName} /></label><label><span>Название для списка *</span><input maxLength={255} onChange={(event) => setDisplayName(event.target.value)} placeholder="Менеджер паролей" required value={displayName} /></label><button className="primary-button" disabled={privacyBusy} type="submit">Добавить исключение</button></form>
        {privacyProcesses.length ? <ul className="privacy-list">{privacyProcesses.map((rule) => <li key={rule.id}><div><strong>{rule.displayName}</strong><code>{rule.processName}</code></div><button className="secondary-button" disabled={privacyBusy} onClick={() => void removePrivacy(rule)} type="button">Удалить</button></li>)}</ul> : <div className="privacy-empty"><Icon name="check" /><span><strong>Исключений пока нет</strong><small>Снимки разрешены для всех активных приложений.</small></span></div>}
      </section>

      {scheduleEmployee && <ScheduleDialog employee={scheduleEmployee} onClose={closeSchedule} onSaved={onReload} />}
    </div>
  )
}
