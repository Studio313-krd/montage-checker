import type { FormEvent } from 'react'
import { presetRange, type DateRange } from '../reporting'
import type { Employee } from '../types'

interface ReportControlsProps {
  application?: string
  employeeId: string
  employeeRequired?: boolean
  employees: Employee[]
  loading: boolean
  onApplicationChange?: (value: string) => void
  onEmployeeChange: (value: string) => void
  onRangeChange: (range: DateRange) => void
  onSubmit: () => void
  range: DateRange
}

const presets = [
  { key: 'today', label: 'Сегодня' },
  { key: 'yesterday', label: 'Вчера' },
  { key: 'week', label: 'Эта неделя' },
  { key: 'previousWeek', label: 'Прошлая неделя' },
  { key: 'month', label: 'Этот месяц' },
] as const

export function ReportControls({
  application,
  employeeId,
  employeeRequired,
  employees,
  loading,
  onApplicationChange,
  onEmployeeChange,
  onRangeChange,
  onSubmit,
  range,
}: ReportControlsProps) {
  function submit(event: FormEvent) {
    event.preventDefault()
    onSubmit()
  }

  function applyPreset(key: typeof presets[number]['key']) {
    onRangeChange(presetRange(key))
  }

  return (
    <form className="report-controls" onSubmit={submit}>
      <div className="preset-row" aria-label="Быстрый выбор периода">
        {presets.map((preset) => (
          <button key={preset.key} onClick={() => applyPreset(preset.key)} type="button">
            {preset.label}
          </button>
        ))}
      </div>
      <div className="filter-grid">
        <label>
          <span>Сотрудник</span>
          <select
            required={employeeRequired}
            value={employeeId}
            onChange={(event) => onEmployeeChange(event.target.value)}
          >
            {!employeeRequired && <option value="">Все сотрудники</option>}
            {employees.map((employee) => <option key={employee.id} value={employee.id}>{employee.name}</option>)}
          </select>
        </label>
        <label>
          <span>Дата с</span>
          <input
            max={range.to}
            onChange={(event) => onRangeChange({ ...range, from: event.target.value })}
            required
            type="date"
            value={range.from}
          />
        </label>
        <label>
          <span>Дата по</span>
          <input
            min={range.from}
            onChange={(event) => onRangeChange({ ...range, to: event.target.value })}
            required
            type="date"
            value={range.to}
          />
        </label>
        {onApplicationChange && (
          <label>
            <span>Приложение</span>
            <input
              onChange={(event) => onApplicationChange(event.target.value)}
              placeholder="Например, Premiere"
              type="search"
              value={application}
            />
          </label>
        )}
        <button className="primary-button filter-submit" disabled={loading} type="submit">
          {loading ? 'Загружаем…' : 'Показать'}
        </button>
      </div>
    </form>
  )
}

interface ReportHeadingProps {
  eyebrow: string
  title: string
  description: string
}

export function ReportHeading({ eyebrow, title, description }: ReportHeadingProps) {
  return (
    <header className="dashboard-header report-header">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
    </header>
  )
}

export function ReportError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="connection-warning" role="alert">
      <span><strong>Отчёт не обновлён.</strong> {message}</span>
      <button onClick={onRetry} type="button">Повторить</button>
    </div>
  )
}

export function ReportEmpty({ children }: { children: string }) {
  return (
    <div className="empty-state report-empty">
      <h2>Нет данных за выбранный период</h2>
      <p>{children}</p>
    </div>
  )
}
