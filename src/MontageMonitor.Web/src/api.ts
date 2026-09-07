import type {
  ApplicationReport,
  AdminUser,
  AuthSession,
  DashboardData,
  Employee,
  EmployeeInput,
  EnrollmentToken,
  ManagedComputer,
  RenderReport,
  ReportQuery,
  ScreenshotPrivacyProcess,
  ScreenshotSettings,
  ScreenshotGallery,
  SummaryReport,
  TimelineData,
  UserInput,
} from './types'

let accessToken: string | null = null
let refreshPromise: Promise<AuthSession | null> | null = null

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message)
  }
}

async function readError(response: Response): Promise<string> {
  try {
    const problem = await response.json() as {
      message?: string
      title?: string
      detail?: string
      errors?: Record<string, string[]>
    }
    const validation = problem.errors
      ? Object.values(problem.errors).flat().filter(Boolean)[0]
      : undefined
    return validation ?? problem.message ?? problem.detail ?? problem.title ?? 'Сервер отклонил запрос.'
  } catch {
    return response.status >= 500
      ? 'Сервер временно недоступен. Повторите попытку.'
      : 'Не удалось выполнить запрос.'
  }
}

async function rawRefresh(): Promise<AuthSession | null> {
  const response = await fetch('/api/auth/refresh', {
    method: 'POST',
    credentials: 'include',
  })
  if (!response.ok) {
    accessToken = null
    return null
  }

  const session = await response.json() as AuthSession
  accessToken = session.accessToken
  return session
}

export function refreshSession(): Promise<AuthSession | null> {
  if (!refreshPromise) {
    refreshPromise = rawRefresh().finally(() => {
      refreshPromise = null
    })
  }

  return refreshPromise
}

async function authorizedFetch(
  path: string,
  init: RequestInit = {},
  retryAfterRefresh = true,
): Promise<Response> {
  const headers = new Headers(init.headers)
  if (accessToken) {
    headers.set('Authorization', `Bearer ${accessToken}`)
  }

  const response = await fetch(path, {
    ...init,
    headers,
    credentials: 'include',
  })
  if (response.status === 401 && retryAfterRefresh) {
    const session = await refreshSession()
    if (session) {
      return authorizedFetch(path, init, false)
    }
  }

  return response
}

export async function login(loginValue: string, password: string): Promise<AuthSession> {
  const response = await fetch('/api/auth/login', {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ login: loginValue, password }),
  })
  if (!response.ok) {
    throw new ApiError(await readError(response), response.status)
  }

  const session = await response.json() as AuthSession
  accessToken = session.accessToken
  return session
}

export async function logout(): Promise<void> {
  try {
    await authorizedFetch('/api/auth/logout', { method: 'POST' }, false)
  } finally {
    accessToken = null
  }
}

export async function getDashboard(signal?: AbortSignal): Promise<DashboardData> {
  const response = await authorizedFetch('/api/dashboard', { signal })
  if (!response.ok) {
    throw new ApiError(await readError(response), response.status)
  }

  return response.json() as Promise<DashboardData>
}

export async function getScreenshot(contentUrl: string, signal?: AbortSignal): Promise<Blob> {
  const response = await authorizedFetch(contentUrl, { signal })
  if (!response.ok) {
    throw new ApiError(await readError(response), response.status)
  }

  return response.blob()
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await authorizedFetch(path, { signal })
  if (!response.ok) throw new ApiError(await readError(response), response.status)
  return response.json() as Promise<T>
}

function queryString(query: ReportQuery, extra: Record<string, string | number | undefined> = {}): string {
  const search = new URLSearchParams({ fromUtc: query.fromUtc, toUtc: query.toUtc })
  if (query.employeeId) search.set('employeeId', query.employeeId)
  if (query.timeZone) search.set('timeZone', query.timeZone)
  Object.entries(extra).forEach(([key, value]) => {
    if (value !== undefined && value !== '') search.set(key, String(value))
  })
  return search.toString()
}

export function getEmployees(signal?: AbortSignal): Promise<Employee[]> {
  return getJson<Employee[]>('/api/employees', signal)
}

export function getManagedEmployees(signal?: AbortSignal): Promise<Employee[]> {
  return getJson<Employee[]>('/api/employees?includeInactive=true', signal)
}

