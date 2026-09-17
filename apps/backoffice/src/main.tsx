import { FormEvent, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  API_BASE_URL,
  DEVELOPMENT_RESTAURANT_ID,
  type AuthSession,
  type BackOfficeContext,
  type Hall,
  getBackOfficeContext,
  getHalls,
  loginWithPin,
} from './api';
import { HallsPage } from './HallsPage';
import { MenuPage } from './MenuPage';
import './styles.css';

const SESSION_KEY = 'restaurant_backoffice_session';

type PageKey = 'overview' | 'halls' | 'menu' | 'kitchen' | 'employees' | 'devices';

function loadStoredSession(): AuthSession | null {
  try {
    const raw = localStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    const session = JSON.parse(raw) as AuthSession;
    if (!session.token || new Date(session.expiresAt).getTime() <= Date.now()) {
      localStorage.removeItem(SESSION_KEY);
      return null;
    }
    return session;
  } catch {
    localStorage.removeItem(SESSION_KEY);
    return null;
  }
}

function App() {
  const [session, setSession] = useState<AuthSession | null>(() => loadStoredSession());

  function onLoggedIn(value: AuthSession) {
    localStorage.setItem(SESSION_KEY, JSON.stringify(value));
    setSession(value);
  }

  function logout() {
    localStorage.removeItem(SESSION_KEY);
    setSession(null);
  }

  return session ? (
    <BackOffice session={session} onLogout={logout} />
  ) : (
    <LoginScreen onLoggedIn={onLoggedIn} />
  );
}

function LoginScreen({ onLoggedIn }: { onLoggedIn: (session: AuthSession) => void }) {
  const [restaurantId, setRestaurantId] = useState(DEVELOPMENT_RESTAURANT_ID);
  const [pin, setPin] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (loading || pin.length < 4) return;
    setLoading(true);
    setError(null);
    try {
      onLoggedIn(await loginWithPin(restaurantId.trim(), pin));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось войти');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-page">
      <div className="login-brand">
        <div className="brand-mark">R</div>
        <div>
          <strong>Restaurant Platform</strong>
          <span>BackOffice</span>
        </div>
      </div>
      <form className="login-card" onSubmit={submit}>
        <div className="eyebrow">Управление рестораном</div>
        <h1>Вход в BackOffice</h1>
        <p className="muted">Используйте PIN сотрудника с правами администратора.</p>

        <label>
          <span>Restaurant ID</span>
          <input
            value={restaurantId}
            onChange={(e) => setRestaurantId(e.target.value)}
            autoComplete="off"
          />
        </label>
        <label>
          <span>PIN</span>
          <input
            className="pin-input"
            type="password"
            inputMode="numeric"
            maxLength={12}
            value={pin}
            onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))}
            placeholder="Введите PIN"
            autoFocus
          />
        </label>

        {error && <div className="error-box">{error}</div>}
        <button className="primary-button login-button" disabled={loading || pin.length < 4}>
          {loading ? 'Входим…' : 'Войти'}
        </button>
        <div className="login-footnote">API: {API_BASE_URL}</div>
      </form>
    </div>
  );
}

function BackOffice({ session, onLogout }: { session: AuthSession; onLogout: () => void }) {
  const [page, setPage] = useState<PageKey>('halls');
  const [context, setContext] = useState<BackOfficeContext | null>(null);
  const [halls, setHalls] = useState<Hall[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const [nextContext, nextHalls] = await Promise.all([
        getBackOfficeContext(session.token),
        getHalls(session.token),
      ]);
      setContext(nextContext);
      setHalls(nextHalls);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить данные');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [session.token]);

  const navItems: Array<{ key: PageKey; label: string; icon: string; ready?: boolean }> = [
    { key: 'overview', label: 'Обзор', icon: '⌂' },
    { key: 'halls', label: 'Залы и столы', icon: '▦', ready: true },
    { key: 'menu', label: 'Меню', icon: '≡', ready: true },
    { key: 'kitchen', label: 'Кухня', icon: '◫' },
    { key: 'employees', label: 'Сотрудники', icon: '◎' },
    { key: 'devices', label: 'Оборудование', icon: '◇' },
  ];

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <div className="brand-mark small">R</div>
          <div>
            <strong>Restaurant</strong>
            <span>BackOffice</span>
          </div>
        </div>

        <div className="restaurant-switcher">
          <span className="switcher-label">РЕСТОРАН</span>
          <strong>{context?.restaurant.name ?? 'Загрузка…'}</strong>
          <small>{context?.organization.name ?? 'Организация'}</small>
        </div>

        <nav>
          {navItems.map((item) => (
            <button
              key={item.key}
              className={`nav-item ${page === item.key ? 'active' : ''}`}
              onClick={() => setPage(item.key)}
            >
              <span className="nav-icon">{item.icon}</span>
              <span>{item.label}</span>
              {!item.ready && <small>скоро</small>}
            </button>
          ))}
        </nav>

        <div className="sidebar-footer">
          <div className="user-avatar">{session.employeeName.slice(0, 1).toUpperCase()}</div>
          <div className="user-meta">
            <strong>{session.employeeName}</strong>
            <span>{session.roleName}</span>
          </div>
          <button className="icon-button" title="Выйти" onClick={onLogout}>↗</button>
        </div>
      </aside>

      <main className="main-area">
        <header className="topbar">
          <div>
            <div className="breadcrumb">{context?.organization.name ?? 'Restaurant Platform'}</div>
            <strong>{context?.restaurant.name ?? 'BackOffice'}</strong>
          </div>
          <div className="topbar-status">
            <span className="status-dot" />
            Restaurant Node online
          </div>
        </header>

        <div className="content-area">
          {error && (
            <div className="global-error">
              <span>{error}</span>
              <button onClick={() => void refresh()}>Повторить</button>
            </div>
          )}

          {page === 'halls' ? (
            <HallsPage
              halls={halls}
              loading={loading}
              token={session.token}
              onRefresh={refresh}
            />
          ) : page === 'menu' ? (
            <MenuPage token={session.token} />
          ) : (
            <ComingSoon page={navItems.find((x) => x.key === page)?.label ?? 'Раздел'} />
          )}
        </div>
      </main>
    </div>
  );
}

function ComingSoon({ page }: { page: string }) {
  return (
    <div className="coming-soon">
      <div className="coming-icon">◌</div>
      <h1>{page}</h1>
      <p>Этот модуль идёт следующим по плану BackOffice MVP.</p>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
