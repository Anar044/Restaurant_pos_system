import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeAdjustmentPreset,
  type BackOfficeAdjustments,
  type UpsertAdjustmentPresetInput,
  createAdjustmentPreset,
  getBackOfficeAdjustments,
  updateAdjustmentPreset,
} from './api';
import './adjustments.css';

type EditorState =
  | { kind: 'create' }
  | { kind: 'edit'; preset: BackOfficeAdjustmentPreset }
  | null;

export function AdjustmentsPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeAdjustments | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeAdjustments(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить скидки и надбавки');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const stats = useMemo(() => {
    const presets = data?.presets ?? [];
    return {
      active: presets.filter((x) => x.isActive).length,
      discounts: presets.filter((x) => x.type === 'DISCOUNT' && x.isActive).length,
      service: presets.filter((x) => x.type === 'SERVICE_CHARGE' && x.isActive).length,
    };
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем скидки и надбавки…</div>;
  }

  return (
    <section>
      <div className="page-heading adjustments-heading">
        <div>
          <div className="eyebrow">ЦЕНЫ И ПРАВА</div>
          <h1>Скидки и надбавки</h1>
          <p>
            Процент и сумма задаются только в BackOffice. Кассир на POS может
            только выбрать заранее созданное правило, разрешённое его роли.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>
            Обновить
          </button>
          {canManage && (
            <button className="primary-button" onClick={() => setEditor({ kind: 'create' })}>
              + Новое правило
            </button>
          )}
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="adjustment-stats">
        <Stat label="Активные правила" value={stats.active} />
        <Stat label="Скидки" value={stats.discounts} />
        <Stat label="Надбавки / сервис" value={stats.service} />
      </div>

      <div className="adjustment-security-note">
        <strong>Безопасная схема доступа</strong>
        <span>
          Для применения на POS у сотрудника должно быть право
          «Применять скидки и надбавки», а его роль должна быть отдельно
          разрешена в конкретном правиле.
        </span>
      </div>

      {(data?.presets.length ?? 0) === 0 ? (
        <div className="empty-state">
          <strong>Правил пока нет</strong>
          <span>Создайте первую скидку или сервисный сбор.</span>
        </div>
      ) : (
        <div className="adjustment-grid">
          {data!.presets.map((preset) => {
            const roles = data!.roles.filter((role) => preset.roleIds.includes(role.id));
            return (
              <article className={`adjustment-card ${!preset.isActive ? 'inactive' : ''}`} key={preset.id}>
                <div className="adjustment-card-head">
                  <div>
                    <span className={`adjustment-kind ${preset.type === 'DISCOUNT' ? 'discount' : 'service'}`}>
                      {preset.type === 'DISCOUNT' ? 'СКИДКА' : 'НАДБАВКА / СЕРВИС'}
                    </span>
                    <h2>{preset.name}</h2>
                  </div>
                  <span className={`badge ${preset.isActive ? 'success' : 'neutral'}`}>
                    {preset.isActive ? 'Активно' : 'Отключено'}
                  </span>
                </div>

                <div className="adjustment-value">
                  {preset.mode === 'PERCENT'
                    ? `${preset.value.toFixed(2)}%`
                    : `${preset.value.toFixed(2)} AZN`}
                </div>

                <div className="adjustment-meta">
                  <span>
                    Область: <strong>{scopeLabel(preset.scope)}</strong>
                  </span>
                  <span>
                    Роли:{' '}
                    <strong>
                      {roles.length > 0
                        ? roles.map((role) => role.name).join(', ')
                        : 'никому не разрешено'}
                    </strong>
                  </span>
                </div>

                {canManage && (
                  <button className="secondary-button" onClick={() => setEditor({ kind: 'edit', preset })}>
                    Настроить
                  </button>
                )}
              </article>
            );
          })}
        </div>
      )}

      {editor && data && canManage && (
        <AdjustmentEditor
          editor={editor}
          data={data}
          token={token}
          onClose={() => setEditor(null)}
          onSaved={async () => {
            setEditor(null);
            await refresh();
          }}
        />
      )}
    </section>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="stat-card">
      <span>{label}</span>
      <div><strong>{value}</strong></div>
    </div>
  );
}

