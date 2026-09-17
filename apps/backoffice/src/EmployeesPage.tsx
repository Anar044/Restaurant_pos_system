import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeEmployee,
  type BackOfficeEmployees,
  type EmployeeRole,
  createEmployee,
  createRole,
  getBackOfficeEmployees,
  updateEmployee,
  updateRole,
} from './api';
import './employees.css';

type TabKey = 'employees' | 'roles';

type EditorState =
  | { kind: 'employee-create' }
  | { kind: 'employee-edit'; employee: BackOfficeEmployee }
  | { kind: 'role-create' }
  | { kind: 'role-edit'; role: EmployeeRole }
  | null;

const permissionMeta: Record<string, { label: string; description: string; group: string }> = {
  'menu.read': { label: 'Просмотр меню', description: 'Видеть меню и цены на кассе', group: 'Касса и заказы' },
  'orders.read': { label: 'Просмотр заказов', description: 'Видеть заказы ресторана', group: 'Касса и заказы' },
  'orders.write': { label: 'Работа с заказами', description: 'Создавать и изменять заказы', group: 'Касса и заказы' },
  'orders.void': { label: 'Отмена позиций', description: 'Отменять позиции и заказы', group: 'Касса и заказы' },
  'shifts.manage': { label: 'Управление сменой', description: 'Открывать и закрывать кассовые смены', group: 'Касса и заказы' },
  'payments.write': { label: 'Оплата', description: 'Принимать и проводить оплаты', group: 'Касса и заказы' },
  'backoffice.read': { label: 'Вход в BackOffice', description: 'Открывать административную панель', group: 'BackOffice' },
  'restaurant.manage': { label: 'Настройки ресторана', description: 'Изменять основные настройки ресторана', group: 'BackOffice' },
  'halls.manage': { label: 'Залы и столы', description: 'Управлять залами и столами', group: 'BackOffice' },
  'menu.manage': { label: 'Управление меню', description: 'Категории, блюда и цены', group: 'BackOffice' },
  'kitchen.manage': { label: 'Управление кухней', description: 'Кухонные станции и маршрутизация', group: 'BackOffice' },
  'employees.manage': { label: 'Сотрудники и роли', description: 'Создавать сотрудников, PIN и права', group: 'BackOffice' },
  'devices.manage': { label: 'Оборудование', description: 'Управлять устройствами и оборудованием', group: 'BackOffice' },
};

