import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeInventory,
  type InventoryStockItem,
  type InventoryWarehouse,
  createStockMovement,
  createWarehouse,
  getBackOfficeInventory,
  updateWarehouse,
} from './api';
import './inventory.css';

type EditorState =
  | { kind: 'warehouse-create' }
  | { kind: 'warehouse-edit'; warehouse: InventoryWarehouse }
  | { kind: 'movement'; item?: InventoryStockItem }
  | null;

const UNIT_OPTIONS = [
  ['pcs', 'шт'],
  ['kg', 'кг'],
  ['g', 'г'],
  ['l', 'л'],
  ['ml', 'мл'],
] as const;

export function InventoryPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeInventory | null>(null);
  const [selectedWarehouseId, setSelectedWarehouseId] = useState<string>('ALL');
  const [query, setQuery] = useState('');
  const [editor, setEditor] = useState<EditorState>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const next = await getBackOfficeInventory(token);
      setData(next);
      if (
        selectedWarehouseId !== 'ALL' &&
        !next.warehouses.some((warehouse) => warehouse.id === selectedWarehouseId)
      ) {
        setSelectedWarehouseId('ALL');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить склад');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const activeWarehouses = data?.warehouses.filter((x) => x.isActive) ?? [];

  const visibleItems = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase();
    return (data?.items ?? []).filter((item) => {
      if (
        normalized &&
        !item.name.toLocaleLowerCase().includes(normalized) &&
        !(item.sku ?? '').toLocaleLowerCase().includes(normalized)
      ) {
        return false;
      }
      return true;
    });
  }, [data, query]);

  const stats = useMemo(() => {
    const items = data?.items ?? [];
    return {
      warehouses: activeWarehouses.length,
      activeItems: items.filter((x) => x.isActive).length,
      lowStock: items.filter((item) => {
        const stock = stockFor(item, selectedWarehouseId);
        return item.isActive && item.minStock > 0 && stock <= item.minStock;
      }).length,
    };
  }, [data, activeWarehouses.length, selectedWarehouseId]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем склад…</div>;
  }

  return (
    <section>
      <div className="page-heading inventory-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Склад</h1>
          <p>
            Остатки и движения формируются по единому справочнику номенклатуры.
            Создание и настройка позиций выполняются в разделе «Номенклатура».
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>
            Обновить
          </button>
          {canManage && (
            <>
              <button className="secondary-button" onClick={() => setEditor({ kind: 'warehouse-create' })}>
                + Склад
              </button>
              <button className="primary-button" onClick={() => setEditor({ kind: 'movement' })}>
                + Операция
              </button>
            </>
          )}
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="stats-grid inventory-stats">
        <InventoryStat label="Активные склады" value={stats.warehouses} />
        <InventoryStat label="Позиции с учётом" value={stats.activeItems} />
        <InventoryStat label="Ниже минимума" value={stats.lowStock} warning={stats.lowStock > 0} />
      </div>

      <div className="inventory-warehouse-bar">
        <div className="inventory-warehouse-tabs">
          <button
            className={selectedWarehouseId === 'ALL' ? 'active' : ''}
            onClick={() => setSelectedWarehouseId('ALL')}
          >
            Все склады
          </button>
          {data?.warehouses.map((warehouse) => (
            <button
              key={warehouse.id}
              className={selectedWarehouseId === warehouse.id ? 'active' : ''}
              onClick={() => setSelectedWarehouseId(warehouse.id)}
            >
              {warehouse.name}
              {!warehouse.isActive && <small>выкл.</small>}
            </button>
          ))}
        </div>
        {canManage && selectedWarehouseId !== 'ALL' && (
          <button
            className="text-button"
            onClick={() => {
              const warehouse = data?.warehouses.find((x) => x.id === selectedWarehouseId);
              if (warehouse) setEditor({ kind: 'warehouse-edit', warehouse });
            }}
          >
            Настроить склад
          </button>
        )}
      </div>

      <div className="inventory-panel">
        <div className="inventory-toolbar">
          <div>
            <strong>Остатки</strong>
            <small>
              {selectedWarehouseId === 'ALL'
                ? 'Суммарно по всем складам'
                : data?.warehouses.find((x) => x.id === selectedWarehouseId)?.name}
            </small>
          </div>
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Поиск по названию или SKU"
          />
        </div>

        {visibleItems.length === 0 ? (
          <div className="inventory-empty">
            <strong>Нет позиций со складским учётом</strong>
            <span>Откройте «Номенклатура» и включите «Вести складской учёт» у нужных позиций.</span>
          </div>
        ) : (
          <div className="inventory-table-wrap">
            <table className="inventory-table">
              <thead>
                <tr>
                  <th>Позиция</th>
                  <th>Остаток</th>
                  <th>Минимум</th>
                  <th>Статус</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {visibleItems.map((item) => {
                  const current = stockFor(item, selectedWarehouseId);
                  const low = item.isActive && item.minStock > 0 && current <= item.minStock;
                  return (
                    <tr key={item.id} className={!item.isActive ? 'row-inactive' : ''}>
                      <td>
                        <div className="inventory-item-name">
                          <strong>{item.name}</strong>
                          <span>{item.sku ? 'SKU ' + item.sku + ' · ' : ''}{unitLabel(item.unit)}</span>
                        </div>
                      </td>
                      <td>
                        <strong className={low ? 'inventory-low-value' : ''}>
                          {formatQuantity(current)} {unitLabel(item.unit)}
                        </strong>
                      </td>
                      <td>{formatQuantity(item.minStock)} {unitLabel(item.unit)}</td>
                      <td>
                        <span className={'badge ' + (low ? 'inventory-warning-badge' : item.isActive ? 'success' : 'neutral')}>
                          {!item.isActive ? 'Отключено' : low ? 'Мало' : 'В норме'}
                        </span>
                      </td>
                      <td className="inventory-actions">
                        {canManage && (
                          <button className="text-button" onClick={() => setEditor({ kind: 'movement', item })}>
                            Операция
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="inventory-panel inventory-movements-panel">
        <div className="inventory-toolbar">
          <div>
            <strong>Последние движения</strong>
            <small>Последние 100 операций</small>
          </div>
        </div>
        {(data?.recentMovements.length ?? 0) === 0 ? (
          <div className="inventory-empty compact">
            <span>Операций по складу пока нет.</span>
          </div>
        ) : (
          <div className="inventory-movement-list">
            {data!.recentMovements.slice(0, 20).map((movement) => (
              <div className="inventory-movement-row" key={movement.id}>
                <div>
                  <strong>{movement.stockItemName}</strong>
                  <span>{movement.warehouseName} · {formatDateTime(movement.createdAt)}</span>
                </div>
                <div className={movement.quantityDelta >= 0 ? 'inventory-movement-plus' : 'inventory-movement-minus'}>
                  {movement.quantityDelta >= 0 ? '+' : ''}
                  {formatQuantity(movement.quantityDelta)} {unitLabel(movement.unit)}
                </div>
                <span>{movement.note || (movement.type === 'RECEIPT' ? 'Приход' : 'Списание')}</span>
              </div>
            ))}
          </div>
        )}
      </div>

      {editor && data && canManage && (
        <InventoryEditor
          editor={editor}
          data={data}
          token={token}
          selectedWarehouseId={selectedWarehouseId}
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

function InventoryStat({
  label,
  value,
  warning = false,
}: {
  label: string;
  value: number;
  warning?: boolean;
}) {
  return (
    <div className={'stat-card' + (warning ? ' inventory-stat-warning' : '')}>
      <span>{label}</span>
      <div><strong>{value}</strong></div>
    </div>
  );
}

function InventoryEditor({
  editor,
  data,
  token,
  selectedWarehouseId,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  data: BackOfficeInventory;
  token: string;
  selectedWarehouseId: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  if (editor.kind === 'warehouse-create' || editor.kind === 'warehouse-edit') {
    return (
      <WarehouseEditor
        editor={editor}
        token={token}
        onClose={onClose}
        onSaved={onSaved}
      />
    );
  }

  return (
    <MovementEditor
      item={editor.item}
      data={data}
      token={token}
      selectedWarehouseId={selectedWarehouseId}
      onClose={onClose}
      onSaved={onSaved}
    />
  );
}

function WarehouseEditor({
  editor,
  token,
  onClose,
  onSaved,
}: {
  editor: { kind: 'warehouse-create' } | { kind: 'warehouse-edit'; warehouse: InventoryWarehouse };
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const warehouse = editor.kind === 'warehouse-edit' ? editor.warehouse : null;
  const [name, setName] = useState(warehouse?.name ?? '');
  const [isActive, setIsActive] = useState(warehouse?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'warehouse-create') {
        await createWarehouse(token, { name: name.trim() });
      } else {
        await updateWarehouse(token, editor.warehouse.id, {
          name: name.trim(),
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить склад');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">СКЛАД</div>
            <h2>{editor.kind === 'warehouse-create' ? 'Новый склад' : 'Настройки склада'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>
        <label>
          <span>Название</span>
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} autoFocus />
        </label>
        {warehouse && (
          <label className="toggle-row">
            <span><strong>Склад активен</strong><small>Отключённый склад нельзя использовать в новых операциях.</small></span>
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

function MovementEditor({
  item,
  data,
  token,
  selectedWarehouseId,
  onClose,
  onSaved,
}: {
  item?: InventoryStockItem;
  data: BackOfficeInventory;
  token: string;
  selectedWarehouseId: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const defaultWarehouse =
    selectedWarehouseId !== 'ALL' &&
    data.warehouses.some((x) => x.id === selectedWarehouseId && x.isActive)
      ? selectedWarehouseId
      : data.warehouses.find((x) => x.isActive)?.id ?? '';

  const [warehouseId, setWarehouseId] = useState(defaultWarehouse);
  const [stockItemId, setStockItemId] = useState(item?.id ?? data.items.find((x) => x.isActive)?.id ?? '');
  const [type, setType] = useState<'RECEIPT' | 'WRITE_OFF'>('RECEIPT');
  const [quantity, setQuantity] = useState('');
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const selectedItem = data.items.find((x) => x.id === stockItemId);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const numericQuantity = Number(quantity.replace(',', '.'));
    if (!warehouseId || !stockItemId || !Number.isFinite(numericQuantity) || numericQuantity <= 0) {
      setError('Выберите склад, позицию и укажите количество больше нуля.');
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await createStockMovement(token, {
        warehouseId,
        stockItemId,
        type,
        quantity: numericQuantity,
        note: note.trim() || null,
      });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести операцию');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">ДВИЖЕНИЕ ТОВАРА</div>
            <h2>{type === 'RECEIPT' ? 'Приход' : 'Списание'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="inventory-operation-switch">
          <button type="button" className={type === 'RECEIPT' ? 'active' : ''} onClick={() => setType('RECEIPT')}>Приход</button>
          <button type="button" className={type === 'WRITE_OFF' ? 'active' : ''} onClick={() => setType('WRITE_OFF')}>Списание</button>
        </div>

        <label>
          <span>Склад</span>
          <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
            <option value="">Выберите склад</option>
            {data.warehouses.filter((x) => x.isActive).map((warehouse) => (
              <option value={warehouse.id} key={warehouse.id}>{warehouse.name}</option>
            ))}
          </select>
        </label>

        <label>
          <span>Позиция номенклатуры</span>
          <select value={stockItemId} onChange={(e) => setStockItemId(e.target.value)}>
            <option value="">Выберите позицию</option>
            {data.items.filter((x) => x.isActive).map((stockItem) => (
              <option value={stockItem.id} key={stockItem.id}>{stockItem.name}</option>
            ))}
          </select>
        </label>

        <label>
          <span>Количество {selectedItem ? '(' + unitLabel(selectedItem.unit) + ')' : ''}</span>
          <input value={quantity} onChange={(e) => setQuantity(e.target.value)} inputMode="decimal" autoFocus />
        </label>

        <label>
          <span>Комментарий</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="Например: поставка №123 или порча" />
        </label>

        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !warehouseId || !stockItemId}>
            {saving ? 'Проводим…' : 'Провести'}
          </button>
        </div>
      </form>
    </div>
  );
}

function stockFor(item: InventoryStockItem, warehouseId: string) {
  if (warehouseId === 'ALL') return item.totalStock;
  return item.warehouseBalances.find((x) => x.warehouseId === warehouseId)?.quantity ?? 0;
}

function unitLabel(unit: string) {
  return UNIT_OPTIONS.find(([value]) => value === unit)?.[1] ?? unit;
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value);
}

function formatDateTime(value: string) {
  return new Date(value).toLocaleString('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}
