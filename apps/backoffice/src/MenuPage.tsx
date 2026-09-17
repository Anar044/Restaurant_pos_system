import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeMenu,
  type MenuCategory,
  type MenuProduct,
  createCategory,
  createProduct,
  getBackOfficeMenu,
  updateCategory,
  updateProduct,
  updateProductPrice,
} from './api';
import './menu.css';

type EditorState =
  | { kind: 'category-create' }
  | { kind: 'category-edit'; category: MenuCategory }
  | { kind: 'product-create'; categoryId?: string }
  | { kind: 'product-edit'; product: MenuProduct }
  | null;

export function MenuPage({ token }: { token: string }) {
  const [data, setData] = useState<BackOfficeMenu | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedCategoryId, setSelectedCategoryId] = useState<string>('all');
  const [editor, setEditor] = useState<EditorState>(null);
  const [query, setQuery] = useState('');

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeMenu(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить меню');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const allProducts = useMemo(
    () => data?.categories.flatMap((category) => category.products) ?? [],
    [data],
  );

  const visibleProducts = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    return allProducts.filter((product) => {
      if (selectedCategoryId !== 'all' && product.categoryId !== selectedCategoryId) return false;
      if (!normalized) return true;
      return product.name.toLowerCase().includes(normalized) ||
        (product.sku ?? '').toLowerCase().includes(normalized);
    });
  }, [allProducts, query, selectedCategoryId]);

  const stats = useMemo(() => ({
    categories: data?.categories.filter((x) => x.isActive).length ?? 0,
    products: allProducts.filter((x) => x.isActive).length,
    unassigned: allProducts.filter((x) => x.isActive && !x.kitchenStationId).length,
  }), [allProducts, data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем меню…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА</div>
          <h1>Меню</h1>
          <p>Управляйте категориями, блюдами, ценами и маршрутизацией на кухню.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="secondary-button" onClick={() => setEditor({ kind: 'category-create' })}>+ Категория</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'product-create', categoryId: selectedCategoryId === 'all' ? undefined : selectedCategoryId })}>
            + Блюдо
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
        <MenuStat label="Активные категории" value={stats.categories} detail={`${data?.categories.length ?? 0} всего`} />
        <MenuStat label="Активные блюда" value={stats.products} detail={`${allProducts.length} всего`} />
        <MenuStat label="Без станции" value={stats.unassigned} detail="нужно назначить маршрут" warning={stats.unassigned > 0} />
      </div>

      <div className="menu-workspace">
        <aside className="menu-categories-panel">
          <div className="menu-panel-heading">
            <div>
              <strong>Категории</strong>
              <small>{data?.categories.length ?? 0} всего</small>
            </div>
            <button className="mini-action" onClick={() => setEditor({ kind: 'category-create' })}>+</button>
          </div>

          <button
            className={`category-row ${selectedCategoryId === 'all' ? 'selected' : ''}`}
            onClick={() => setSelectedCategoryId('all')}
          >
            <span>Все блюда</span>
            <small>{allProducts.length}</small>
          </button>

          {data?.categories.map((category) => (
            <div className="category-line" key={category.id}>
              <button
                className={`category-row ${selectedCategoryId === category.id ? 'selected' : ''} ${!category.isActive ? 'inactive' : ''}`}
                onClick={() => setSelectedCategoryId(category.id)}
              >
                <span>{category.name}</span>
                <small>{category.products.length}</small>
              </button>
              <button className="category-edit" title="Настроить категорию" onClick={() => setEditor({ kind: 'category-edit', category })}>•••</button>
            </div>
          ))}
        </aside>

        <div className="menu-products-panel">
          <div className="menu-toolbar">
            <div>
              <strong>{selectedCategoryId === 'all' ? 'Все блюда' : data?.categories.find((x) => x.id === selectedCategoryId)?.name}</strong>
              <small>{visibleProducts.length} позиций</small>
            </div>
            <input
              className="menu-search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Поиск по названию или SKU"
            />
          </div>

          {visibleProducts.length === 0 ? (
            <div className="menu-empty">
              <strong>Здесь пока нет блюд</strong>
              <span>Создайте первую позицию меню.</span>
              <button className="primary-button compact" onClick={() => setEditor({ kind: 'product-create', categoryId: selectedCategoryId === 'all' ? undefined : selectedCategoryId })}>+ Добавить блюдо</button>
            </div>
          ) : (
            <div className="product-table-wrap">
              <table className="product-table">
                <thead>
                  <tr>
                    <th>Блюдо</th>
                    <th>Категория</th>
                    <th>Кухня</th>
                    <th>Цена</th>
                    <th>Статус</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {visibleProducts.map((product) => {
                    const category = data?.categories.find((x) => x.id === product.categoryId);
                    return (
                      <tr key={product.id} className={!product.isActive ? 'row-inactive' : ''}>
                        <td>
                          <div className="product-name-cell">
                            <div className="product-avatar">{product.name.slice(0, 1).toUpperCase()}</div>
                            <div>
                              <strong>{product.name}</strong>
                              <small>{product.sku ? `SKU: ${product.sku}` : 'SKU не указан'}</small>
                            </div>
                          </div>
                        </td>
                        <td>{category?.name ?? '—'}</td>
                        <td>
                          {product.kitchenStationName ? (
                            <span className="route-badge">{product.kitchenStationName}</span>
                          ) : (
                            <span className="route-badge warning">Не назначена</span>
                          )}
                        </td>
                        <td>
                          <strong className="price-value">
                            {product.currentPrice ? `${formatMoney(product.currentPrice.amount)} ${product.currentPrice.currencyCode}` : 'Нет цены'}
                          </strong>
                          {product.priceHistory.length > 1 && <small className="history-count">История: {product.priceHistory.length}</small>}
                        </td>
                        <td>
                          <span className={`badge ${product.isActive ? 'success' : 'neutral'}`}>
                            {product.isActive ? 'Активно' : 'Отключено'}
                          </span>
                        </td>
                        <td className="product-action-cell">
                          <button className="text-button" onClick={() => setEditor({ kind: 'product-edit', product })}>Настроить</button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {editor && data && (
        <MenuEditor
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

function MenuStat({ label, value, detail, warning = false }: { label: string; value: number; detail: string; warning?: boolean }) {
  return (
    <div className={`stat-card ${warning ? 'stat-warning' : ''}`}>
      <span>{label}</span>
      <div>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </div>
  );
}

function MenuEditor({
  editor,
  data,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  data: BackOfficeMenu;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const isCategory = editor.kind.startsWith('category');
  const editing = editor.kind.endsWith('edit');
  const category = editor.kind === 'category-edit' ? editor.category : null;
  const product = editor.kind === 'product-edit' ? editor.product : null;

  const defaultCategoryId = product?.categoryId ??
    (editor.kind === 'product-create' ? editor.categoryId : undefined) ??
    data.categories[0]?.id ?? '';

  const [name, setName] = useState(product?.name ?? category?.name ?? '');
  const [sortOrder, setSortOrder] = useState(product?.sortOrder ?? category?.sortOrder ?? 0);
  const [isActive, setIsActive] = useState(product?.isActive ?? category?.isActive ?? true);
  const [categoryId, setCategoryId] = useState(defaultCategoryId);
  const [stationId, setStationId] = useState(product?.kitchenStationId ?? '');
  const [sku, setSku] = useState(product?.sku ?? '');
  const [price, setPrice] = useState(product?.currentPrice?.amount ?? 0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title = editor.kind === 'category-create' ? 'Новая категория' :
    editor.kind === 'category-edit' ? 'Настройки категории' :
    editor.kind === 'product-create' ? 'Новое блюдо' :
    'Настройки блюда';

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);

    try {
      if (editor.kind === 'category-create') {
        await createCategory(token, { name: name.trim(), sortOrder });
      } else if (editor.kind === 'category-edit') {
        await updateCategory(token, editor.category.id, { name: name.trim(), sortOrder, isActive });
      } else if (editor.kind === 'product-create') {
        await createProduct(token, {
          categoryId,
          kitchenStationId: stationId || null,
          name: name.trim(),
          sku: sku.trim() || null,
          sortOrder,
          price,
        });
      } else {
        await updateProduct(token, editor.product.id, {
          categoryId,
          kitchenStationId: stationId || null,
          name: name.trim(),
          sku: sku.trim() || null,
          sortOrder,
          isActive,
        });
        if (editor.product.currentPrice?.amount !== price || !editor.product.currentPrice) {
          await updateProductPrice(token, editor.product.id, price);
        }
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
      <form className="modal-card menu-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">{isCategory ? 'КАТЕГОРИЯ' : 'БЛЮДО'}</div>
            <h2>{title}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="form-grid">
          <label className="full-field">
            <span>Название</span>
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={160} autoFocus />
          </label>

          {!isCategory && (
            <>
              <label>
                <span>Категория</span>
                <select value={categoryId} onChange={(e) => setCategoryId(e.target.value)} required>
                  {data.categories.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}
                </select>
              </label>
              <label>
                <span>Кухонная станция</span>
                <select value={stationId} onChange={(e) => setStationId(e.target.value)}>
                  <option value="">Не назначена</option>
                  {data.kitchenStations.map((station) => (
                    <option value={station.id} key={station.id}>{station.name}{station.isActive ? '' : ' · отключена'}</option>
                  ))}
                </select>
              </label>
              <label>
                <span>SKU / Артикул</span>
                <input value={sku} onChange={(e) => setSku(e.target.value)} maxLength={100} placeholder="Необязательно" />
              </label>
              <label>
                <span>Цена, {data.currencyCode}</span>
                <input type="number" min={0} max={1000000} step="0.01" value={price} onChange={(e) => setPrice(Number(e.target.value))} />
              </label>
            </>
          )}

          <label className={isCategory ? '' : 'full-field'}>
            <span>Порядок</span>
            <input type="number" min={0} value={sortOrder} onChange={(e) => setSortOrder(Number(e.target.value))} />
          </label>
        </div>

        {editing && (
          <label className="toggle-row">
            <span>
              <strong>Активно</strong>
              <small>{isCategory ? 'Показывать категорию на кассе' : 'Блюдо доступно для продажи'}</small>
            </span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {product && product.priceHistory.length > 0 && (
          <details className="price-history">
            <summary>История цен ({product.priceHistory.length})</summary>
            <div>
              {product.priceHistory.map((item) => (
                <div className="price-history-row" key={item.id}>
                  <strong>{formatMoney(item.amount)} {item.currencyCode}</strong>
                  <span>{new Date(item.validFrom).toLocaleString('ru-RU')}</span>
                  <small>{item.validTo ? `до ${new Date(item.validTo).toLocaleString('ru-RU')}` : 'текущая'}</small>
                </div>
              ))}
            </div>
          </details>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim() || (!isCategory && !categoryId)}>
            {saving ? 'Сохраняем…' : editing ? 'Сохранить' : 'Создать'}
          </button>
        </div>
      </form>
    </div>
  );
}

function formatMoney(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}
