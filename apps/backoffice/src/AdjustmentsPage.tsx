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

const DAYS = [
  ['Пн', 1],
  ['Вт', 2],
  ['Ср', 4],
  ['Чт', 8],
  ['Пт', 16],
  ['Сб', 32],
  ['Вс', 64],
] as const;

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
      setError(e instanceof Error ? e.message : 'Не удалось загрузить правила');
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
      automatic: presets.filter((x) => x.isActive && x.applicationMode === 'AUTOMATIC').length,
      manual: presets.filter((x) => x.isActive && x.applicationMode === 'MANUAL').length,
    };
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем правила ценообразования…</div>;
  }

  return (
    <section>
      <div className="page-heading adjustments-heading">
        <div>
          <div className="eyebrow">PRICING ENGINE</div>
          <h1>Скидки и надбавки</h1>
          <p>
            Здесь задаются сумма, блюда, категории, расписание и порядок расчёта.
            POS не может изменять эти условия.
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

      {error && <div className="global-error">{error}</div>}

      <div className="adjustment-stats">
        <Stat label="Активные" value={stats.active} />
        <Stat label="Автоматические" value={stats.automatic} />
        <Stat label="Ручные на POS" value={stats.manual} />
      </div>

      <div className="adjustment-security-note">
        <strong>Расчёт выполняет сервер</strong>
        <span>
          Условия правила сохраняются snapshot-ом в заказе. Изменение правила
          позже не меняет уже применённый расчёт.
        </span>
      </div>

      {(data?.presets.length ?? 0) === 0 ? (
        <div className="empty-state">
          <strong>Правил пока нет</strong>
          <span>Создайте первую скидку, акцию или сервисный сбор.</span>
        </div>
      ) : (
        <div className="adjustment-grid">
          {data!.presets.map((preset) => {
            const target = targetLabel(preset, data!);
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
                  <span>Применение: <strong>{preset.applicationMode === 'AUTOMATIC' ? 'автоматически' : 'вручную на POS'}</strong></span>
                  <span>Цель: <strong>{target}</strong></span>
                  <span>Расписание: <strong>{scheduleLabel(preset)}</strong></span>
                  <span>Время по: <strong>{preset.timeBasis === 'ITEM_ADDED_AT' ? 'добавлению блюда' : 'открытию заказа'}</strong></span>
                  <span>Приоритет: <strong>{preset.priority}</strong></span>
                  <span>Совмещение: <strong>{preset.canStack ? 'разрешено' : 'не совмещать'}</strong></span>
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
  const [applicationMode, setApplicationMode] = useState<'MANUAL' | 'AUTOMATIC'>(existing?.applicationMode ?? 'MANUAL');
  const [timeBasis, setTimeBasis] = useState<'ORDER_OPENED_AT' | 'ITEM_ADDED_AT'>(existing?.timeBasis ?? 'ITEM_ADDED_AT');
  const [value, setValue] = useState(existing?.value.toString() ?? '');
  const [priority, setPriority] = useState(existing?.priority.toString() ?? '100');
  const [canStack, setCanStack] = useState(existing?.canStack ?? true);
  const [weekdayMask, setWeekdayMask] = useState(existing?.weekdayMask ?? 127);
  const [allDay, setAllDay] = useState(existing ? existing.startMinute == null : true);
  const [startTime, setStartTime] = useState(minuteToTime(existing?.startMinute) ?? '12:00');
  const [endTime, setEndTime] = useState(minuteToTime(existing?.endMinute) ?? '16:00');
  const [productIds, setProductIds] = useState<string[]>(existing?.productIds ?? []);
  const [categoryIds, setCategoryIds] = useState<string[]>(existing?.categoryIds ?? []);
  const [requireComment, setRequireComment] = useState(existing?.requireComment ?? false);
  const [isActive, setIsActive] = useState(existing?.isActive ?? true);
  const [roleIds, setRoleIds] = useState<string[]>(existing?.roleIds ?? []);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function toggle(list: string[], id: string, setter: (value: string[]) => void) {
    setter(list.includes(id) ? list.filter((x) => x !== id) : [...list, id]);
  }

  function toggleDay(bit: number) {
    setWeekdayMask((current) => current ^ bit);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const numericValue = Number(value.replace(',', '.'));
    const numericPriority = Number(priority);

    if (!name.trim()) return setError('Введите название правила.');
    if (!Number.isFinite(numericValue) || numericValue <= 0) return setError('Значение должно быть больше нуля.');
    if (mode === 'PERCENT' && numericValue > 100) return setError('Процент не может быть больше 100%.');
    if (!Number.isInteger(numericPriority) || numericPriority < 0 || numericPriority > 9999) return setError('Приоритет должен быть целым числом от 0 до 9999.');
    if (weekdayMask === 0) return setError('Выберите хотя бы один день недели.');

    const input: UpsertAdjustmentPresetInput = {
      name: name.trim(),
      type,
      mode,
      scope: applicationMode === 'AUTOMATIC' || type === 'SERVICE_CHARGE' ? 'ORDER' : scope,
      applicationMode,
      timeBasis,
      value: numericValue,
      priority: numericPriority,
      canStack,
      weekdayMask,
      startMinute: allDay ? null : timeToMinute(startTime),
      endMinute: allDay ? null : timeToMinute(endTime),
      requireComment: applicationMode === 'MANUAL' ? requireComment : false,
      isActive,
      roleIds: applicationMode === 'MANUAL' ? roleIds : [],
      productIds,
      categoryIds,
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
      <form className="modal-card adjustment-modal pricing-rule-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">PRICING RULE</div>
            <h2>{editor.kind === 'create' ? 'Новое правило' : 'Настройка правила'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Название</span>
          <input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} placeholder="Happy Hour Drinks -20%" autoFocus />
        </label>

        <div className="pricing-section">
          <h3>1. Расчёт и применение</h3>
          <div className="adjustment-form-grid">
            <label><span>Тип</span><select value={type} onChange={(e) => setType(e.target.value as 'DISCOUNT' | 'SERVICE_CHARGE')}><option value="DISCOUNT">Скидка</option><option value="SERVICE_CHARGE">Надбавка / сервис</option></select></label>
            <label><span>Расчёт</span><select value={mode} onChange={(e) => setMode(e.target.value as 'PERCENT' | 'FIXED')}><option value="PERCENT">Процент</option><option value="FIXED">Фиксированная сумма</option></select></label>
            <label><span>{mode === 'PERCENT' ? 'Процент' : 'Сумма'}</span><input value={value} onChange={(e) => setValue(e.target.value)} inputMode="decimal" /></label>
            <label><span>Применение</span><select value={applicationMode} onChange={(e) => setApplicationMode(e.target.value as 'MANUAL' | 'AUTOMATIC')}><option value="MANUAL">Вручную на POS</option><option value="AUTOMATIC">Автоматически</option></select></label>
            <label><span>Область</span><select value={applicationMode === 'AUTOMATIC' || type === 'SERVICE_CHARGE' ? 'ORDER' : scope} disabled={applicationMode === 'AUTOMATIC' || type === 'SERVICE_CHARGE'} onChange={(e) => setScope(e.target.value as 'ORDER' | 'GUEST' | 'BOTH')}><option value="ORDER">Весь заказ</option><option value="GUEST">Выбранный гость</option><option value="BOTH">Заказ или гость</option></select></label>
            <label><span>Приоритет</span><input type="number" min="0" max="9999" value={priority} onChange={(e) => setPriority(e.target.value)} /></label>
          </div>
          <label className="toggle-row"><span><strong>Можно совмещать</strong><small>Если выключено, правило не применяется к позициям, уже затронутым другим правилом, и блокирует их для следующих правил.</small></span><input type="checkbox" checked={canStack} onChange={(e) => setCanStack(e.target.checked)} /></label>
        </div>

        <div className="pricing-section">
          <h3>2. На что действует</h3>
          <p className="pricing-help">Если ничего не выбрано — правило действует на все блюда. Блюда и категории объединяются.</p>
          <div className="pricing-target-columns">
            <div>
              <strong>Категории</strong>
              <div className="pricing-check-list">
                {data.categories.map((category) => (
                  <label key={category.id} className="adjustment-role-option">
                    <input type="checkbox" checked={categoryIds.includes(category.id)} onChange={() => toggle(categoryIds, category.id, setCategoryIds)} />
                    <span>{category.name}{!category.isActive && <small>Неактивна</small>}</span>
                  </label>
                ))}
              </div>
            </div>
            <div>
              <strong>Блюда</strong>
              <div className="pricing-check-list">
                {data.products.map((product) => (
                  <label key={product.id} className="adjustment-role-option">
                    <input type="checkbox" checked={productIds.includes(product.id)} onChange={() => toggle(productIds, product.id, setProductIds)} />
                    <span>{product.name}{!product.isActive && <small>Неактивно</small>}</span>
                  </label>
                ))}
              </div>
            </div>
          </div>
        </div>

        <div className="pricing-section">
          <h3>3. Дни и время</h3>
          <div className="weekday-row">
            {DAYS.map(([label, bit]) => (
              <button type="button" key={bit} className={(weekdayMask & bit) !== 0 ? 'day-chip selected' : 'day-chip'} onClick={() => toggleDay(bit)}>{label}</button>
            ))}
          </div>
          <label className="toggle-row"><span><strong>Весь день</strong><small>Выключите, чтобы задать интервал, включая переход через полночь.</small></span><input type="checkbox" checked={allDay} onChange={(e) => setAllDay(e.target.checked)} /></label>
          {!allDay && (
            <div className="adjustment-form-grid">
              <label><span>С</span><input type="time" value={startTime} onChange={(e) => setStartTime(e.target.value)} /></label>
              <label><span>До</span><input type="time" value={endTime} onChange={(e) => setEndTime(e.target.value)} /></label>
            </div>
          )}
          <label>
            <span>Время проверять по</span>
            <select value={timeBasis} onChange={(e) => setTimeBasis(e.target.value as 'ORDER_OPENED_AT' | 'ITEM_ADDED_AT')}>
              <option value="ITEM_ADDED_AT">Времени добавления блюда</option>
              <option value="ORDER_OPENED_AT">Времени открытия заказа</option>
            </select>
          </label>
        </div>

        {applicationMode === 'MANUAL' && (
          <div className="pricing-section">
            <h3>4. Доступ на POS</h3>
            <div className="adjustment-role-grid">
              {data.roles.map((role) => (
                <label className={`adjustment-role-option ${!role.canApplyAdjustments ? 'disabled' : ''}`} key={role.id}>
                  <input type="checkbox" checked={roleIds.includes(role.id)} disabled={!role.canApplyAdjustments} onChange={() => toggle(roleIds, role.id, setRoleIds)} />
                  <span>{role.name}{!role.canApplyAdjustments && <small>Нет права применять скидки</small>}</span>
                </label>
              ))}
            </div>
            <label className="toggle-row"><span><strong>Требовать комментарий</strong><small>Кассир обязан указать причину применения.</small></span><input type="checkbox" checked={requireComment} onChange={(e) => setRequireComment(e.target.checked)} /></label>
          </div>
        )}

        <label className="toggle-row"><span><strong>Правило активно</strong><small>Отключённое правило не применяется к новым заказам.</small></span><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /></label>

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button>
        </div>
      </form>
    </div>
  );
}

function minuteToTime(value: number | null | undefined) {
  if (value == null) return null;
  const hours = Math.floor(value / 60).toString().padStart(2, '0');
  const minutes = (value % 60).toString().padStart(2, '0');
  return `${hours}:${minutes}`;
}

function timeToMinute(value: string) {
  const [hours, minutes] = value.split(':').map(Number);
  return hours * 60 + minutes;
}

function targetLabel(preset: BackOfficeAdjustmentPreset, data: BackOfficeAdjustments) {
  const categoryNames = data.categories.filter((x) => preset.categoryIds.includes(x.id)).map((x) => x.name);
  const productNames = data.products.filter((x) => preset.productIds.includes(x.id)).map((x) => x.name);
  if (categoryNames.length === 0 && productNames.length === 0) return 'все блюда';
  return [...categoryNames.map((x) => `кат. ${x}`), ...productNames].join(', ');
}

function scheduleLabel(preset: BackOfficeAdjustmentPreset) {
  const days = DAYS.filter(([, bit]) => (preset.weekdayMask & bit) !== 0).map(([label]) => label).join(', ');
  const time = preset.startMinute == null || preset.endMinute == null
    ? 'весь день'
    : `${minuteToTime(preset.startMinute)}–${minuteToTime(preset.endMinute)}`;
  return `${days} · ${time}`;
}
