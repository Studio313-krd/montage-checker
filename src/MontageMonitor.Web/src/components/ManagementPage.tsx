import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import {
  ApiError,
  changeUserPassword,
  createEmployee,
  createEnrollmentToken,
  createUser,
  getEmployeeOperatorPins,
  getManagedComputers,
  getManagedEmployees,
  getScreenshotPrivacyProcesses,
  getScreenshotSettings,
  getUsers,
  revokeAgent,
  regenerateEmployeeOperatorPin,
  updateEmployee,
  updateUser,
} from '../api'
import type {
  AdminUser,
  AuthUser,
  Employee,
  EmployeeInput,
  EnrollmentToken,
  ManagedComputer,
  ScreenshotPrivacyProcess,
  ScreenshotSettings,
  UserInput,
  UserRole,
} from '../types'
import { Icon } from './Icon'
import { ScreenshotManagement } from './ScreenshotManagement'

type ManagementTab = 'employees' | 'computers' | 'screenshots' | 'users'

const roleLabels: Record<UserRole, string> = {
  Owner: 'Владелец',
  Admin: 'Администратор',
  Manager: 'Руководитель',
  Viewer: 'Наблюдатель',
}

const agentStatusLabels: Record<NonNullable<ManagedComputer['agentStatus']>, string> = {
  Online: 'В сети',
  Offline: 'Не в сети',
  Revoked: 'Отключён',
}

function messageFrom(error: unknown): string {
  return error instanceof ApiError ? error.message : 'Не удалось выполнить действие. Проверьте соединение и повторите попытку.'
}

function formatMoment(value: string | null): string {
  if (!value) return 'Нет данных'
  return new Intl.DateTimeFormat('ru-RU', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value))
}

