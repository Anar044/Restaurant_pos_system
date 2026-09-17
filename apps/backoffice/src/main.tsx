import { FormEvent, useEffect, useMemo, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  API_BASE_URL,
  DEVELOPMENT_RESTAURANT_ID,
  type AuthSession,
  type BackOfficeContext,
  type DiningTable,
  type Hall,
  createHall,
  createTable,
  getBackOfficeContext,
  getHalls,
  loginWithPin,
  updateHall,
  updateTable,
} from './api';
import './styles.css';

const SESSION_KEY = 'restaurant_backoffice_session';

type PageKey = 'overview' | 'halls' | 'menu' | 'kitchen' | 'employees' | 'devices';

type EditorState =
  | { kind: 'hall-create' }
  | { kind: 'hall-edit'; hall: Hall }
  | { kind: 'table-create'; hall: Hall }
  | { kind: 'table-edit'; hall: Hall; table: DiningTable }
  | null;

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
            placeholder="••••"
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
  const [editor, setEditor] = useState<EditorState>(null);

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
    { key: 'menu', label: 'Меню', icon: '≡' },
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
              onRefresh={() => void refresh()}
              onEdit={setEditor}
            />
          ) : (
            <ComingSoon page={navItems.find((x) => x.key === page)?.label ?? 'Раздел'} />
          )}
        </div>
      </main>

      {editor && (
        <EditorModal
          editor={editor}
          halls={halls}
          token={session.token}
          onClose={() => setEditor(null)}
          onSaved={async () => {
            setEditor(null);
            await refresh();
          }}
        />
      )}
    </div>
  );
}