export function EmployeesPage({
  token,
  currentEmployeeId,
}: {
  token: string;
  currentEmployeeId: string;
}) {
  const [data, setData] = useState<BackOfficeEmployees | null>(null);
  const [tab, setTab] = useState<TabKey>('employees');
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeEmployees(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить сотрудников');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const visibleEmployees = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    const employees = data?.employees ?? [];
    if (!normalized) return employees;
    return employees.filter((employee) =>
      employee.name.toLowerCase().includes(normalized) ||
      (employee.roleName ?? '').toLowerCase().includes(normalized),
    );
  }, [data, query]);

  const stats = useMemo(() => {
    const employees = data?.employees ?? [];
    const roles = data?.roles ?? [];
    return {
      activeEmployees: employees.filter((x) => x.isActive).length,
      totalEmployees: employees.length,
      roles: roles.length,
      backOfficeUsers: employees.filter((employee) => {
        const role = roles.find((x) => x.id === employee.roleId);
        return employee.isActive && role?.permissions.includes('backoffice.read');
      }).length,
    };
  }, [data]);

  const currentEmployee = data?.employees.find((x) => x.id === currentEmployeeId) ?? null;

  if (!data && loading) {
    return <div className="empty-state">Загружаем сотрудников…</div>;
  }

  return (
    <section>
      <div className="page-heading employees-heading">
        <div>
          <div className="eyebrow">КОМАНДА И ДОСТУП</div>
          <h1>Сотрудники</h1>
          <p>Создавайте сотрудников, назначайте роли и управляйте правами доступа к POS и BackOffice.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button
            className="primary-button"
            onClick={() => setEditor(tab === 'employees' ? { kind: 'employee-create' } : { kind: 'role-create' })}
          >
            {tab === 'employees' ? '+ Сотрудник' : '+ Роль'}
          </button>
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="stats-grid">
        <EmployeeStat label="Активные сотрудники" value={stats.activeEmployees} detail={`${stats.totalEmployees} всего`} />
        <EmployeeStat label="Роли" value={stats.roles} detail="настроено для ресторана" />
        <EmployeeStat label="Доступ BackOffice" value={stats.backOfficeUsers} detail="активных сотрудников" />
      </div>

      <div className="employees-tabs">
        <button className={tab === 'employees' ? 'active' : ''} onClick={() => setTab('employees')}>
          Сотрудники
          <span>{stats.totalEmployees}</span>
        </button>
        <button className={tab === 'roles' ? 'active' : ''} onClick={() => setTab('roles')}>
          Роли и права
          <span>{stats.roles}</span>
        </button>
      </div>

      {tab === 'employees' ? (
        <div className="employees-panel">
          <div className="employees-toolbar">
            <div>
              <strong>Команда ресторана</strong>
              <small>PIN никогда не показывается после сохранения</small>
            </div>
            <input
              className="employees-search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Поиск по имени или роли"
            />
          </div>

          {visibleEmployees.length === 0 ? (
            <div className="employees-empty">
              <strong>Сотрудников не найдено</strong>
              <span>Создайте сотрудника или измените строку поиска.</span>
            </div>
          ) : (
            <div className="employees-table-wrap">
              <table className="employees-table">
                <thead>
                  <tr>
                    <th>Сотрудник</th>
                    <th>Роль</th>
                    <th>Статус</th>
                    <th>Создан</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {visibleEmployees.map((employee) => (
                    <tr key={employee.id} className={!employee.isActive ? 'row-inactive' : ''}>
                      <td>
                        <div className="employee-name-cell">
                          <div className="employee-avatar">{employee.name.slice(0, 1).toUpperCase()}</div>
                          <div>
                            <strong>{employee.name}</strong>
                            <small>{employee.id === currentEmployeeId ? 'Текущая учётная запись' : 'PIN настроен'}</small>
                          </div>
                        </div>
                      </td>
                      <td><span className="role-pill">{employee.roleName ?? 'Без роли'}</span></td>
                      <td>
                        <span className={`badge ${employee.isActive ? 'success' : 'neutral'}`}>
                          {employee.isActive ? 'Активен' : 'Отключён'}
                        </span>
                      </td>
                      <td>{new Date(employee.createdAt).toLocaleDateString('ru-RU')}</td>
                      <td className="employee-action-cell">
                        <button className="text-button" onClick={() => setEditor({ kind: 'employee-edit', employee })}>
                          Настроить
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : (
        <div className="roles-grid">
          {data?.roles.map((role) => {
            const isCurrentRole = currentEmployee?.roleId === role.id;
            return (
              <article className="role-card" key={role.id}>
                <div className="role-card-header">
                  <div>
                    <div className="title-row">
                      <h2>{role.name}</h2>
                      {isCurrentRole && <span className="badge current-role">Ваша роль</span>}
                    </div>
                    <p>{role.activeEmployeeCount} активных · {role.employeeCount} всего</p>
                  </div>
                  <button className="text-button" onClick={() => setEditor({ kind: 'role-edit', role })}>Настроить</button>
                </div>

                <div className="role-permission-summary">
                  {role.permissions.length === 0 ? (
                    <span className="no-permissions">Права не назначены</span>
                  ) : (
                    role.permissions.slice(0, 6).map((permission) => (
                      <span className="permission-chip" key={permission}>
                        {permissionMeta[permission]?.label ?? permission}
                      </span>
                    ))
                  )}
                  {role.permissions.length > 6 && <span className="permission-chip more">+{role.permissions.length - 6}</span>}
                </div>
              </article>
            );
          })}

          {(data?.roles.length ?? 0) === 0 && (
            <div className="empty-state roles-empty">
              <strong>Ролей пока нет</strong>
              <span>Создайте первую роль и выберите для неё права.</span>
              <button className="primary-button" onClick={() => setEditor({ kind: 'role-create' })}>Создать роль</button>
            </div>
          )}
        </div>
      )}

      {editor && data && (
        editor.kind.startsWith('employee') ? (
          <EmployeeEditor
            editor={editor as Extract<Exclude<EditorState, null>, { kind: 'employee-create' | 'employee-edit' }>}
            data={data}
            token={token}
            currentEmployeeId={currentEmployeeId}
            onClose={() => setEditor(null)}
            onSaved={async () => {
              setEditor(null);
              await refresh();
            }}
          />
        ) : (
          <RoleEditor
            editor={editor as Extract<Exclude<EditorState, null>, { kind: 'role-create' | 'role-edit' }>}
            data={data}
            token={token}
            currentRoleId={currentEmployee?.roleId ?? null}
            onClose={() => setEditor(null)}
            onSaved={async () => {
              setEditor(null);
              await refresh();
            }}
          />
        )
      )}
    </section>
  );
}

function EmployeeStat({ label, value, detail }: { label: string; value: number; detail: string }) {
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

function EmployeeEditor({
  editor,
  data,
  token,
  currentEmployeeId,
  onClose,
  onSaved,
}: {
  editor: { kind: 'employee-create' } | { kind: 'employee-edit'; employee: BackOfficeEmployee };
  data: BackOfficeEmployees;
  token: string;
  currentEmployeeId: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const employee = editor.kind === 'employee-edit' ? editor.employee : null;
  const [name, setName] = useState(employee?.name ?? '');
  const [roleId, setRoleId] = useState(employee?.roleId ?? data.roles[0]?.id ?? '');
  const [pin, setPin] = useState('');
  const [isActive, setIsActive] = useState(employee?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isCurrentEmployee = employee?.id === currentEmployeeId;
  const pinValid = editor.kind === 'employee-edit' && pin.length === 0
    ? true
    : pin.length >= 4 && pin.length <= 12;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim() || !roleId || !pinValid) return;

    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'employee-create') {
        await createEmployee(token, {
          name: name.trim(),
          roleId,
          pin,
        });
      } else {
        await updateEmployee(token, editor.employee.id, {
          name: name.trim(),
          roleId,
          isActive,
          newPin: pin || null,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить сотрудника');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card employee-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">СОТРУДНИК</div>
            <h2>{editor.kind === 'employee-create' ? 'Новый сотрудник' : 'Настройки сотрудника'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Имя сотрудника</span>
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={160} autoFocus />
        </label>

        <label>
          <span>Роль</span>
          <select value={roleId} onChange={(e) => setRoleId(e.target.value)} required>
            {data.roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}
          </select>
        </label>

        <label>
          <span>{editor.kind === 'employee-create' ? 'PIN' : 'Новый PIN'}</span>
          <input
            className="employee-pin-input"
            type="password"
            inputMode="numeric"
            maxLength={12}
            value={pin}
            onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))}
            placeholder={editor.kind === 'employee-create' ? '4–12 цифр' : 'Оставьте пустым, чтобы не менять'}
          />
          <small className="field-help">PIN хранится только в виде защищённого хэша и после сохранения не показывается.</small>
        </label>

        {editor.kind === 'employee-edit' && (
          <label className={`toggle-row ${isCurrentEmployee ? 'toggle-disabled' : ''}`}>
            <span>
              <strong>Сотрудник активен</strong>
              <small>{isCurrentEmployee ? 'Нельзя отключить текущую учётную запись' : 'Отключённый сотрудник не сможет войти по PIN'}</small>
            </span>
            <input
              type="checkbox"
              checked={isActive}
              disabled={isCurrentEmployee}
              onChange={(e) => setIsActive(e.target.checked)}
            />
          </label>
        )}

        {data.roles.length === 0 && (
          <div className="error-box">Сначала создайте хотя бы одну роль.</div>
        )}
        {pin.length > 0 && !pinValid && (
          <div className="employee-inline-warning">PIN должен содержать от 4 до 12 цифр.</div>
        )}
        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim() || !roleId || !pinValid || data.roles.length === 0}>
            {saving ? 'Сохраняем…' : editor.kind === 'employee-create' ? 'Создать' : 'Сохранить'}
          </button>
        </div>
      </form>
    </div>
  );
}

function RoleEditor({
  editor,
  data,
  token,
  currentRoleId,
  onClose,
  onSaved,
}: {
  editor: { kind: 'role-create' } | { kind: 'role-edit'; role: EmployeeRole };
  data: BackOfficeEmployees;
  token: string;
  currentRoleId: string | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const role = editor.kind === 'role-edit' ? editor.role : null;
  const [name, setName] = useState(role?.name ?? '');
  const [permissions, setPermissions] = useState<string[]>(role?.permissions ?? []);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const groupedPermissions = useMemo(() => {
    const groups = new Map<string, string[]>();
    for (const permission of data.availablePermissions) {
      const group = permissionMeta[permission]?.group ?? 'Другие';
      const current = groups.get(group) ?? [];
      current.push(permission);
      groups.set(group, current);
    }
    return Array.from(groups.entries());
  }, [data.availablePermissions]);

  const isCurrentRole = role?.id === currentRoleId;

  function togglePermission(permission: string) {
    setPermissions((current) =>
      current.includes(permission)
        ? current.filter((item) => item !== permission)
        : [...current, permission],
    );
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;

    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'role-create') {
        await createRole(token, { name: name.trim(), permissions });
      } else {
        await updateRole(token, editor.role.id, { name: name.trim(), permissions });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить роль');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card role-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">РОЛЬ И ПРАВА</div>
            <h2>{editor.kind === 'role-create' ? 'Новая роль' : 'Настройки роли'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Название роли</span>
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus />
        </label>

        {isCurrentRole && (
          <div className="role-security-note">
            Это ваша текущая роль. Система не позволит убрать у неё доступ к BackOffice и управлению сотрудниками, пока вы используете эту роль.
          </div>
        )}

        <div className="permissions-toolbar">
          <div>
            <strong>Права доступа</strong>
            <small>{permissions.length} из {data.availablePermissions.length} выбрано</small>
          </div>
          <div>
            <button type="button" className="text-button" onClick={() => setPermissions([...data.availablePermissions])}>Выбрать все</button>
            <button type="button" className="text-button" onClick={() => setPermissions([])}>Очистить</button>
          </div>
        </div>

        <div className="permissions-groups">
          {groupedPermissions.map(([group, items]) => (
            <div className="permission-group" key={group}>
              <div className="permission-group-title">{group}</div>
              <div className="permission-options">
                {items.map((permission) => {
                  const meta = permissionMeta[permission];
                  return (
                    <label className="permission-option" key={permission}>
                      <input
                        type="checkbox"
                        checked={permissions.includes(permission)}
                        onChange={() => togglePermission(permission)}
                      />
                      <span>
                        <strong>{meta?.label ?? permission}</strong>
                        <small>{meta?.description ?? permission}</small>
                      </span>
                    </label>
                  );
                })}
              </div>
            </div>
          ))}
        </div>

        <div className="role-apply-note">Изменённые права применятся к сотрудникам этой роли после следующего входа.</div>
        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>
            {saving ? 'Сохраняем…' : editor.kind === 'role-create' ? 'Создать' : 'Сохранить'}
          </button>
        </div>
      </form>
    </div>
  );
}