async function sendJson<T>(path: string, method: 'POST' | 'PUT', body: unknown): Promise<T> {
  const response = await authorizedFetch(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw new ApiError(await readError(response), response.status)
  return response.json() as Promise<T>
}

async function sendWithoutResponse(path: string, method: 'POST' | 'DELETE' | 'PUT'): Promise<void> {
  const response = await authorizedFetch(path, { method })
  if (!response.ok) throw new ApiError(await readError(response), response.status)
}

async function sendJsonWithoutResponse(path: string, method: 'POST' | 'PUT', body: unknown): Promise<void> {
  const response = await authorizedFetch(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw new ApiError(await readError(response), response.status)
}

export function createEmployee(input: EmployeeInput): Promise<Employee> {
  return sendJson<Employee>('/api/employees', 'POST', input)
}

export function updateEmployee(id: string, input: EmployeeInput & { isActive: boolean }): Promise<Employee> {
  return sendJson<Employee>(`/api/employees/${id}`, 'PUT', input)
}

export function createEnrollmentToken(employeeId: string, expiresInHours = 24): Promise<EnrollmentToken> {
  return sendJson<EnrollmentToken>(`/api/admin/employees/${employeeId}/agent-enrollment`, 'POST', { expiresInHours })
}

export function getManagedComputers(signal?: AbortSignal): Promise<ManagedComputer[]> {
  return getJson<ManagedComputer[]>('/api/admin/computers', signal)
}

export function revokeAgent(agentId: string): Promise<void> {
  return sendWithoutResponse(`/api/admin/agents/${agentId}/revoke`, 'POST')
}

export function getUsers(signal?: AbortSignal): Promise<AdminUser[]> {
  return getJson<AdminUser[]>('/api/admin/users/', signal)
}

export function createUser(input: UserInput & { password: string }): Promise<AdminUser> {
  return sendJson<AdminUser>('/api/admin/users/', 'POST', input)
}

export function updateUser(id: string, input: UserInput & { isActive: boolean }): Promise<AdminUser> {
  return sendJson<AdminUser>(`/api/admin/users/${id}`, 'PUT', input)
}

export function changeUserPassword(id: string, newPassword: string): Promise<void> {
  return sendJsonWithoutResponse(`/api/admin/users/${id}/password`, 'PUT', { newPassword })
}

export function getScreenshotSettings(signal?: AbortSignal): Promise<ScreenshotSettings> {
  return getJson<ScreenshotSettings>('/api/admin/screenshots/settings', signal)
}

export function updateScreenshotSettings(settings: ScreenshotSettings): Promise<ScreenshotSettings> {
  return sendJson<ScreenshotSettings>('/api/admin/screenshots/settings', 'PUT', settings)
}

export function getScreenshotPrivacyProcesses(signal?: AbortSignal): Promise<ScreenshotPrivacyProcess[]> {
  return getJson<ScreenshotPrivacyProcess[]>('/api/admin/screenshots/privacy-processes', signal)
}

export function addScreenshotPrivacyProcess(processName: string, displayName: string): Promise<ScreenshotPrivacyProcess> {
  return sendJson<ScreenshotPrivacyProcess>('/api/admin/screenshots/privacy-processes', 'POST', { processName, displayName })
}

export function removeScreenshotPrivacyProcess(ruleId: string): Promise<void> {
  return sendWithoutResponse(`/api/admin/screenshots/privacy-processes/${ruleId}`, 'DELETE')
}

export function getTimeline(employeeId: string, query: ReportQuery, signal?: AbortSignal): Promise<TimelineData> {
  const search = queryString({ ...query, employeeId: undefined })
  return getJson<TimelineData>(`/api/employees/${employeeId}/timeline?${search}`, signal)
}

export function getSummaryReport(query: ReportQuery, signal?: AbortSignal): Promise<SummaryReport> {
  return getJson<SummaryReport>(`/api/reports/summary?${queryString(query)}`, signal)
}

export function getApplicationReport(query: ReportQuery, signal?: AbortSignal): Promise<ApplicationReport> {
  return getJson<ApplicationReport>(`/api/reports/applications?${queryString(query)}`, signal)
}

export function getRenderReport(query: ReportQuery, signal?: AbortSignal): Promise<RenderReport> {
  return getJson<RenderReport>(`/api/reports/renders?${queryString(query)}`, signal)
}

export async function downloadExcelReport(query: ReportQuery, employeeIds: string[]): Promise<Blob> {
  const search = new URLSearchParams({
    fromUtc: query.fromUtc,
    toUtc: query.toUtc,
  })
  if (query.timeZone) search.set('timeZone', query.timeZone)
  if (employeeIds.length) search.set('employeeIds', employeeIds.join(','))
  const response = await authorizedFetch(`/api/reports/export.xlsx?${search}`)
  if (!response.ok) throw new ApiError(await readError(response), response.status)
  return response.blob()
}

export function getScreenshotGallery(
  query: ReportQuery,
  page: number,
  application: string,
  signal?: AbortSignal,
): Promise<ScreenshotGallery> {
  const search = queryString(query, { page, pageSize: 24, application: application.trim() })
  return getJson<ScreenshotGallery>(`/api/screenshots?${search}`, signal)
}
