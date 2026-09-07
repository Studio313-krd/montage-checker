import type { ApplicationClassification, HumanState, MachineState, ProcessingType, ReportQuery } from './types'

export interface DateRange {
  from: string
  to: string
}

export const humanLabels: Record<HumanState, string> = {
  Active: 'Активная работа',
  Idle: 'Простой',
  Locked: 'Заблокирован',
  Offline: 'Нет связи',
}

export const machineLabels: Record<MachineState, string> = {
  Normal: 'Обычная работа',
  Render: 'Рендер',
  Proxy: 'Прокси',
  BackgroundProcessing: 'Фоновая обработка',
}

export const classificationLabels: Record<ApplicationClassification, string> = {
  Productive: 'Продуктивное',
  Neutral: 'Нейтральное',
  Unproductive: 'Непродуктивное',
  Ignored: 'Игнорируется',
}

export const processingLabels: Record<ProcessingType, string> = {
  Render: 'Рендер',
  Proxy: 'Прокси',
  Background: 'Фоновая обработка',
}

function dateInputValue(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function presetRange(preset: 'today' | 'yesterday' | 'week' | 'previousWeek' | 'month'): DateRange {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const from = new Date(today)
  const to = new Date(today)
  if (preset === 'yesterday') {
    from.setDate(from.getDate() - 1)
    to.setDate(to.getDate() - 1)
  } else if (preset === 'week' || preset === 'previousWeek') {
    const mondayOffset = (from.getDay() + 6) % 7
    from.setDate(from.getDate() - mondayOffset - (preset === 'previousWeek' ? 7 : 0))
    to.setTime(from.getTime())
    to.setDate(to.getDate() + 6)
  } else if (preset === 'month') {
    from.setDate(1)
  }
  return { from: dateInputValue(from), to: dateInputValue(to) }
}

export function toReportQuery(range: DateRange, employeeId = ''): ReportQuery {
  const start = new Date(`${range.from}T00:00:00`)
  const end = new Date(`${range.to}T00:00:00`)
  end.setDate(end.getDate() + 1)
  return {
    employeeId: employeeId || undefined,
    fromUtc: start.toISOString(),
    toUtc: end.toISOString(),
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
  }
}

export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const rest = total % 60
  return [hours, minutes, rest].map((part) => String(part).padStart(2, '0')).join(':')
}

export function formatMoment(value: string | null, includeDate = false): string {
  if (!value) return '—'
  return new Intl.DateTimeFormat('ru-RU', {
    day: includeDate ? '2-digit' : undefined,
    month: includeDate ? 'short' : undefined,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(value))
}

export function formatFileSize(bytes: number | null): string {
  if (bytes === null) return '—'
  if (bytes < 1_048_576) return `${Math.round(bytes / 1_024)} КБ`
  if (bytes < 1_073_741_824) return `${(bytes / 1_048_576).toFixed(1)} МБ`
  return `${(bytes / 1_073_741_824).toFixed(1)} ГБ`
}
