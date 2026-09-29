import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeNomenclature,
  type NomenclatureItem,
  createNomenclatureItem,
  getBackOfficeNomenclature,
  updateNomenclatureItem,
  updateNomenclatureRecipe,
} from './api';
import './nomenclature.css';

type EditorState =
  | { kind: 'create' }
  | { kind: 'edit'; item: NomenclatureItem }
  | null;

const TYPE_LABELS: Record<NomenclatureItem['type'], string> = {
  DISH: 'Блюдо',
  GOODS: 'Товар',
  PREPARATION: 'Заготовка',
  MODIFIER: 'Модификатор',
};

const UNIT_OPTIONS = [
  ['pcs', 'шт'],
  ['kg', 'кг'],
  ['g', 'г'],
  ['l', 'л'],
  ['ml', 'мл'],
] as const;

const INVENTORY_ACCOUNT_HELP: Record<string, string> = {
  '201-1': 'Сырьё и продукты, которые используются для приготовления блюд и заготовок.',
  '201-2': 'Вспомогательные материалы, которые используются в работе ресторана.',
  '201-3': 'Упаковка: контейнеры, коробки, пакеты и другие упаковочные материалы.',
  '205': 'Товары для перепродажи без переработки или приготовления.',
};

export function NomenclaturePage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeNomenclature | null>(null);
  const [query, setQuery] = useState('');
  const [typeFilter, setTypeFilter] = useState<'ALL' | NomenclatureItem['type']>('ALL');
  const [editor, setEditor] = useState<EditorState>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeNomenclature(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить номенклатуру');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const visibleItems = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    return (data?.items ?? []).filter((item) => {
      if (typeFilter !== 'ALL' && item.type !== typeFilter) return false;
      if (!normalized) return true;
      return item.name.toLowerCase().includes(normalized) ||
        (item.sku ?? '').toLowerCase().includes(normalized);
    });
  }, [data, query, typeFilter]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем номенклатуру…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Номенклатура</h1>
          <p>Единый справочник товаров, блюд, заготовок и модификаторов.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditor({ kind: 'create' })}>+ Позиция</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span><button onClick={() => void refresh()}>Повторить</button></div>}

      <div className="nomenclature-toolbar">
        <div className="nomenclature-type-tabs">
          <button className={typeFilter === 'ALL' ? 'active' : ''} onClick={() => setTypeFilter('ALL')}>Все</button>
          {(Object.keys(TYPE_LABELS) as NomenclatureItem['type'][]).map((type) => (
            <button key={type} className={typeFilter === type ? 'active' : ''} onClick={() => setTypeFilter(type)}>
              {TYPE_LABELS[type]}
            </button>
          ))}
        </div>
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Поиск по названию или SKU" />
      </div>

      <div className="nomenclature-grid">
        {visibleItems.map((item) => (
          <button className="nomenclature-card" key={item.id} onClick={() => setEditor({ kind: 'edit', item })}>
            <div className="nomenclature-card-head">
              <span className="badge neutral">{TYPE_LABELS[item.type]}</span>
              {!item.isActive && <span className="badge neutral">Отключено</span>}
            </div>
            <strong>{item.name}</strong>
            <span>{item.sku ? 'SKU ' + item.sku + ' · ' : ''}{unitLabel(item.unit)}</span>
            <div className="nomenclature-card-meta">
              <small>
                {item.trackStock
                  ? `Складской учёт · ${item.inventoryAccountCode ?? '201-1'}`
                  : 'Без складского учёта'}
              </small>
              {(item.type === 'DISH' || item.type === 'PREPARATION' || item.type === 'MODIFIER') && (
                <small>Техкарта: {item.recipe.length} поз.</small>
              )}
            </div>
          </button>
        ))}
      </div>

      {visibleItems.length === 0 && (
        <div className="empty-state">
          <strong>Ничего не найдено</strong>
          <span>Измените фильтр или создайте новую позицию.</span>
        </div>
      )}

      {editor && data && (
        <NomenclatureEditor
          editor={editor}
          allItems={data.items}
          categories={data.categories}
          preparationPlaceTypes={data.preparationPlaceTypes}
          inventoryAccounts={data.inventoryAccounts}
          currencyCode={data.currencyCode}
          token={token}
          canManage={canManage}
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

function NomenclatureEditor({
  editor,
  allItems,
  categories,
  preparationPlaceTypes,
  inventoryAccounts,
  currencyCode,
  token,
  canManage,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  allItems: NomenclatureItem[];
  categories: BackOfficeNomenclature['categories'];
  preparationPlaceTypes: BackOfficeNomenclature['preparationPlaceTypes'];
  inventoryAccounts: BackOfficeNomenclature['inventoryAccounts'];
  currencyCode: string;
  token: string;
  canManage: boolean;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const item = editor.kind === 'edit' ? editor.item : null;
  const [tab, setTab] = useState<'main' | 'sale' | 'recipe'>('main');
  const [name, setName] = useState(item?.name ?? '');
  const [sku, setSku] = useState(item?.sku ?? '');
  const [type, setType] = useState<NomenclatureItem['type']>(item?.type ?? 'GOODS');
  const [unit, setUnit] = useState(item?.unit ?? 'pcs');
  const [minStock, setMinStock] = useState(String(item?.minStock ?? 0));
  const [trackStock, setTrackStock] = useState(item?.trackStock ?? true);
  const [inventoryAccountCode, setInventoryAccountCode] = useState(item?.inventoryAccountCode ?? '201-1');
  const [isActive, setIsActive] = useState(item?.isActive ?? true);
  const [isSellable, setIsSellable] = useState(item?.isSellable ?? false);
  const [price, setPrice] = useState(item?.currentPrice?.toString() ?? '0');
  const [categoryId, setCategoryId] = useState(item?.categoryId ?? '');
  const [preparationPlaceTypeId, setPreparationPlaceTypeId] = useState(item?.preparationPlaceTypeId ?? '');
  const [recipe, setRecipe] = useState(
    item?.recipe.map((line) => ({
      ingredientProductId: line.ingredientProductId,
      quantity: String(line.quantity),
    })) ?? [],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const recipeEnabled = type === 'DISH' || type === 'PREPARATION' || type === 'MODIFIER';
  const ingredientCandidates = allItems.filter((candidate) =>
    candidate.id !== item?.id &&
    candidate.isActive &&
    (candidate.type === 'GOODS' || candidate.type === 'PREPARATION')
  );

  async function saveMain(event: FormEvent) {
    event.preventDefault();
    const min = Number(minStock.replace(',', '.'));
    const numericPrice = Number(price.replace(',', '.'));
    if (!name.trim() || !Number.isFinite(min) || min < 0) return;
    if (
      isSellable &&
      (
        !Number.isFinite(numericPrice) ||
        numericPrice < -1000000 ||
        numericPrice > 1000000 ||
        (type !== 'MODIFIER' && numericPrice < 0)
      )
    ) {
      setError(
        type === 'MODIFIER'
          ? 'Укажите изменение цены от -1000000 до 1000000.'
          : 'Укажите корректную неотрицательную цену продажи.',
      );
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const input = {
        name: name.trim(),
        sku: sku.trim() || null,
        type,
        unit,
        minStock: min,
        trackStock,
        inventoryAccountCode: trackStock ? inventoryAccountCode : null,
        isSellable,
        isActive,
        sortOrder: item?.sortOrder ?? 0,
        categoryId: categoryId || null,
        preparationPlaceTypeId: preparationPlaceTypeId || null,
        price: isSellable ? numericPrice : null,
      };

      if (editor.kind === 'create') {
        const created = await createNomenclatureItem(token, input);
        if (recipeEnabled && recipe.length > 0) {
          await updateNomenclatureRecipe(token, created.id, normalizeRecipe(recipe));
        }
      } else {
        await updateNomenclatureItem(token, editor.item.id, input);
        if (recipeEnabled) {
          await updateNomenclatureRecipe(token, editor.item.id, normalizeRecipe(recipe));
        }
      }

      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить позицию');
    } finally {
      setSaving(false);
    }
  }

  function addRecipeLine() {
    const used = new Set(recipe.map((line) => line.ingredientProductId));
    const first = ingredientCandidates.find((candidate) => !used.has(candidate.id));
    if (!first) return;
    setRecipe((current) => [...current, { ingredientProductId: first.id, quantity: '1' }]);
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card nomenclature-modal" onSubmit={saveMain}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">НОМЕНКЛАТУРА</div>
            <h2>{editor.kind === 'create' ? 'Новая позиция' : item?.name}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="nomenclature-editor-tabs">
          <button type="button" className={tab === 'main' ? 'active' : ''} onClick={() => setTab('main')}>Основное</button>
          <button type="button" className={tab === 'sale' ? 'active' : ''} onClick={() => setTab('sale')}>Продажа</button>
          {recipeEnabled && (
            <button type="button" className={tab === 'recipe' ? 'active' : ''} onClick={() => setTab('recipe')}>Тех. карта</button>
          )}
        </div>

        {tab === 'main' ? (
          <>
            <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} disabled={!canManage} /></label>
            <div className="form-grid">
              <label>
                <span>Тип</span>
                <select value={type} onChange={(e) => setType(e.target.value as NomenclatureItem['type'])} disabled={!canManage}>
                  {(Object.keys(TYPE_LABELS) as NomenclatureItem['type'][]).map((key) => <option key={key} value={key}>{TYPE_LABELS[key]}</option>)}
                </select>
              </label>
              <label><span>SKU / артикул</span><input value={sku} onChange={(e) => setSku(e.target.value)} disabled={!canManage} /></label>
              <label>
                <span>Единица измерения</span>
                <select value={unit} onChange={(e) => setUnit(e.target.value)} disabled={!canManage}>
                  {UNIT_OPTIONS.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                </select>
              </label>
              <label><span>Минимальный остаток</span><input value={minStock} onChange={(e) => setMinStock(e.target.value)} disabled={!canManage} /></label>
            </div>
            <label className="toggle-row">
              <span><strong>Вести складской учёт</strong><small>Остатки и движения будут учитываться по этой позиции.</small></span>
              <input type="checkbox" checked={trackStock} onChange={(e) => setTrackStock(e.target.checked)} disabled={!canManage} />
            </label>
            {trackStock && (
              <label>
                <span>Счёт складского учёта</span>
                <select
                  value={inventoryAccountCode}
                  onChange={(e) => setInventoryAccountCode(e.target.value)}
                  disabled={!canManage}
                >
                  {inventoryAccounts.map((account) => (
                    <option key={account.code} value={account.code}>
                      {account.code} · {account.name}
                    </option>
                  ))}
                </select>
                <small className="field-help">
                  Все движения этой позиции будут автоматически отражаться по выбранному счёту.
                </small>
                <div className="inventory-account-help">
                  <strong>
                    {inventoryAccounts.find((account) => account.code === inventoryAccountCode)?.code}
                    {' · '}
                    {inventoryAccounts.find((account) => account.code === inventoryAccountCode)?.name}
                  </strong>
                  <span>
                    {INVENTORY_ACCOUNT_HELP[inventoryAccountCode] ??
                      'Складской счёт для этой позиции.'}
                  </span>
                </div>
              </label>
            )}
            {item && (
              <label className="toggle-row">
                <span><strong>Позиция активна</strong><small>Отключённая позиция не используется в новых операциях.</small></span>
                <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} disabled={!canManage} />
              </label>
            )}
          </>
        ) : tab === 'sale' ? (
          <div className="nomenclature-sale-tab">
            <label className="toggle-row">
              <span>
                <strong>{type === 'MODIFIER' ? 'Доступен на POS' : 'Продаётся'}</strong>
                <small>
                  {type === 'MODIFIER'
                    ? 'Модификатор можно будет включать в группы и выбирать на POS.'
                    : 'Активная позиция будет доступна POS для продажи.'}
                </small>
              </span>
              <input type="checkbox" checked={isSellable} onChange={(e) => setIsSellable(e.target.checked)} disabled={!canManage} />
            </label>

            <div className="form-grid">
              <label>
                <span>{type === 'MODIFIER' ? 'Изменение цены' : 'Цена продажи'}</span>
                <input
                  value={price}
                  onChange={(e) => setPrice(e.target.value)}
                  inputMode="decimal"
                  disabled={!canManage || !isSellable}
                />
                <small className="field-help">{currencyCode}</small>
              </label>

              {type !== 'MODIFIER' && (
                <label>
                  <span>Категория продажи</span>
                  <select
                    value={categoryId}
                    onChange={(e) => setCategoryId(e.target.value)}
                    disabled={!canManage || !isSellable}
                  >
                    <option value="">Выберите категорию</option>
                    {categories.filter((x) => x.isActive).map((category) => (
                      <option key={category.id} value={category.id}>{category.name}</option>
                    ))}
                  </select>
                </label>
              )}

              <label className="full-field">
                <span>Тип места приготовления</span>
                <select
                  value={preparationPlaceTypeId}
                  onChange={(e) => setPreparationPlaceTypeId(e.target.value)}
                  disabled={!canManage || !isSellable}
                >
                  <option value="">Не назначено</option>
                  {preparationPlaceTypes.filter((x) => x.isActive).map((typeOption) => (
                    <option key={typeOption.id} value={typeOption.id}>{typeOption.name}</option>
                  ))}
                </select>
                <small className="field-help">
                  Здесь выбирается логический тип. Конкретное место, принтер и склад определяются маршрутизацией.
                </small>
              </label>
            </div>
          </div>
        ) : (
          <div className="recipe-editor">
            <div className="recipe-editor-head">
              <div>
                <strong>Состав техкарты</strong>
                <span>Количество указано на 1 {unitLabel(unit)} готовой позиции.</span>
                <small>В составе можно использовать товары и заготовки. Циклические техкарты запрещены.</small>
              </div>
              {canManage && (
                <button
                  type="button"
                  className="secondary-button compact"
                  onClick={addRecipeLine}
                  disabled={recipe.length >= ingredientCandidates.length}
                >
                  + Ингредиент
                </button>
              )}
            </div>

            {recipe.length === 0 ? (
              <div className="recipe-empty">Ингредиенты ещё не добавлены.</div>
            ) : (
              <div className="recipe-lines">
                {recipe.map((line, index) => {
                  const ingredient = allItems.find((x) => x.id === line.ingredientProductId);
                  return (
                    <div className="recipe-line" key={index}>
                      <select
                        value={line.ingredientProductId}
                        onChange={(e) => setRecipe((current) => current.map((x, i) => i === index ? { ...x, ingredientProductId: e.target.value } : x))}
                        disabled={!canManage}
                      >
                        {ingredientCandidates
                          .filter((candidate) =>
                            candidate.id === line.ingredientProductId ||
                            !recipe.some((existing, existingIndex) =>
                              existingIndex !== index &&
                              existing.ingredientProductId === candidate.id
                            )
                          )
                          .map((candidate) => (
                            <option value={candidate.id} key={candidate.id}>
                              {candidate.name} · {TYPE_LABELS[candidate.type]}
                            </option>
                          ))}
                      </select>
                      <input
                        value={line.quantity}
                        onChange={(e) => setRecipe((current) => current.map((x, i) => i === index ? { ...x, quantity: e.target.value } : x))}
                        inputMode="decimal"
                        disabled={!canManage}
                      />
                      <span>{unitLabel(ingredient?.unit ?? '')}</span>
                      {canManage && (
                        <button type="button" className="text-button" onClick={() => setRecipe((current) => current.filter((_, i) => i !== index))}>Удалить</button>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}

        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          {canManage && <button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button>}
        </div>
      </form>
    </div>
  );
}

function normalizeRecipe(lines: Array<{ ingredientProductId: string; quantity: string }>) {
  return lines.map((line) => ({
    ingredientProductId: line.ingredientProductId,
    quantity: Number(line.quantity.replace(',', '.')),
  })).filter((line) => line.ingredientProductId && Number.isFinite(line.quantity) && line.quantity > 0);
}

function unitLabel(unit: string) {
  return UNIT_OPTIONS.find(([value]) => value === unit)?.[1] ?? unit;
}
