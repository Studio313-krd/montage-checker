import { useEffect, useRef, useState } from 'react'
import { ApiError, getEmployees, getScreenshot, getScreenshotGallery } from '../api'
import { formatMoment, humanLabels, machineLabels, presetRange, toReportQuery, type DateRange } from '../reporting'
import type { Employee, ScreenshotGallery, ScreenshotGalleryItem } from '../types'
import { Icon } from './Icon'
import { ReportControls, ReportEmpty, ReportError, ReportHeading } from './ReportControls'

function useScreenshotSource(contentUrl: string) {
  const [source, setSource] = useState<string | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    let objectUrl: string | null = null
    getScreenshot(contentUrl, controller.signal).then((blob) => {
      objectUrl = URL.createObjectURL(blob)
      setSource(objectUrl)
    }).catch((caught: unknown) => {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) setSource(null)
    })
    return () => {
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [contentUrl])
  return source
}

function ScreenshotCard({ item, onOpen }: { item: ScreenshotGalleryItem; onOpen: () => void }) {
  const source = useScreenshotSource(item.contentUrl)
  return (
    <article className="gallery-card">
      <button aria-label={`Открыть снимок ${item.employeeName} за ${formatMoment(item.timestampUtc, true)}`} onClick={onOpen} type="button">
        <div className="gallery-image">
          {source
            ? <img alt="" height={item.height} loading="lazy" src={source} width={item.width} />
            : <span className="screenshot-empty is-loading"><Icon name="camera" /><small>Загружаем…</small></span>}
        </div>
        <div className="gallery-caption">
          <span><strong>{item.employeeName}</strong><time>{formatMoment(item.timestampUtc, true)}</time></span>
          <b>{item.application ?? 'Приложение не определено'}</b>
          <small>{item.windowTitle ?? item.computerName}</small>
          <span className="gallery-states">
            <i>{humanLabels[item.humanState]}</i><i>{machineLabels[item.machineState]}</i>
          </span>
        </div>
      </button>
    </article>
  )
}

function ScreenshotModal({
  index,
  items,
  onClose,
  onSelect,
}: {
  index: number
  items: ScreenshotGalleryItem[]
  onClose: () => void
  onSelect: (index: number) => void
}) {
  const item = items[index]
  const source = useScreenshotSource(item.contentUrl)
  const closeRef = useRef<HTMLButtonElement>(null)
  const modalRef = useRef<HTMLDivElement>(null)
  useEffect(() => {
    closeRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
      if (event.key === 'ArrowLeft' && index > 0) onSelect(index - 1)
      if (event.key === 'ArrowRight' && index < items.length - 1) onSelect(index + 1)
      if (event.key === 'Tab') {
        const controls = modalRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')
        if (!controls?.length) return
        const first = controls[0]
        const last = controls[controls.length - 1]
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [index, items.length, onClose, onSelect])

  return (
    <div aria-label={`Снимок экрана сотрудника ${item.employeeName}`} aria-modal="true" className="screenshot-modal" ref={modalRef} role="dialog">
      <div className="modal-toolbar">
        <div><strong>{item.employeeName}</strong><span>{formatMoment(item.timestampUtc, true)} · {item.application ?? 'Без приложения'}</span></div>
        <button aria-label="Закрыть просмотр" className="icon-button icon-button-on-dark" onClick={onClose} ref={closeRef} type="button"><Icon name="x" /></button>
      </div>
      <div className="modal-canvas">{source ? <img alt={`Снимок экрана: ${item.windowTitle ?? item.application ?? item.employeeName}`} src={source} /> : <span>Загружаем изображение…</span>}</div>
      <div className="modal-details">
        <button disabled={index === 0} onClick={() => onSelect(index - 1)} type="button">← Предыдущий</button>
        <p>{item.windowTitle ?? 'Заголовок окна не передан'}<small>{humanLabels[item.humanState]} · {machineLabels[item.machineState]}</small></p>
        <button disabled={index === items.length - 1} onClick={() => onSelect(index + 1)} type="button">Следующий →</button>
      </div>
    </div>
  )
}

export function ScreenshotGalleryPage() {
  const [employees, setEmployees] = useState<Employee[]>([])
  const [employeeId, setEmployeeId] = useState('')
  const [range, setRange] = useState<DateRange>(() => presetRange('today'))
  const [application, setApplication] = useState('')
  const [applied, setApplied] = useState(() => ({ employeeId: '', range: presetRange('today'), application: '' }))
  const [page, setPage] = useState(1)
  const [data, setData] = useState<ScreenshotGallery | null>(null)
  const [selected, setSelected] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const previousFocus = useRef<HTMLElement | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getEmployees(controller.signal).then(setEmployees).catch(() => setEmployees([]))
    return () => controller.abort()
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    getScreenshotGallery(
      toReportQuery(applied.range, applied.employeeId),
      page,
      applied.application,
      controller.signal,
    ).then((next) => { setData(next); setError(null) })
      .catch((caught: unknown) => {
        if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught instanceof ApiError ? caught.message : 'Не удалось загрузить галерею.')
        }
      }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [applied, page, retry])

  const apply = () => {
    setLoading(true)
    setPage(1)
    setApplied({ employeeId, range: { ...range }, application })
  }
  const pageCount = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1
  const openModal = (index: number) => {
    previousFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    setSelected(index)
  }
  const closeModal = () => {
    setSelected(null)
    window.setTimeout(() => previousFocus.current?.focus(), 0)
  }

  return (
    <>
      <ReportHeading
        description="Снимки открываются только после проверки роли и доступных сотрудников. В базе остаются metadata и защищённая ссылка, а не публичный путь к файлу."
        eyebrow="Визуальный контроль"
        title="Скриншоты"
      />
      <ReportControls
        application={application}
        employeeId={employeeId}
        employees={employees}
        loading={loading}
        onApplicationChange={setApplication}
        onEmployeeChange={setEmployeeId}
        onRangeChange={setRange}
        onSubmit={apply}
        range={range}
      />
      {error && <ReportError message={error} onRetry={() => setRetry((value) => value + 1)} />}
      {loading && !data ? <div className="report-skeleton" aria-label="Загрузка скриншотов" aria-busy="true" />
        : data?.screenshots.length ? (
          <section className="gallery-section" aria-labelledby="gallery-title">
            <div className="section-heading"><div><p className="eyebrow">Кадры смены</p><h2 id="gallery-title">Галерея</h2></div><p>{data.totalCount} снимков</p></div>
            <div className="gallery-grid">{data.screenshots.map((item, index) => (
              <ScreenshotCard item={item} key={item.id} onOpen={() => openModal(index)} />
            ))}</div>
            <nav aria-label="Страницы галереи" className="pagination">
              <button disabled={page <= 1 || loading} onClick={() => { setLoading(true); setPage((value) => value - 1) }} type="button">← Назад</button>
              <span>Страница {page} из {pageCount}</span>
              <button disabled={page >= pageCount || loading} onClick={() => { setLoading(true); setPage((value) => value + 1) }} type="button">Вперёд →</button>
            </nav>
          </section>
        ) : <ReportEmpty>Снимков с такими фильтрами нет. Проверьте период, приложение или настройку сотрудника.</ReportEmpty>}
      {selected !== null && data && (
        <ScreenshotModal index={selected} items={data.screenshots} onClose={closeModal} onSelect={setSelected} />
      )}
    </>
  )
}
