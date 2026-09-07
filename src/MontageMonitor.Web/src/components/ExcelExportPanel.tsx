import { useMemo, useState } from 'react'
import { ApiError, downloadExcelReport } from '../api'
import { toReportQuery, type DateRange } from '../reporting'
import type { Employee } from '../types'
import { Icon } from './Icon'

interface ExcelExportPanelProps {
  employees: Employee[]
  range: DateRange
}

const sheets = ['SUMMARY', 'TIMELINE', 'APPLICATIONS', 'RENDERS', 'IDLE']

export function ExcelExportPanel({ employees, range }: ExcelExportPanelProps) {
  const [selectedIds, setSelectedIds] = useState<Set<string> | null>(null)
  const [downloading, setDownloading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const selected = useMemo(
    () => selectedIds ?? new Set(employees.map((employee) => employee.id)),
    [employees, selectedIds],
  )

  function toggleEmployee(id: string) {
    const next = new Set(selected)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    setSelectedIds(next)
    setError(null)
  }

  async function download() {
    if (!selected.size) {
      setError('Выберите хотя бы одного сотрудника.')
      return
    }

    setDownloading(true)
    setError(null)
    try {
      const blob = await downloadExcelReport(toReportQuery(range), [...selected])
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `MontageMonitor_${range.from}_${range.to}.xlsx`
      document.body.append(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
    } catch (caught: unknown) {
      setError(caught instanceof ApiError
        ? caught.message
        : 'Не удалось подготовить Excel. Проверьте соединение и повторите скачивание.')
    } finally {
      setDownloading(false)
    }
  }

  return (
    <section className="excel-export" aria-labelledby="excel-export-title">
      <div className="excel-export-copy">
        <span className="excel-file-mark" aria-hidden="true"><Icon name="file" size={24} /></span>
        <div>
          <p className="eyebrow">XLSX · готово для анализа</p>
          <h2 id="excel-export-title">Скачать полный отчёт</h2>
          <p>Период берётся из фильтра выше. Даты и длительности останутся числами Excel — их можно сортировать и суммировать.</p>
        </div>
      </div>
      <div className="excel-sheet-strip" aria-label="Листы в файле">
        {sheets.map((sheet) => <span key={sheet}>{sheet}</span>)}
      </div>
      <details className="employee-picker">
        <summary>
          Сотрудники
          <span>{selected.size} из {employees.length}</span>
        </summary>
        <div className="employee-picker-actions">
          <button className="secondary-button" onClick={() => setSelectedIds(null)} type="button">Выбрать всех</button>
          <button className="secondary-button" onClick={() => setSelectedIds(new Set())} type="button">Очистить</button>
        </div>
        <div className="employee-check-list">
          {employees.map((employee) => (
            <label key={employee.id}>
              <input
                checked={selected.has(employee.id)}
                onChange={() => toggleEmployee(employee.id)}
                type="checkbox"
              />
              <span>{employee.name}</span>
              {employee.department && <small>{employee.department}</small>}
            </label>
          ))}
        </div>
      </details>
      <div className="excel-export-action">
        <p aria-live="polite" className={error ? 'excel-export-error' : ''}>
          {error ?? `${range.from} — ${range.to} · ${selected.size} сотрудников`}
        </p>
        <button
          className="primary-button"
          disabled={downloading || !employees.length}
          onClick={download}
          type="button"
        >
          <Icon name="file" size={20} />
          {downloading ? 'Готовим Excel…' : 'Скачать Excel'}
        </button>
      </div>
    </section>
  )
}
