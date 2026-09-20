import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeModifier,
  type BackOfficeModifierGroup,
  type BackOfficeModifiers,
  createModifier,
  createModifierGroup,
  getBackOfficeModifiers,
  setModifierGroupItems,
  setProductModifierGroups,
  updateModifier,
  updateModifierGroup,
} from './api';
import './modifiers.css';

type EditorState =
  | { kind: 'group-create' }
  | { kind: 'group-edit'; group: BackOfficeModifierGroup }
  | { kind: 'modifier-create' }
  | { kind: 'modifier-edit'; modifier: BackOfficeModifier }
  | null;

export function ModifiersPage({ token }: { token: string }) {
  const [data, setData] = useState<BackOfficeModifiers | null>(null);
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState>(null);
  const [loading, setLoading] = useState(true);
  const [savingLink, setSavingLink] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const next = await getBackOfficeModifiers(token);
      setData(next);
      setSelectedGroupId((current) => {
        if (current && next.groups.some((group) => group.id === current)) return current;
        return next.groups[0]?.id ?? null;
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить модификаторы');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const selectedGroup = useMemo(
    () => data?.groups.find((group) => group.id === selectedGroupId) ?? null,
    [data, selectedGroupId],
  );

  async function toggleModifier(group: BackOfficeModifierGroup, modifierId: string, checked: boolean) {
    if (!data || savingLink) return;
    setSavingLink(`modifier:${modifierId}`);
    setError(null);
    try {
      const current = group.modifiers.map((item) => item.id);
      const next = checked
        ? [...current.filter((id) => id !== modifierId), modifierId]
        : current.filter((id) => id !== modifierId);
      await setModifierGroupItems(token, group.id, next);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось изменить состав группы');
    } finally {
      setSavingLink(null);
    }
  }

  async function toggleProduct(group: BackOfficeModifierGroup, productId: string, checked: boolean) {
    if (!data || savingLink) return;
    const product = data.products.find((item) => item.id === productId);
    if (!product) return;

    setSavingLink(`product:${productId}`);
    setError(null);
    try {
      const next = checked
        ? [...product.groupIds.filter((id) => id !== group.id), group.id]
        : product.groupIds.filter((id) => id !== group.id);
      await setProductModifierGroups(token, product.id, next);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось назначить группу блюду');
    } finally {
      setSavingLink(null);
    }
  }

  if (!data && loading) {
    return <div className="empty-state">Загружаем модификаторы…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">МЕНЮ · НАСТРОЙКИ</div>
          <h1>Модификаторы</h1>
          <p>Размеры, соусы, добавки, степень прожарки и другие варианты блюда.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="secondary-button" onClick={() => setEditor({ kind: 'modifier-create' })}>+ Вариант</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'group-create' })}>+ Группа</button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span><button onClick={() => void refresh()}>Повторить</button></div>}

      <div className="stats-grid">
        <ModifierStat label="Группы" value={data?.groups.filter((x) => x.isActive).length ?? 0} detail="активных" />
        <ModifierStat label="Варианты" value={data?.modifiers.filter((x) => x.isActive).length ?? 0} detail="активных" />
        <ModifierStat
          label="Блюда с модификаторами"
          value={data?.products.filter((x) => x.groupIds.length > 0).length ?? 0}
          detail={`${data?.products.length ?? 0} блюд всего`}
        />
      </div>

      <div className="modifier-workspace">
        <aside className="modifier-groups-panel">
          <div className="modifier-panel-title">
            <div><strong>Группы</strong><small>{data?.groups.length ?? 0} всего</small></div>
            <button className="mini-action" onClick={() => setEditor({ kind: 'group-create' })}>+</button>
          </div>

          {(data?.groups ?? []).map((group) => (
            <button
              key={group.id}
              className={`modifier-group-row ${selectedGroupId === group.id ? 'selected' : ''} ${!group.isActive ? 'inactive' : ''}`}
              onClick={() => setSelectedGroupId(group.id)}
            >
              <span>
                <strong>{group.name}</strong>
                <small>
                  {group.isRequired ? 'Обязательно' : 'Необязательно'} · {group.minSelections}–{group.maxSelections}
                </small>
              </span>
              <b>{group.modifiers.length}</b>
            </button>
          ))}

          {(data?.groups.length ?? 0) === 0 && (
            <div className="modifier-empty-small">Создайте первую группу, например «Размер».</div>
          )}
        </aside>

        <div className="modifier-detail-panel">
          {selectedGroup ? (
            <>
              <div className="modifier-detail-header">
                <div>
                  <div className="eyebrow">{selectedGroup.isRequired ? 'ОБЯЗАТЕЛЬНАЯ ГРУППА' : 'НЕОБЯЗАТЕЛЬНАЯ ГРУППА'}</div>
                  <h2>{selectedGroup.name}</h2>
                  <p>Выбор: минимум {selectedGroup.minSelections}, максимум {selectedGroup.maxSelections}.</p>
                </div>
                <button className="secondary-button compact" onClick={() => setEditor({ kind: 'group-edit', group: selectedGroup })}>Настроить</button>
              </div>

              <div className="modifier-section">
                <div className="modifier-section-heading">
                  <div><strong>Варианты в группе</strong><small>Отметьте доступные варианты и их доплату.</small></div>
                  <button className="text-button" onClick={() => setEditor({ kind: 'modifier-create' })}>+ Новый вариант</button>
                </div>

                <div className="modifier-option-grid">
                  {(data?.modifiers ?? []).map((modifier) => {
                    const checked = selectedGroup.modifiers.some((item) => item.id === modifier.id);
                    return (
                      <label key={modifier.id} className={`modifier-option-card ${checked ? 'checked' : ''} ${!modifier.isActive ? 'inactive' : ''}`}>
                        <input
                          type="checkbox"
                          checked={checked}
                          disabled={savingLink !== null || !modifier.isActive}
                          onChange={(e) => void toggleModifier(selectedGroup, modifier.id, e.target.checked)}
                        />
                        <span>
                          <strong>{modifier.name}</strong>
                          <small>{priceText(modifier.priceDelta, data?.currencyCode ?? 'AZN')}</small>
                        </span>
                        <button
                          type="button"
                          className="modifier-inline-edit"
                          onClick={(e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            setEditor({ kind: 'modifier-edit', modifier });
                          }}
                        >
                          Изменить
                        </button>
                      </label>
                    );
                  })}
                </div>

                {(data?.modifiers.length ?? 0) === 0 && (
                  <div className="menu-empty"><strong>Нет вариантов</strong><span>Создайте, например, «Большой +2 AZN».</span></div>
                )}
              </div>

              <div className="modifier-section">
                <div className="modifier-section-heading">
                  <div><strong>Назначить блюдам</strong><small>На POS эта группа появится только у отмеченных блюд.</small></div>
                  <span className="route-badge">{selectedGroup.productIds.length} блюд</span>
                </div>
                <div className="modifier-product-list">
                  {(data?.products ?? []).map((product) => {
                    const checked = product.groupIds.includes(selectedGroup.id);
                    return (
                      <label key={product.id} className={`modifier-product-row ${!product.isActive ? 'inactive' : ''}`}>
                        <input
                          type="checkbox"
                          checked={checked}
                          disabled={savingLink !== null || !product.isActive}
                          onChange={(e) => void toggleProduct(selectedGroup, product.id, e.target.checked)}
                        />
                        <span>{product.name}</span>
                        <small>{checked ? 'Назначено' : 'Не назначено'}</small>
                      </label>
                    );
                  })}
                </div>
              </div>
            </>
          ) : (
            <div className="menu-empty">
              <strong>Выберите или создайте группу</strong>
              <span>Например: «Размер», «Соус», «Прожарка», «Добавки».</span>
              <button className="primary-button compact" onClick={() => setEditor({ kind: 'group-create' })}>+ Создать группу</button>
            </div>
          )}
        </div>
      </div>

      {editor && data && (
        <ModifierEditor
          editor={editor}
          currencyCode={data.currencyCode}
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

function ModifierStat({ label, value, detail }: { label: string; value: number; detail: string }) {
  return <div className="stat-card"><span>{label}</span><div><strong>{value}</strong><small>{detail}</small></div></div>;
}

function ModifierEditor({
  editor,
  currencyCode,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  currencyCode: string;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const group = editor.kind === 'group-edit' ? editor.group : null;
  const modifier = editor.kind === 'modifier-edit' ? editor.modifier : null;
  const isGroup = editor.kind.startsWith('group');
  const editing = editor.kind.endsWith('edit');

  const [name, setName] = useState(group?.name ?? modifier?.name ?? '');
  const [minSelections, setMinSelections] = useState(group?.minSelections ?? 0);
  const [maxSelections, setMaxSelections] = useState(group?.maxSelections ?? 1);
  const [isRequired, setIsRequired] = useState(group?.isRequired ?? false);
  const [priceDelta, setPriceDelta] = useState(modifier?.priceDelta ?? 0);
  const [isActive, setIsActive] = useState(group?.isActive ?? modifier?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'group-create') {
        await createModifierGroup(token, { name: name.trim(), minSelections, maxSelections, isRequired });
      } else if (editor.kind === 'group-edit') {
        await updateModifierGroup(token, editor.group.id, { name: name.trim(), minSelections, maxSelections, isRequired, isActive });
      } else if (editor.kind === 'modifier-create') {
        await createModifier(token, { name: name.trim(), priceDelta });
      } else {
        await updateModifier(token, editor.modifier.id, { name: name.trim(), priceDelta, isActive });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить');
    } finally {
      setSaving(false);
    }
  }

  function requiredChanged(value: boolean) {
    setIsRequired(value);
    if (value && minSelections < 1) setMinSelections(1);
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card modifier-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">{isGroup ? 'ГРУППА МОДИФИКАТОРОВ' : 'ВАРИАНТ'}</div>
            <h2>{editing ? 'Настройки' : isGroup ? 'Новая группа' : 'Новый вариант'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="form-grid">
          <label className="full-field">
            <span>Название</span>
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={160} autoFocus />
          </label>

          {isGroup ? (
            <>
              <label>
                <span>Минимум выборов</span>
                <input type="number" min={0} max={100} value={minSelections} onChange={(e) => setMinSelections(Number(e.target.value))} />
              </label>
              <label>
                <span>Максимум выборов</span>
                <input type="number" min={1} max={100} value={maxSelections} onChange={(e) => setMaxSelections(Number(e.target.value))} />
              </label>
              <label className="toggle-row full-field">
                <span><strong>Обязательная группа</strong><small>Кассир не сможет добавить блюдо, пока не сделает выбор.</small></span>
                <input type="checkbox" checked={isRequired} onChange={(e) => requiredChanged(e.target.checked)} />
              </label>
            </>
          ) : (
            <label className="full-field">
              <span>Изменение цены, {currencyCode}</span>
              <input type="number" step="0.01" min={-1000000} max={1000000} value={priceDelta} onChange={(e) => setPriceDelta(Number(e.target.value))} />
              <small className="field-hint">0 — без доплаты, 2 — +2 {currencyCode}, -1 — скидка 1 {currencyCode}.</small>
            </label>
          )}
        </div>

        {editing && (
          <label className="toggle-row">
            <span><strong>Активно</strong><small>Неактивный элемент не показывается на POS.</small></span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button
            className="primary-button"
            disabled={
              saving ||
              !name.trim() ||
              (isGroup && (maxSelections < 1 || minSelections < 0 || minSelections > maxSelections || (isRequired && minSelections < 1)))
            }
          >
            {saving ? 'Сохраняем…' : 'Сохранить'}
          </button>
        </div>
      </form>
    </div>
  );
}

function priceText(value: number, currency: string) {
  if (Math.abs(value) < 0.0001) return 'Без доплаты';
  return `${value > 0 ? '+' : ''}${value.toFixed(2)} ${currency}`;
}