function HallsPage({
  halls,
  loading,
  onRefresh,
  onEdit,
}: {
  halls: Hall[];
  loading: boolean;
  onRefresh: () => void;
  onEdit: (state: EditorState) => void;
}) {
  const stats = useMemo(() => {
    const tables = halls.flatMap((hall) => hall.tables);
    return {
      halls: halls.filter((x) => x.isActive).length,
      tables: tables.length,
      activeTables: tables.filter((x) => x.isActive).length,
      seats: tables.filter((x) => x.isActive).reduce((sum, table) => sum + table.seats, 0),
    };
  }, [halls]);

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">СТРУКТУРА РЕСТОРАНА</div>
          <h1>Залы и столы</h1>
          <p>Настройте залы, столы, количество мест и порядок отображения на кассе.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={onRefresh} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => onEdit({ kind: 'hall-create' })}>+ Добавить зал</button>
        </div>
      </div>

      <div className="stats-grid">
        <StatCard label="Активные залы" value={stats.halls} detail={`${halls.length} всего`} />
        <StatCard label="Столы" value={stats.activeTables} detail={`${stats.tables} всего`} />
        <StatCard label="Посадочных мест" value={stats.seats} detail="в активных столах" />
      </div>

      {loading && halls.length === 0 ? (
        <div className="empty-state">Загружаем структуру ресторана…</div>
      ) : halls.length === 0 ? (
        <div className="empty-state">
          <strong>Залов пока нет</strong>
          <span>Создайте первый зал, затем добавьте в него столы.</span>
          <button className="primary-button" onClick={() => onEdit({ kind: 'hall-create' })}>Создать зал</button>
        </div>
      ) : (
        <div className="hall-list">
          {halls.map((hall) => (
            <article className={`hall-card ${!hall.isActive ? 'inactive-card' : ''}`} key={hall.id}>
              <div className="hall-header">
                <div>
                  <div className="title-row">
                    <h2>{hall.name}</h2>
                    <span className={`badge ${hall.isActive ? 'success' : 'neutral'}`}>
                      {hall.isActive ? 'Активен' : 'Отключён'}
                    </span>
                  </div>
                  <p>{hall.tables.length} столов · порядок {hall.sortOrder}</p>
                </div>
                <div className="hall-actions">
                  <button className="text-button" onClick={() => onEdit({ kind: 'hall-edit', hall })}>Настроить</button>
                  <button className="primary-button compact" onClick={() => onEdit({ kind: 'table-create', hall })}>+ Стол</button>
                </div>
              </div>

              <div className="table-grid">
                {hall.tables.map((table) => (
                  <button
                    className={`table-card ${!table.isActive ? 'inactive' : ''}`}
                    key={table.id}
                    onClick={() => onEdit({ kind: 'table-edit', hall, table })}
                  >
                    <div className="table-card-top">
                      <span className="table-symbol">▢</span>
                      <span className={`mini-dot ${table.isActive ? '' : 'off'}`} />
                    </div>
                    <strong>{table.name}</strong>
                    <span>{table.seats} мест</span>
                    <small>Порядок: {table.sortOrder}</small>
                  </button>
                ))}
                {hall.tables.length === 0 && (
                  <button className="table-card add-table" onClick={() => onEdit({ kind: 'table-create', hall })}>
                    <span className="plus-circle">+</span>
                    <strong>Добавить стол</strong>
                  </button>
                )}
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

function StatCard({ label, value, detail }: { label: string; value: number; detail: string }) {
  return (
    <div className="stat-card">
      <span>{label}</span>
      <div>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
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

function EditorModal({
  editor,
  halls,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  halls: Hall[];
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const isHall = editor.kind.startsWith('hall');
  const editing = editor.kind.endsWith('edit');
  const currentHall = editor.kind === 'hall-edit' ? editor.hall : editor.kind.startsWith('table') ? editor.hall : null;
  const currentTable = editor.kind === 'table-edit' ? editor.table : null;

  const [name, setName] = useState(currentTable?.name ?? currentHall?.name ?? '');
  const [sortOrder, setSortOrder] = useState(currentTable?.sortOrder ?? currentHall?.sortOrder ?? 0);
  const [seats, setSeats] = useState(currentTable?.seats ?? 4);
  const [hallId, setHallId] = useState(currentTable?.hallId ?? currentHall?.id ?? halls[0]?.id ?? '');
  const [isActive, setIsActive] = useState(currentTable?.isActive ?? currentHall?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title =
    editor.kind === 'hall-create' ? 'Новый зал' :
    editor.kind === 'hall-edit' ? 'Настройки зала' :
    editor.kind === 'table-create' ? `Новый стол · ${editor.hall.name}` :
    'Настройки стола';

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'hall-create') {
        await createHall(token, { name: name.trim(), sortOrder });
      } else if (editor.kind === 'hall-edit') {
        await updateHall(token, editor.hall.id, { name: name.trim(), sortOrder, isActive });
      } else if (editor.kind === 'table-create') {
        await createTable(token, editor.hall.id, { name: name.trim(), seats, sortOrder });
      } else {
        await updateTable(token, editor.table.id, {
          hallId,
          name: name.trim(),
          seats,
          sortOrder,
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить изменения');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">{isHall ? 'ЗАЛ' : 'СТОЛ'}</div>
            <h2>{title}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="form-grid">
          <label className="full-field">
            <span>Название</span>
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus />
          </label>

          {!isHall && editing && (
            <label className="full-field">
              <span>Зал</span>
              <select value={hallId} onChange={(e) => setHallId(e.target.value)}>
                {halls.map((hall) => <option value={hall.id} key={hall.id}>{hall.name}</option>)}
              </select>
            </label>
          )}

          {!isHall && (
            <label>
              <span>Количество мест</span>
              <input type="number" min={1} max={100} value={seats} onChange={(e) => setSeats(Number(e.target.value))} />
            </label>
          )}

          <label>
            <span>Порядок</span>
            <input type="number" min={0} value={sortOrder} onChange={(e) => setSortOrder(Number(e.target.value))} />
          </label>
        </div>

        {editing && (
          <label className="toggle-row">
            <span>
              <strong>Активен</strong>
              <small>{isHall ? 'Показывать зал на кассе' : 'Стол доступен для работы'}</small>
            </span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>
            {saving ? 'Сохраняем…' : editing ? 'Сохранить' : 'Создать'}
          </button>
        </div>
      </form>
    </div>
  );
}

createRoot(document.getElementById('root')!).render(<App />);
