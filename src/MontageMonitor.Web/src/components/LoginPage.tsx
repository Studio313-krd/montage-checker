import { FormEvent, useState } from 'react'
import { ApiError, login } from '../api'
import type { AuthSession } from '../types'
import { Icon } from './Icon'

interface LoginPageProps {
  onLogin: (session: AuthSession) => void
  onToggleTheme: () => void
  theme: 'light' | 'dark'
}

export function LoginPage({ onLogin, onToggleTheme, theme }: LoginPageProps) {
  const [loginValue, setLoginValue] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      onLogin(await login(loginValue.trim(), password))
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Не удалось связаться с сервером.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="login-page">
      <button
        aria-label={theme === 'light' ? 'Включить тёмную тему' : 'Включить светлую тему'}
        className="icon-button login-theme"
        onClick={onToggleTheme}
        type="button"
      >
        <Icon name={theme === 'light' ? 'moon' : 'sun'} />
      </button>

      <section className="login-intro" aria-labelledby="login-title">
        <a className="brand brand-on-dark" href="/" aria-label="MontageMonitor — главная">
          <span className="brand-mark" aria-hidden="true">MM</span>
          <span>MontageMonitor</span>
        </a>
        <div className="login-statement">
          <p className="eyebrow">Монтажная диспетчерская</p>
          <h1 id="login-title">Работа человека.<br />Работа машины.</h1>
          <p>Два независимых канала показывают реальную картину смены — от активного монтажа до длительного рендера.</p>
        </div>
        <div className="channel-preview" aria-hidden="true">
          <span><i /> Человек</span>
          <span><i /> Машина</span>
        </div>
      </section>

      <section className="login-panel" aria-label="Вход в систему">
        <form onSubmit={handleSubmit}>
          <div className="form-heading">
            <p className="eyebrow">Защищённый доступ</p>
            <h2>Войти в панель</h2>
            <p>Используйте учётную запись руководителя или администратора.</p>
          </div>

          <label htmlFor="login">Логин</label>
          <input
            autoComplete="username"
            autoFocus
            id="login"
            name="login"
            onChange={(event) => setLoginValue(event.target.value)}
            required
            type="text"
            value={loginValue}
          />

          <label htmlFor="password">Пароль</label>
          <input
            autoComplete="current-password"
            id="password"
            name="password"
            onChange={(event) => setPassword(event.target.value)}
            required
            type="password"
            value={password}
          />

          {error && <div className="form-error" role="alert">{error}</div>}

          <button className="primary-button" disabled={submitting} type="submit">
            {submitting ? 'Проверяем доступ…' : 'Войти'}
          </button>
        </form>
        <p className="login-note">Данные хранятся только на вашем VPS.</p>
      </section>
    </main>
  )
}