function AdjustmentEditor({
  editor,
  data,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  data: BackOfficeAdjustments;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const existing = editor.kind === 'edit' ? editor.preset : null;
  const [name, setName] = useState(existing?.name ?? '');
  const [type, setType] = useState<'DISCOUNT' | 'SERVICE_CHARGE'>(existing?.type ?? 'DISCOUNT');
  const [mode, setMode] = useState<'PERCENT' | 'FIXED'>(existing?.mode ?? 'PERCENT');
  const [scope, setScope] = useState<'ORDER' | 'GUEST' | 'BOTH'>(existing?.scope ?? 'ORDER');
  const [value, setValue] = useState(existing?.value.toString() ?? '');
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);
  const [roleIds, setRoleIds] = useState<string[]>(existing?.roleIds ?? []);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function toggleRole(roleId: string) {
    setRoleIds((current) =>
      current.includes(roleId)
        ? current.filter((id) => id !== roleId)
        : [...current, roleId],
    );
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const numericValue = Number(value.replace(',', '.'));

    if (!name.trim()) {
      setError('Введите название правила.');
      return;
    }
    if (!Number.isFinite(numericValue) || numericValue <= 0) {
      setError('Значение должно быть больше нуля.');
      return;
    }
    if (mode === 'PERCENT' && numericValue > 100) {
      setError('Процент не может быть больше 100%.');
      return;
    }

    const input: UpsertAdjustmentPresetInput = {
      name: name.trim(),
      type,
      mode,
      scope: type === 'SERVICE_CHARGE' ? 'ORDER' : scope,
      value: numericValue,
      isActive,
      roleIds,
    };

    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'create') {
        await createAdjustmentPreset(token, input);
      } else {
        await updateAdjustmentPreset(token, editor.preset.id, input);
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить правило');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card adjustment-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">ПРАВИЛО ДЛЯ POS</div>
            <h2>{editor.kind === 'create' ? 'Новое правило' : 'Настройка правила'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Название на кассе</span>
          <input
            value={name}
            maxLength={120}
            onChange={(e) => setName(e.target.value)}
            placeholder="Например: Постоянный клиент 10%"
            autoFocus
          />
        </label>

        <div className="adjustment-form-grid">
          <label>
            <span>Тип</span>
            <select
              value={type}
              onChange={(e) => {
                const next = e.target.value as 'DISCOUNT' | 'SERVICE_CHARGE';
                setType(next);
                if (next === 'SERVICE_CHARGE') setScope('ORDER');
              }}
            >
              <option value="DISCOUNT">Скидка</option>
              <option value="SERVICE_CHARGE">Надбавка / сервис</option>
            </select>
          </label>

          <label>
            <span>Расчёт</span>
            <select value={mode} onChange={(e) => setMode(e.target.value as 'PERCENT' | 'FIXED')}>
              <option value="PERCENT">Процент</option>
              <option value="FIXED">Фиксированная сумма</option>
            </select>
          </label>

          <label>
            <span>{mode === 'PERCENT' ? 'Процент' : 'Сумма'}</span>
            <input
              inputMode="decimal"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              placeholder={mode === 'PERCENT' ? '10' : '5.00'}
            />
          </label>

          <label>
            <span>Область применения</span>
            <select
              value={type === 'SERVICE_CHARGE' ? 'ORDER' : scope}
              disabled={type === 'SERVICE_CHARGE'}
              onChange={(e) => setScope(e.target.value as 'ORDER' | 'GUEST' | 'BOTH')}
            >
              <option value="ORDER">Весь заказ</option>
              <option value="GUEST">Только выбранный гость</option>
              <option value="BOTH">Заказ или гость</option>
            </select>
          </label>
        </div>

        <div className="adjustment-role-section">
          <strong>Кому разрешено применять</strong>
          <small>
            Одного общего права недостаточно: POS покажет это правило только выбранным ролям.
          </small>
          <div className="adjustment-role-grid">
            {data.roles.map((role) => (
              <label className="adjustment-role-option" key={role.id}>
                <input
                  type="checkbox"
                  checked={roleIds.includes(role.id)}
                  onChange={() => toggleRole(role.id)}
                />
                <span>{role.name}</span>
              </label>
            ))}
          </div>
          {roleIds.length === 0 && (
            <div className="adjustment-warning">
              Ни одна роль не выбрана — правило не будет доступно ни на одном POS.
            </div>
          )}
        </div>

        <label className="toggle-row">
          <span>
            <strong>Правило активно</strong>
            <small>Отключённое правило сразу исчезнет из списка на POS.</small>
          </span>
          <input
            type="checkbox"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
          />
        </label>

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>
            {saving ? 'Сохраняем…' : 'Сохранить'}
          </button>
        </div>
      </form>
    </div>
  );
}

function scopeLabel(scope: 'ORDER' | 'GUEST' | 'BOTH') {
  if (scope === 'GUEST') return 'гость';
  if (scope === 'BOTH') return 'заказ или гость';
  return 'весь заказ';
}