function Modal({ children, onClose, title }: { children: ReactNode; onClose: () => void; title: string }) {
  useEffect(() => {
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [onClose])

  return (
    <div className="management-modal-backdrop" onMouseDown={(event) => {
      if (event.currentTarget === event.target) onClose()
    }}>
      <section aria-labelledby="management-modal-title" aria-modal="true" className="management-modal" role="dialog">
        <header>
          <div><p className="eyebrow">Настройка</p><h2 id="management-modal-title">{title}</h2></div>
          <button aria-label="Закрыть окно" className="icon-button" onClick={onClose} type="button"><Icon name="x" /></button>
        </header>
        {children}
      </section>
    </div>
  )
}

function EmployeeEditor({ employee, onClose, onSaved }: {
  employee: Employee | null
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [draft, setDraft] = useState<EmployeeInput & { isActive: boolean }>({
    name: employee?.name ?? '',
    login: employee?.login ?? '',
    department: employee?.department ?? '',
    isActive: employee?.isActive ?? true,
    screenshotEnabled: employee?.screenshotEnabled ?? false,
    screenshotIntervalMinutes: employee?.screenshotIntervalMinutes ?? 5,
    idleThresholdSeconds: employee?.idleThresholdSeconds ?? 300,
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const payload = { ...draft, department: draft.department?.trim() || null }
      if (employee) await updateEmployee(employee.id, payload)
      else await createEmployee(payload)
      await onSaved()
      onClose()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal onClose={onClose} title={employee ? 'Изменить сотрудника' : 'Новый сотрудник'}>
      <form className="management-form" onSubmit={(event) => void submit(event)}>
        <div className="management-form-grid">
          <label><span>Имя сотрудника *</span><input autoFocus maxLength={200} onChange={(event) => setDraft({ ...draft, name: event.target.value })} required value={draft.name} /></label>
          <label><span>Логин сотрудника *</span><input autoComplete="off" maxLength={100} onChange={(event) => setDraft({ ...draft, login: event.target.value })} required value={draft.login} /></label>
          <label className="management-form-wide"><span>Отдел</span><input maxLength={200} onChange={(event) => setDraft({ ...draft, department: event.target.value })} placeholder="Например, постпродакшн" value={draft.department ?? ''} /></label>
          <label><span>Скриншот каждые, минут</span><input disabled={!draft.screenshotEnabled} max={60} min={1} onChange={(event) => setDraft({ ...draft, screenshotIntervalMinutes: event.target.valueAsNumber })} required type="number" value={draft.screenshotIntervalMinutes} /></label>
          <label><span>Простой после, секунд</span><input min={0} onChange={(event) => setDraft({ ...draft, idleThresholdSeconds: event.target.valueAsNumber })} required type="number" value={draft.idleThresholdSeconds} /></label>
        </div>
        <div className="management-switches">
          <label><input checked={draft.screenshotEnabled} onChange={(event) => setDraft({ ...draft, screenshotEnabled: event.target.checked })} type="checkbox" /><span><strong>Снимки экрана</strong><small>Agent будет отправлять снимки с заданным интервалом.</small></span></label>
          {employee && <label><input checked={draft.isActive} onChange={(event) => setDraft({ ...draft, isActive: event.target.checked })} type="checkbox" /><span><strong>Сотрудник активен</strong><small>Неактивному сотруднику нельзя выдать новый код Agent.</small></span></label>}
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <footer className="management-form-actions">
          <button className="secondary-button" onClick={onClose} type="button">Отмена</button>
          <button className="primary-button" disabled={saving} type="submit">{saving ? 'Сохраняем…' : 'Сохранить'}</button>
        </footer>
      </form>
    </Modal>
  )
}

function UserEditor({ currentUser, employeeOptions, onClose, onSaved, user }: {
  currentUser: AuthUser
  employeeOptions: Employee[]
  onClose: () => void
  onSaved: () => Promise<void>
  user: AdminUser | null
}) {
  const [draft, setDraft] = useState<UserInput & { isActive: boolean; password: string }>({
    login: user?.login ?? '',
    displayName: user?.displayName ?? '',
    password: '',
    role: user?.role ?? 'Manager',
    isActive: user?.isActive ?? true,
    employeeIds: user?.employeeIds ?? [],
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const scopedRole = draft.role === 'Manager' || draft.role === 'Viewer'

  function changeRole(role: UserRole) {
    setDraft({ ...draft, role, employeeIds: role === 'Owner' || role === 'Admin' ? [] : draft.employeeIds })
  }

  function toggleEmployee(employeeId: string) {
    setDraft({
      ...draft,
      employeeIds: draft.employeeIds.includes(employeeId)
        ? draft.employeeIds.filter((id) => id !== employeeId)
        : [...draft.employeeIds, employeeId],
    })
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      if (user) {
        await updateUser(user.id, draft)
      } else {
        await createUser({ ...draft, password: draft.password })
      }
      await onSaved()
      onClose()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  const roles: UserRole[] = currentUser.role === 'Owner'
    ? ['Owner', 'Admin', 'Manager', 'Viewer']
    : ['Admin', 'Manager', 'Viewer']

  return (
    <Modal onClose={onClose} title={user ? 'Изменить пользователя' : 'Новый пользователь'}>
      <form className="management-form" onSubmit={(event) => void submit(event)}>
        <div className="management-form-grid">
          <label><span>Отображаемое имя *</span><input autoFocus maxLength={200} onChange={(event) => setDraft({ ...draft, displayName: event.target.value })} required value={draft.displayName} /></label>
          <label><span>Логин *</span><input autoComplete="username" maxLength={100} minLength={3} onChange={(event) => setDraft({ ...draft, login: event.target.value })} required value={draft.login} /></label>
          {!user && <label className="management-form-wide"><span>Временный пароль *</span><input autoComplete="new-password" minLength={12} onChange={(event) => setDraft({ ...draft, password: event.target.value })} required type="password" value={draft.password} /><small>Не менее 12 символов: заглавная и строчная буквы, цифра и специальный знак.</small></label>}
          <label className="management-form-wide"><span>Роль</span><select onChange={(event) => changeRole(event.target.value as UserRole)} value={draft.role}>{roles.map((role) => <option key={role} value={role}>{roleLabels[role]}</option>)}</select></label>
        </div>
        {scopedRole && (
          <fieldset className="employee-scope">
            <legend>Доступные сотрудники</legend>
            <p>Если никого не выбрать, пользователь увидит пустую диспетчерскую.</p>
            <div>{employeeOptions.filter((employee) => employee.isActive).map((employee) => <label key={employee.id}><input checked={draft.employeeIds.includes(employee.id)} onChange={() => toggleEmployee(employee.id)} type="checkbox" /><span>{employee.name}<small>{employee.department ?? employee.login}</small></span></label>)}</div>
          </fieldset>
        )}
        {user && <div className="management-switches"><label><input checked={draft.isActive} disabled={user.id === currentUser.id} onChange={(event) => setDraft({ ...draft, isActive: event.target.checked })} type="checkbox" /><span><strong>Учётная запись активна</strong><small>{user.id === currentUser.id ? 'Собственную учётную запись отключить нельзя.' : 'При отключении активные сеансы будут завершены.'}</small></span></label></div>}
        {error && <p className="form-error" role="alert">{error}</p>}
        <footer className="management-form-actions">
          <button className="secondary-button" onClick={onClose} type="button">Отмена</button>
          <button className="primary-button" disabled={saving} type="submit">{saving ? 'Сохраняем…' : 'Сохранить'}</button>
        </footer>
      </form>
    </Modal>
  )
}

function PasswordEditor({ onClose, onSaved, user }: { onClose: () => void; onSaved: () => void; user: AdminUser }) {
  const [password, setPassword] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await changeUserPassword(user.id, password)
      onSaved()
      onClose()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal onClose={onClose} title={`Новый пароль · ${user.displayName}`}>
      <form className="management-form" onSubmit={(event) => void submit(event)}>
        <label className="management-single-field"><span>Новый пароль *</span><input autoFocus autoComplete="new-password" minLength={12} onChange={(event) => setPassword(event.target.value)} required type="password" value={password} /><small>Не менее 12 символов: заглавная и строчная буквы, цифра и специальный знак.</small></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <footer className="management-form-actions"><button className="secondary-button" onClick={onClose} type="button">Отмена</button><button className="primary-button" disabled={saving} type="submit">{saving ? 'Меняем…' : 'Изменить пароль'}</button></footer>
      </form>
    </Modal>
  )
}

function EnrollmentDialog({ employee, onClose }: { employee: Employee; onClose: () => void }) {
  const [hours, setHours] = useState(24)
  const [result, setResult] = useState<EnrollmentToken | null>(null)
  const [loading, setLoading] = useState(false)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function generate() {
    setLoading(true)
    setError(null)
    try {
      setResult(await createEnrollmentToken(employee.id, hours))
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setLoading(false)
    }
  }

  async function copy() {
    if (!result) return
    try {
      await navigator.clipboard.writeText(result.enrollmentToken)
      setCopied(true)
    } catch {
      setError('Браузер не разрешил копирование. Выделите код и скопируйте его вручную.')
    }
  }

  return (
    <Modal onClose={onClose} title={`Подключить Agent · ${employee.name}`}>
      <div className="management-form enrollment-dialog">
        {!result ? <>
          <p>Создайте одноразовый код и передайте его монтажёру вместе с адресом сервера. После первого подключения код перестанет действовать.</p>
          <label className="management-single-field"><span>Срок действия кода</span><select onChange={(event) => setHours(Number(event.target.value))} value={hours}><option value={1}>1 час</option><option value={24}>24 часа</option><option value={72}>3 дня</option><option value={168}>7 дней</option></select></label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <footer className="management-form-actions"><button className="secondary-button" onClick={onClose} type="button">Отмена</button><button className="primary-button" disabled={loading} onClick={() => void generate()} type="button">{loading ? 'Создаём…' : 'Создать код'}</button></footer>
        </> : <>
          <p className="enrollment-success"><Icon name="check" /> Код готов. Он показан только сейчас.</p>
          <div className="enrollment-token"><code>{result.enrollmentToken}</code><button aria-label="Скопировать код" className="icon-button" onClick={() => void copy()} type="button"><Icon name={copied ? 'check' : 'copy'} /></button></div>
          {error && <p className="form-error" role="alert">{error}</p>}
          <dl className="enrollment-details"><div><dt>Адрес сервера</dt><dd>{window.location.origin}</dd></div><div><dt>Действует до</dt><dd>{formatMoment(result.expiresAtUtc)}</dd></div></dl>
          <footer className="management-form-actions"><button className="primary-button" onClick={onClose} type="button">Готово</button></footer>
        </>}
      </div>
    </Modal>
  )
}

export function ManagementPage({ currentUser }: { currentUser: AuthUser }) {
  const [tab, setTab] = useState<ManagementTab>('employees')
  const [employees, setEmployees] = useState<Employee[]>([])
  const [operatorPins, setOperatorPins] = useState<Record<string, string>>({})
  const [computers, setComputers] = useState<ManagedComputer[]>([])
  const [users, setUsers] = useState<AdminUser[]>([])
  const [screenshotSettings, setScreenshotSettings] = useState<ScreenshotSettings | null>(null)
  const [privacyProcesses, setPrivacyProcesses] = useState<ScreenshotPrivacyProcess[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [employeeEditor, setEmployeeEditor] = useState<Employee | null | undefined>(undefined)
  const [userEditor, setUserEditor] = useState<AdminUser | null | undefined>(undefined)
  const [passwordUser, setPasswordUser] = useState<AdminUser | null>(null)
  const [enrollmentEmployee, setEnrollmentEmployee] = useState<Employee | null>(null)
  const [revoking, setRevoking] = useState<string | null>(null)
  const [regeneratingPin, setRegeneratingPin] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const [nextEmployees, nextPins, nextComputers, nextUsers, nextScreenshotSettings, nextPrivacyProcesses] = await Promise.all([
        getManagedEmployees(),
        getEmployeeOperatorPins(),
        getManagedComputers(),
        getUsers(),
        getScreenshotSettings(),
        getScreenshotPrivacyProcesses(),
      ])
      setEmployees(nextEmployees)
      setOperatorPins(Object.fromEntries(nextPins.map((item) => [item.employeeId, item.operatorPin])))
      setComputers(nextComputers)
      setUsers(nextUsers)
      setScreenshotSettings(nextScreenshotSettings)
      setPrivacyProcesses(nextPrivacyProcesses)
      setError(null)
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    const initialLoad = window.setTimeout(() => void load(), 0)
    return () => window.clearTimeout(initialLoad)
  }, [load])

  const counts = useMemo(() => ({
    activeEmployees: employees.filter((item) => item.isActive).length,
    onlineComputers: computers.filter((item) => item.agentStatus === 'Online').length,
    activeUsers: users.filter((item) => item.isActive).length,
  }), [computers, employees, users])

  async function handleRevoke(computer: ManagedComputer) {
    if (!computer.agentId || !window.confirm(`Отключить Agent на компьютере «${computer.name}»? Для повторного подключения понадобится новый одноразовый код.`)) return
    setRevoking(computer.agentId)
    setNotice(null)
    try {
      await revokeAgent(computer.agentId)
      setNotice(`Agent на компьютере «${computer.name}» отключён.`)
      await load()
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setRevoking(null)
    }
  }

  async function handleRegeneratePin(employee: Employee) {
    if (!window.confirm(`Создать новый PIN для сотрудника «${employee.name}»? Старый PIN сразу перестанет работать для новых смен.`)) return
    setRegeneratingPin(employee.id)
    setNotice(null)
    try {
      const result = await regenerateEmployeeOperatorPin(employee.id)
      setOperatorPins((current) => ({ ...current, [result.employeeId]: result.operatorPin }))
      setNotice(`Для сотрудника «${employee.name}» создан новый PIN: ${result.operatorPin}`)
    } catch (caught) {
      setError(messageFrom(caught))
    } finally {
      setRegeneratingPin(null)
    }
  }

  return (
    <>
      <header className="dashboard-header management-header">
        <div><p className="eyebrow">Контур доступа</p><h1>Управление</h1><p>Сотрудники, рабочие компьютеры и доступ к диспетчерской — в одном месте.</p></div>
        <div className="header-actions"><button className="secondary-button" disabled={loading} onClick={() => void load()} type="button"><Icon name="refresh" /> {loading ? 'Обновляем…' : 'Обновить'}</button></div>
      </header>

      <section aria-label="Состояние системы" className="management-summary">
        <div><span>Сотрудники</span><strong>{counts.activeEmployees}</strong><small>активных</small></div>
        <div><span>Agent</span><strong>{counts.onlineComputers}</strong><small>сейчас в сети</small></div>
        <div><span>Доступ</span><strong>{counts.activeUsers}</strong><small>учётных записей</small></div>
      </section>

      {error && <div className="connection-warning" role="alert"><span><strong>Действие не выполнено.</strong> {error}</span><button onClick={() => { setError(null); void load() }} type="button">Повторить</button></div>}
      {notice && <div className="management-notice" role="status"><Icon name="check" /> {notice}</div>}

      <div className="management-tabs" role="tablist" aria-label="Разделы управления">
        <button aria-selected={tab === 'employees'} onClick={() => setTab('employees')} role="tab" type="button">Сотрудники <span>{employees.length}</span></button>
        <button aria-selected={tab === 'computers'} onClick={() => setTab('computers')} role="tab" type="button">Компьютеры <span>{computers.length}</span></button>
        <button aria-selected={tab === 'screenshots'} onClick={() => setTab('screenshots')} role="tab" type="button">Скриншоты <span>{employees.filter((employee) => employee.screenshotEnabled).length}</span></button>
        <button aria-selected={tab === 'users'} onClick={() => setTab('users')} role="tab" type="button">Пользователи <span>{users.length}</span></button>
      </div>

      {tab === 'employees' && <section className="management-surface" role="tabpanel">
        <header><div><p className="eyebrow">Команда</p><h2>Сотрудники</h2><p>PIN подтверждает монтажёра на каждом компьютере до следующего 06:00.</p></div><button className="primary-button management-create" onClick={() => setEmployeeEditor(null)} type="button"><Icon name="userPlus" /> Добавить сотрудника</button></header>
        {loading && !employees.length ? <div className="management-loading">Загружаем сотрудников…</div> : employees.length ? <div className="responsive-table management-table"><table><thead><tr><th>Сотрудник</th><th>Логин</th><th>PIN монтажёра</th><th>Мониторинг</th><th>Компьютеры</th><th><span className="visually-hidden">Действия</span></th></tr></thead><tbody>{employees.map((employee) => {
          const employeeComputers = computers.filter((computer) => computer.employeeId === employee.id)
          return <tr key={employee.id}><td><strong>{employee.name}</strong><small>{employee.department ?? 'Отдел не указан'}</small>{!employee.isActive && <span className="management-status is-inactive">Неактивен</span>}</td><td><code>{employee.login}</code></td><td><strong className="operator-pin">{operatorPins[employee.id] ?? '••••'}</strong><small>Выдайте сотруднику лично</small></td><td><strong>{employee.screenshotEnabled ? `Снимки · ${employee.screenshotIntervalMinutes} мин` : 'Без снимков'}</strong><small>Простой после {employee.idleThresholdSeconds} сек.</small></td><td><strong>{employeeComputers.length || '—'}</strong><small>{employeeComputers.some((item) => item.agentStatus === 'Online') ? 'Есть Agent в сети' : 'Нет Agent в сети'}</small></td><td><div className="management-row-actions"><button disabled={!employee.isActive || regeneratingPin === employee.id} onClick={() => void handleRegeneratePin(employee)} type="button"><Icon name="refresh" size={17} /> {regeneratingPin === employee.id ? 'Создаём…' : 'Новый PIN'}</button><button disabled={!employee.isActive} onClick={() => setEnrollmentEmployee(employee)} type="button"><Icon name="key" size={17} /> Код Agent</button><button onClick={() => setEmployeeEditor(employee)} type="button">Изменить</button></div></td></tr>
        })}</tbody></table></div> : <div className="empty-state management-empty"><Icon name="users" size={30} /><h3>Сотрудников пока нет</h3><p>Добавьте первого сотрудника, затем выдайте ему одноразовый код Agent.</p></div>}
      </section>}

      {tab === 'computers' && <section className="management-surface" role="tabpanel">
        <header><div><p className="eyebrow">Рабочие места</p><h2>Компьютеры и Agent</h2><p>Состояние установленных приложений и момент последней связи.</p></div></header>
        {computers.length ? <div className="responsive-table management-table"><table><thead><tr><th>Компьютер</th><th>Сотрудник</th><th>Agent</th><th>Последняя связь</th><th><span className="visually-hidden">Действия</span></th></tr></thead><tbody>{computers.map((computer) => <tr key={computer.id}><td><strong>{computer.name}</strong><small>{computer.operatingSystem ?? 'ОС не определена'}</small></td><td><strong>{computer.employeeName}</strong></td><td><span className={`management-status status-${computer.agentStatus?.toLowerCase() ?? 'unknown'}`}>{computer.agentStatus ? agentStatusLabels[computer.agentStatus] : 'Не установлен'}</span><small>{computer.agentVersion ? `Версия ${computer.agentVersion}` : 'Версия неизвестна'}</small></td><td className="time-cell">{formatMoment(computer.agentLastSeenAtUtc ?? computer.lastHeartbeatAtUtc)}</td><td><div className="management-row-actions"><button className="danger-action" disabled={!computer.agentId || computer.agentStatus === 'Revoked' || revoking === computer.agentId} onClick={() => void handleRevoke(computer)} type="button">{revoking === computer.agentId ? 'Отключаем…' : 'Отключить Agent'}</button></div></td></tr>)}</tbody></table></div> : <div className="empty-state management-empty"><Icon name="monitor" size={30} /><h3>Компьютеры ещё не подключены</h3><p>Откройте вкладку «Сотрудники», выдайте код Agent и введите его на рабочем компьютере.</p></div>}
      </section>}

      {tab === 'screenshots' && screenshotSettings && <div role="tabpanel"><ScreenshotManagement employees={employees} key={`${screenshotSettings.captureMode}-${screenshotSettings.maxWidth}-${screenshotSettings.jpegQuality}-${screenshotSettings.retentionDays}-${privacyProcesses.length}`} onReload={load} privacyProcesses={privacyProcesses} settings={screenshotSettings} /></div>}

      {tab === 'users' && <section className="management-surface" role="tabpanel">
        <header><div><p className="eyebrow">Роли и область видимости</p><h2>Пользователи</h2><p>Определите, кто входит в диспетчерскую и каких сотрудников видит.</p></div><button className="primary-button management-create" onClick={() => setUserEditor(null)} type="button"><Icon name="userPlus" /> Добавить пользователя</button></header>
        {users.length ? <div className="responsive-table management-table"><table><thead><tr><th>Пользователь</th><th>Роль</th><th>Доступ</th><th>Последний вход</th><th><span className="visually-hidden">Действия</span></th></tr></thead><tbody>{users.map((user) => <tr key={user.id}><td><strong>{user.displayName}{user.id === currentUser.id ? ' · вы' : ''}</strong><small>{user.login}</small>{!user.isActive && <span className="management-status is-inactive">Отключён</span>}</td><td><span className="management-role">{roleLabels[user.role]}</span></td><td><strong>{user.role === 'Owner' || user.role === 'Admin' ? 'Все сотрудники' : `${user.employeeIds.length} назначено`}</strong><small>{user.role === 'Manager' || user.role === 'Viewer' ? user.employeeIds.map((id) => employees.find((employee) => employee.id === id)?.name).filter(Boolean).join(', ') || 'Нет доступа к сотрудникам' : 'Глобальная область'}</small></td><td className="time-cell">{formatMoment(user.lastLoginAtUtc)}</td><td><div className="management-row-actions"><button disabled={currentUser.role !== 'Owner' && user.role === 'Owner'} onClick={() => setPasswordUser(user)} type="button"><Icon name="key" size={17} /> Пароль</button><button disabled={currentUser.role !== 'Owner' && user.role === 'Owner'} onClick={() => setUserEditor(user)} type="button">Изменить</button></div></td></tr>)}</tbody></table></div> : <div className="empty-state management-empty"><Icon name="users" size={30} /><h3>Пользователей нет</h3><p>Добавьте администратора, руководителя или наблюдателя.</p></div>}
      </section>}

      {employeeEditor !== undefined && <EmployeeEditor employee={employeeEditor} onClose={() => setEmployeeEditor(undefined)} onSaved={load} />}
      {userEditor !== undefined && <UserEditor currentUser={currentUser} employeeOptions={employees} onClose={() => setUserEditor(undefined)} onSaved={load} user={userEditor} />}
      {passwordUser && <PasswordEditor onClose={() => setPasswordUser(null)} onSaved={() => setNotice(`Пароль пользователя «${passwordUser.displayName}» изменён.`)} user={passwordUser} />}
      {enrollmentEmployee && <EnrollmentDialog employee={enrollmentEmployee} onClose={() => setEnrollmentEmployee(null)} />}
    </>
  )
}
