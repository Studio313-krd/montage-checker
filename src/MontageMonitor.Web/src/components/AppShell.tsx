import { useState, type MouseEvent, type ReactNode } from 'react'
import { logout } from '../api'
import type { AuthUser, UserRole } from '../types'
import brandIconUrl from '../../../MontageMonitor.Agent/Assets/AppIcon.Source.png'
import { Icon, type IconName } from './Icon'

export type AppRoute = '/' | '/timeline' | '/applications' | '/renders' | '/screenshots' | '/reports' | '/management'

interface AppShellProps {
  children: ReactNode
  currentPath: AppRoute
  onLogout: () => void
  onNavigate: (path: AppRoute) => void
  onToggleTheme: () => void
  theme: 'light' | 'dark'
  user: AuthUser
}

const roleLabels: Record<UserRole, string> = {
  Owner: 'Владелец',
  Admin: 'Администратор',
  Manager: 'Руководитель',
  Viewer: 'Наблюдатель',
}

const navigation: Array<{ icon: IconName; label: string; path: AppRoute; management?: boolean }> = [
  { icon: 'activity', label: 'Обзор', path: '/' },
  { icon: 'clock', label: 'Хронология', path: '/timeline' },
  { icon: 'file', label: 'Приложения', path: '/applications' },
  { icon: 'monitor', label: 'Рендеры', path: '/renders' },
  { icon: 'camera', label: 'Скриншоты', path: '/screenshots', management: true },
  { icon: 'users', label: 'Отчёты', path: '/reports' },
]

function getInitials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2)
    .map((part) => part[0]?.toUpperCase()).join('')
}

export function AppShell({
  children,
  currentPath,
  onLogout,
  onNavigate,
  onToggleTheme,
  theme,
  user,
}: AppShellProps) {
  const [loggingOut, setLoggingOut] = useState(false)
  const canViewScreenshots = user.role !== 'Viewer'
  const canManage = user.role === 'Owner' || user.role === 'Admin'

  function handleNavigation(event: MouseEvent<HTMLAnchorElement>, path: AppRoute) {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
    event.preventDefault()
    onNavigate(path)
  }

  async function handleLogout() {
    setLoggingOut(true)
    await logout()
    onLogout()
  }

  return (
    <div className="dashboard-shell">
      <a className="skip-link" href="#main-content">К основному содержанию</a>
      <aside className="sidebar">
        <a
          className="brand brand-on-dark"
          href="/"
          aria-label="MontageMonitor — обзор"
          onClick={(event) => handleNavigation(event, '/')}
        >
          <img alt="" aria-hidden="true" className="brand-icon" src={brandIconUrl} />
          <span>MontageMonitor</span>
        </a>
        <nav aria-label="Основная навигация">
          <p>Производство</p>
          {navigation.filter((item) => !item.management || canViewScreenshots).map((item) => (
            <a
              aria-current={currentPath === item.path ? 'page' : undefined}
              className={`nav-item${currentPath === item.path ? ' is-active' : ''}`}
              href={item.path}
              key={item.path}
              onClick={(event) => handleNavigation(event, item.path)}
            >
              <Icon name={item.icon} /> {item.label}
            </a>
          ))}
          {canManage && (
            <a
              aria-current={currentPath === '/management' ? 'page' : undefined}
              className={`nav-item${currentPath === '/management' ? ' is-active' : ''}`}
              href="/management"
              onClick={(event) => handleNavigation(event, '/management')}
            >
              <Icon name="settings" /> Управление
            </a>
          )}
        </nav>
        <div className="sidebar-user">
          <span className="user-avatar" aria-hidden="true">{getInitials(user.displayName)}</span>
          <div><strong>{user.displayName}</strong><span>{roleLabels[user.role]}</span></div>
          <div className="sidebar-actions">
            <button
              aria-label={theme === 'light' ? 'Включить тёмную тему' : 'Включить светлую тему'}
              className="icon-button icon-button-on-dark"
              onClick={onToggleTheme}
              type="button"
            >
              <Icon name={theme === 'light' ? 'moon' : 'sun'} />
            </button>
            <button
              aria-label="Выйти из системы"
              className="icon-button icon-button-on-dark"
              disabled={loggingOut}
              onClick={() => void handleLogout()}
              type="button"
            >
              <Icon name="logout" />
            </button>
          </div>
        </div>
      </aside>
      <main id="main-content" className="dashboard-main">{children}</main>
    </div>
  )
}
