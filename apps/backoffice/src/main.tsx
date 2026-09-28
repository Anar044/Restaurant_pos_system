import { FormEvent, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  API_BASE_URL,
  DEVELOPMENT_RESTAURANT_ID,
  type AuthSession,
  type BackOfficeContext,
  getBackOfficeContext,
  loginWithPin,
} from './api';
import { AdjustmentsPage } from './AdjustmentsPage';
import { DevicesPage } from './DevicesPage';
import { EmployeesPage } from './EmployeesPage';
import { FinancePage } from './FinancePage';
import { InventoryPage } from './InventoryPage';
import { ModifiersPage } from './ModifiersPage';
import { NomenclaturePage } from './NomenclaturePage';
import { OverviewPage } from './OverviewPage';
import { GroupsPage } from './GroupsPage';
import './styles.css';

const SESSION_KEY = 'restaurant_backoffice_session';

type PageKey = 'overview' | 'groups' | 'modifiers' | 'nomenclature' | 'inventory' | 'adjustments' | 'employees' | 'roles' | 'devices' | 'finance';

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
  const [page, setPage] = useState<PageKey>('overview');
  const [context, setContext] = useState<BackOfficeContext | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [restaurantSettingsOpen, setRestaurantSettingsOpen] = useState(true);
  const [catalogOpen, setCatalogOpen] = useState(false);
  const [employeesOpen, setEmployeesOpen] = useState(false);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const nextContext = await getBackOfficeContext(session.token);
      setContext(nextContext);
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
    { key: 'overview', label: 'Обзор', icon: '⌂', ready: true },
    { key: 'finance', label: 'Кассы и смены', icon: '₼', ready: true },
    { key: 'groups', label: 'Группы, отделения и залы', icon: '▦', ready: true },
    { key: 'devices', label: 'Оборудование', icon: '◇', ready: true },
    { key: 'nomenclature', label: 'Номенклатура', icon: '▤', ready: true },
    { key: 'modifiers', label: 'Группы модификаторов', icon: '±', ready: true },
    { key: 'inventory', label: 'Склад', icon: '▥', ready: true },
    { key: 'adjustments', label: 'Скидки и надбавки', icon: '%', ready: true },
    { key: 'employees', label: 'Список сотрудников', icon: '◎', ready: true },
    { key: 'roles', label: 'Роли и права', icon: '◉', ready: true },
  ];

  const restaurantSettingsItems: PageKey[] = ['groups', 'devices'];
  const catalogItems: PageKey[] = ['nomenclature', 'modifiers', 'inventory'];
  const employeeItems: PageKey[] = ['employees', 'roles'];

  function renderNavItem(key: PageKey, nested = false) {
    const item = navItems.find((x) => x.key === key)!;
    return (
      <button
        key={item.key}
        className={`nav-item ${nested ? 'nested' : ''} ${page === item.key ? 'active' : ''}`}
        onClick={() => setPage(item.key)}
      >
        <span className="nav-icon">{item.icon}</span>
        <span>{item.label}</span>
        {!item.ready && <small>скоро</small>}
      </button>
    );
  }

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
          <div className="nav-section">
            {renderNavItem('overview')}
            {renderNavItem('finance')}
          </div>

          <div className="nav-section">
            <button
              className={`nav-parent ${restaurantSettingsItems.includes(page) ? 'active-parent' : ''}`}
              onClick={() => setRestaurantSettingsOpen((value) => !value)}
              aria-expanded={restaurantSettingsOpen}
            >
              <span className="nav-parent-icon">⚙</span>
              <span>Настройки ресторана</span>
              <span className={`nav-chevron ${restaurantSettingsOpen ? 'open' : ''}`}>›</span>
            </button>
            {restaurantSettingsOpen && (
              <div className="nav-submenu">
                {restaurantSettingsItems.map((key) => renderNavItem(key, true))}
              </div>
            )}
          </div>

          <div className="nav-section">
            <button
              className={`nav-parent ${catalogItems.includes(page) ? 'active-parent' : ''}`}
              onClick={() => setCatalogOpen((value) => !value)}
              aria-expanded={catalogOpen}
            >
              <span className="nav-parent-icon">▤</span>
              <span>Номенклатура и склад</span>
              <span className={`nav-chevron ${catalogOpen ? 'open' : ''}`}>›</span>
            </button>
            {catalogOpen && (
              <div className="nav-submenu">
                {catalogItems.map((key) => renderNavItem(key, true))}
              </div>
            )}
          </div>

          <div className="nav-section">
            {renderNavItem('adjustments')}
          </div>

          <div className="nav-section">
            <button
              className={`nav-parent ${employeeItems.includes(page) ? 'active-parent' : ''}`}
              onClick={() => setEmployeesOpen((value) => !value)}
              aria-expanded={employeesOpen}
            >
              <span className="nav-parent-icon">◎</span>
              <span>Сотрудники</span>
              <span className={`nav-chevron ${employeesOpen ? 'open' : ''}`}>›</span>
            </button>
            {employeesOpen && (
              <div className="nav-submenu">
                {employeeItems.map((key) => renderNavItem(key, true))}
              </div>
            )}
          </div>
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

          {page === 'overview' ? (
            <OverviewPage
              token={session.token}
              restaurantName={context?.restaurant.name ?? 'Ресторан'}
              onOpenFinance={() => setPage('finance')}
            />
          ) : page === 'modifiers' ? (
            <ModifiersPage token={session.token} />
          ) : page === 'nomenclature' ? (
            <NomenclaturePage
              token={session.token}
              canManage={(session.permissions ?? []).includes('inventory.manage')}
            />
          ) : page === 'inventory' ? (
            <InventoryPage
              token={session.token}
              canManage={(session.permissions ?? []).includes('inventory.manage')}
            />
          ) : page === 'adjustments' ? (
            <AdjustmentsPage
              token={session.token}
              canManage={(session.permissions ?? []).includes('pricing.manage')}
            />
          ) : page === 'groups' ? (
            <GroupsPage token={session.token} />
          ) : page === 'employees' ? (
            <EmployeesPage
              token={session.token}
              currentEmployeeId={session.employeeId}
              initialTab="employees"
              showTabs={false}
            />
          ) : page === 'roles' ? (
            <EmployeesPage
              token={session.token}
              currentEmployeeId={session.employeeId}
              initialTab="roles"
              showTabs={false}
            />
          ) : page === 'devices' ? (
            <DevicesPage token={session.token} />
          ) : page === 'finance' ? (
            <FinancePage
              token={session.token}
              canManageShifts={(session.permissions ?? []).includes('shifts.manage')}
            />
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
