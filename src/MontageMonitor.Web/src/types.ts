export type UserRole = 'Owner' | 'Admin' | 'Manager' | 'Viewer'
export type HumanState = 'Active' | 'Idle' | 'Locked' | 'Offline'
export type MachineState = 'Normal' | 'Render' | 'Proxy' | 'BackgroundProcessing'
export type ApplicationClassification = 'Productive' | 'Neutral' | 'Unproductive' | 'Ignored'
export type ProcessingType = 'Render' | 'Proxy' | 'Background'
export type ScreenshotCaptureMode = 'PrimaryMonitor' | 'AllMonitors'

export interface AuthUser {
  id: string
  login: string
  displayName: string
  role: UserRole
}

export interface AuthSession {
  accessToken: string
  tokenType: string
  expiresAtUtc: string
  user: AuthUser
}

export interface DashboardScreenshot {
  id: string
  timestampUtc: string
  width: number
  height: number
  screenIndex: number
  contentUrl: string
}

export interface DashboardEmployee {
  employeeId: string
  name: string
  department: string | null
  computerId: string | null
  computerName: string | null
  agentVersion: string | null
  isOnline: boolean
  currentApplication: string | null
  currentProcess: string | null
  currentWindowTitle: string | null
  humanState: HumanState
  humanStateStartedAtUtc: string | null
  machineState: MachineState
  machineStateStartedAtUtc: string | null
  primaryStateStartedAtUtc: string | null
  lastHeartbeatAtUtc: string | null
  latestScreenshot: DashboardScreenshot | null
}

export interface DashboardData {
  generatedAtUtc: string
  refreshAfterSeconds: number
  employees: DashboardEmployee[]
}

export interface Employee {
  id: string
  name: string
  login: string
  department: string | null
  isActive: boolean
  screenshotEnabled: boolean
  screenshotIntervalMinutes: number
  idleThresholdSeconds: number
}

export interface AdminUser {
  id: string
  login: string
  displayName: string
  role: UserRole
  isActive: boolean
  lastLoginAtUtc: string | null
  employeeIds: string[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ManagedComputer {
  id: string
  employeeId: string
  employeeName: string
  name: string
  operatingSystem: string | null
  agentVersion: string | null
  lastHeartbeatAtUtc: string | null
  lastOnlineAtUtc: string | null
  isRevoked: boolean
  agentId: string | null
  agentStatus: 'Online' | 'Offline' | 'Revoked' | null
  agentLastSeenAtUtc: string | null
}

export interface EnrollmentToken {
  id: string
  employeeId: string
  enrollmentToken: string
  expiresAtUtc: string
}

export interface ScreenshotSettings {
  captureMode: ScreenshotCaptureMode
  maxWidth: number
  jpegQuality: number
  retentionDays: number
}

export interface ScreenshotPrivacyProcess {
  id: string
  processName: string
  displayName: string
}

export interface EmployeeInput {
  name: string
  login: string
  department: string | null
  isActive?: boolean
  screenshotEnabled: boolean
  screenshotIntervalMinutes: number
  idleThresholdSeconds: number
}

export interface UserInput {
  login: string
  displayName: string
  password?: string
  role: UserRole
  isActive?: boolean
  employeeIds: string[]
}

export interface ApplicationSession {
  id: string
  computerId: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationSeconds: number
  processName: string
  executablePath: string | null
  windowTitle: string | null
  classification: ApplicationClassification
}

export interface StateSession<TState> {
  id: string
  computerId: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationSeconds: number
  state: TState
}

export interface MachineStateSession extends StateSession<MachineState> {
  detectionConfidence: number
  detectionReason: string | null
}

export interface TimelineData {
  employeeId: string
  fromUtc: string
  toUtc: string
  generatedAtUtc: string
  applications: ApplicationSession[]
  humanStates: StateSession<HumanState>[]
  machineStates: MachineStateSession[]
  renders: TimelineRenderSession[]
}

export interface TimelineRenderSession {
  id: string
  computerId: string
  type: ProcessingType
  program: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationSeconds: number
  outputFolder: string | null
  outputFile: string | null
  averageCpuPercent: number | null
  maxCpuPercent: number | null
  fileSizeBytes: number | null
  detectionConfidence: number
  detectionReason: string
}

export interface ReportQuery {
  employeeId?: string
  fromUtc: string
  toUtc: string
  timeZone?: string
}

export interface EmployeeSummary {
  employeeId: string
  employeeName: string
  firstEventAtUtc: string | null
  lastEventAtUtc: string | null
  totalSeconds: number
  productiveSeconds: number
  activeSeconds: number
  renderSeconds: number
  proxySeconds: number
  backgroundSeconds: number
  idleSeconds: number
  lockedSeconds: number
  offlineSeconds: number
}

export interface SummaryReport {
  fromUtc: string
  toUtc: string
  timeZone: string
  generatedAtUtc: string
  employees: EmployeeSummary[]
}

export interface ApplicationReportRow {
  employeeId: string
  employeeName: string
  date: string
  application: string
  processName: string
  classification: ApplicationClassification
  durationSeconds: number
}

export interface ApplicationReport {
  fromUtc: string
  toUtc: string
  timeZone: string
  generatedAtUtc: string
  applications: ApplicationReportRow[]
}

export interface RenderReportRow {
  id: string
  employeeId: string
  employeeName: string
  computerId: string
  computerName: string
  type: ProcessingType
  program: string
  startedAtUtc: string
  endedAtUtc: string | null
  durationSeconds: number
  outputFolder: string | null
  outputFile: string | null
  maxCpuPercent: number | null
  averageCpuPercent: number | null
  fileSizeBytes: number | null
  detectionConfidence: number
  detectionReason: string
}

export interface RenderReport {
  fromUtc: string
  toUtc: string
  generatedAtUtc: string
  renders: RenderReportRow[]
}

export interface ScreenshotGalleryItem {
  id: string
  employeeId: string
  employeeName: string
  computerId: string
  computerName: string
  timestampUtc: string
  width: number
  height: number
  screenIndex: number
  application: string | null
  windowTitle: string | null
  humanState: HumanState
  machineState: MachineState
  contentUrl: string
}

export interface ScreenshotGallery {
  fromUtc: string
  toUtc: string
  generatedAtUtc: string
  page: number
  pageSize: number
  totalCount: number
  screenshots: ScreenshotGalleryItem[]
}
