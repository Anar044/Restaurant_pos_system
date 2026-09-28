import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeModifierGroup,
  type BackOfficeModifiers,
  createModifierGroup,
  getBackOfficeModifiers,
  setModifierGroupItems,
  setProductModifierGroups,
  updateModifierGroup,
} from './api';
import './modifiers.css';

type EditorState =
  | { kind: 'group-create' }
  | { kind: 'group-edit'; group: BackOfficeModifierGroup }
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
      setError(e instanceof Error ? e.message : 'Не удалось загрузить группы модификаторов');
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
    setSavingLink('modifier:' + modifierId);
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

    setSavingLink('product:' + productId);
    setError(null);
    try {
      const next = checked
        ? [...product.groupIds.filter((id) => id !== group.id), group.id]
        : product.groupIds.filter((id) => id !== group.id);
      await setProductModifierGroups(token, product.id, next);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось назначить группу позиции');
    } finally {
      setSavingLink(null);
    }
  }

  if (!data && loading) {
    return <div className="empty-state">Загружаем группы модификаторов…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Группы модификаторов</h1>
          <p>
            Здесь настраиваются только группы и правила выбора. Сами модификаторы
            создаются и редактируются в «Номенклатуре».
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'group-create' })}>+ Группа</button>
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="stats-grid">
        <ModifierStat label="Группы" value={data?.groups.filter((x) => x.isActive).length ?? 0} detail="активных" />
        <ModifierStat label="Модификаторы" value={data?.modifiers.filter((x) => x.isActive).length ?? 0} detail="из номенклатуры" />
        <ModifierStat
          label="Позиций с группами"
          value={data?.products.filter((x) => x.groupIds.length > 0).length ?? 0}
          detail={(data?.products.length ?? 0) + ' продаваемых позиций'}
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
              className={'modifier-group-row ' + (selectedGroupId === group.id ? 'selected ' : '') + (!group.isActive ? 'inactive' : '')}
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
            <div className="modifier-empty-small">Создайте первую группу, например «Добавки».</div>
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
                <button className="secondary-button compact" onClick={() => setEditor({ kind: 'group-edit', group: selectedGroup })}>
                  Настроить
                </button>
              </div>

              <div className="modifier-section">
                <div className="modifier-section-heading">
                  <div>
                    <strong>Модификаторы в группе</strong>
                    <small>Список берётся из номенклатуры с типом «Модификатор».</small>
                  </div>
                  <span className="route-badge">{selectedGroup.modifiers.length} выбрано</span>
                </div>

                <div className="modifier-option-grid">
                  {(data?.modifiers ?? []).map((modifier) => {
                    const checked = selectedGroup.modifiers.some((item) => item.id === modifier.id);
                    return (
                      <label
                        key={modifier.id}
                        className={'modifier-option-card ' + (checked ? 'checked ' : '') + (!modifier.isActive ? 'inactive' : '')}
                      >
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
                      </label>
                    );
                  })}
                </div>

                {(data?.modifiers.length ?? 0) === 0 && (
                  <div className="menu-empty">
                    <strong>Нет модификаторов</strong>
                    <span>Создайте позицию типа «Модификатор» в разделе «Номенклатура».</span>
                  </div>
                )}
              </div>

              <div className="modifier-section">
                <div className="modifier-section-heading">
                  <div>
                    <strong>Назначить продаваемым позициям</strong>
                    <small>На POS группа появится только у отмеченных блюд или товаров.</small>
                  </div>
                  <span className="route-badge">{selectedGroup.productIds.length} поз.</span>
                </div>

                <div className="modifier-product-list">
                  {(data?.products ?? []).map((product) => {
                    const checked = product.groupIds.includes(selectedGroup.id);
                    return (
                      <label key={product.id} className={'modifier-product-row ' + (!product.isActive ? 'inactive' : '')}>
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

      {editor && (
        <ModifierGroupEditor
          editor={editor}
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

function ModifierGroupEditor({
  editor,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const group = editor.kind === 'group-edit' ? editor.group : null;
  const [name, setName] = useState(group?.name ?? '');
  const [minSelections, setMinSelections] = useState(group?.minSelections ?? 0);
  const [maxSelections, setMaxSelections] = useState(group?.maxSelections ?? 1);
  const [isRequired, setIsRequired] = useState(group?.isRequired ?? false);
  const [isActive, setIsActive] = useState(group?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'group-create') {
        await createModifierGroup(token, {
          name: name.trim(),
          minSelections,
          maxSelections,
          isRequired,
        });
      } else {
        await updateModifierGroup(token, editor.group.id, {
          name: name.trim(),
          minSelections,
          maxSelections,
          isRequired,
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить группу');
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
            <div className="eyebrow">ГРУППА МОДИФИКАТОРОВ</div>
            <h2>{editor.kind === 'group-create' ? 'Новая группа' : 'Настройки группы'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="form-grid">
          <label className="full-field">
            <span>Название</span>
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} autoFocus />
          </label>
          <label>
            <span>Минимум выборов</span>
            <input type="number" min={0} max={100} value={minSelections} onChange={(e) => setMinSelections(Number(e.target.value))} />
          </label>
          <label>
            <span>Максимум выборов</span>
            <input type="number" min={1} max={100} value={maxSelections} onChange={(e) => setMaxSelections(Number(e.target.value))} />
          </label>
          <label className="toggle-row full-field">
            <span>
              <strong>Обязательная группа</strong>
              <small>Кассир не сможет добавить позицию, пока не сделает обязательный выбор.</small>
            </span>
            <input type="checkbox" checked={isRequired} onChange={(e) => requiredChanged(e.target.checked)} />
          </label>
        </div>

        {editor.kind === 'group-edit' && (
          <label className="toggle-row">
            <span><strong>Группа активна</strong><small>Неактивная группа не показывается на POS.</small></span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

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

function priceText(value: number, currencyCode: string) {
  if (value === 0) return 'без изменения цены';
  const sign = value > 0 ? '+' : '';
  return sign + value.toFixed(2) + ' ' + currencyCode;
}
