import { useEffect, useState } from 'react'
import { refreshSession } from './api'
import { DashboardPage } from './components/DashboardPage'
import { AppShell, type AppRoute } from './components/AppShell'
import { Icon } from './components/Icon'
import { LoginPage } from './components/LoginPage'
import { ManagementPage } from './components/ManagementPage'
import { ApplicationsPage, RendersPage, SummaryPage } from './components/ReportPages'
import { ScreenshotGalleryPage } from './components/ScreenshotGalleryPage'
import { TimelinePage } from './components/TimelinePage'
import type { AuthSession } from './types'

type Theme = 'light' | 'dark'

function getInitialTheme(): Theme {
  const saved = window.localStorage.getItem('montage-theme')
  if (saved === 'light' || saved === 'dark') return saved
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function App() {
  const [session, setSession] = useState<AuthSession | null>(null)
  const [checkingSession, setCheckingSession] = useState(true)
  const [theme, setTheme] = useState<Theme>(getInitialTheme)
  const [path, setPath] = useState<AppRoute>(() => routeFromPath(window.location.pathname))

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    window.localStorage.setItem('montage-theme', theme)
    document.querySelector('meta[name="theme-color"]')?.setAttribute(
      'content',
      theme === 'dark' ? '#101820' : '#efede6',
    )
  }, [theme])

  useEffect(() => {
    let active = true
    refreshSession()
      .then((next) => {
        if (active) setSession(next)
      })
      .finally(() => {
        if (active) setCheckingSession(false)
      })
    return () => { active = false }
  }, [])

  useEffect(() => {
    const onPopState = () => setPath(routeFromPath(window.location.pathname))
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const toggleTheme = () => setTheme((current) => current === 'light' ? 'dark' : 'light')

  if (checkingSession) {
    return (
      <main className="boot-screen" aria-label="Проверяем авторизацию" aria-busy="true">
        <span className="brand-mark" aria-hidden="true">MM</span>
        <Icon name="activity" size={28} />
        <p>Подключаем диспетчерскую…</p>
      </main>
    )
  }

  if (!session) {
    return <LoginPage onLogin={setSession} onToggleTheme={toggleTheme} theme={theme} />
  }

  const navigate = (next: AppRoute) => {
    if (next !== path) window.history.pushState({}, '', next)
    setPath(next)
    window.scrollTo({ top: 0, behavior: 'instant' })
  }

  const canManage = session.user.role === 'Owner' || session.user.role === 'Admin'
  const effectivePath = (path === '/screenshots' && session.user.role === 'Viewer') || (path === '/management' && !canManage) ? '/' : path
  const page = effectivePath === '/timeline' ? <TimelinePage />
    : effectivePath === '/applications' ? <ApplicationsPage />
      : effectivePath === '/renders' ? <RendersPage />
        : effectivePath === '/screenshots' ? <ScreenshotGalleryPage />
          : effectivePath === '/reports' ? <SummaryPage canExport={session.user.role !== 'Viewer'} />
            : effectivePath === '/management' ? <ManagementPage currentUser={session.user} />
            : <DashboardPage />

  return (
    <AppShell
      currentPath={effectivePath}
      onLogout={() => setSession(null)}
      onNavigate={navigate}
      onToggleTheme={toggleTheme}
      theme={theme}
      user={session.user}
    >
      {page}
    </AppShell>
  )
}

function routeFromPath(pathname: string): AppRoute {
  const routes: AppRoute[] = ['/', '/timeline', '/applications', '/renders', '/screenshots', '/reports', '/management']
  return routes.includes(pathname as AppRoute) ? pathname as AppRoute : '/'
}

export default App
