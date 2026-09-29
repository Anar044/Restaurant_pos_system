import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeInventory,
  type InventoryNomenclatureItem,
  type InventoryWarehouse,
  createWarehouse,
  getBackOfficeInventory,
  updateWarehouse,
} from './api';
import './inventory.css';

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
  const [selectedWarehouseId, setSelectedWarehouseId] = useState('ALL');
  const [query, setQuery] = useState('');
  const [editor, setEditor] = useState<InventoryWarehouse | 'new' | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const next = await getBackOfficeInventory(token);
      setData(next);
      if (selectedWarehouseId !== 'ALL' && !next.warehouses.some((x) => x.id === selectedWarehouseId)) {
        setSelectedWarehouseId('ALL');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить остатки');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const visibleItems = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase();
    return (data?.items ?? []).filter((item) =>
      !normalized ||
      item.name.toLocaleLowerCase().includes(normalized) ||
      (item.sku ?? '').toLocaleLowerCase().includes(normalized));
  }, [data, query]);

  const stats = useMemo(() => {
    const items = data?.items ?? [];
    return {
      warehouses: data?.warehouses.filter((x) => x.isActive).length ?? 0,
      positions: items.filter((x) => x.isActive).length,
      lowStock: items.filter((item) => {
        const stock = stockFor(item, selectedWarehouseId);
        return item.isActive && item.minStock > 0 && stock <= item.minStock;
      }).length,
      stockValue: items.reduce((sum, item) => sum + stockValueFor(item, selectedWarehouseId), 0),
    };
  }, [data, selectedWarehouseId]);

  if (!data && loading) return <div className="empty-state">Загружаем склады и остатки…</div>;

  return (
    <section>
      <div className="page-heading inventory-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Склады и остатки</h1>
          <p>Только справочник складов и текущее состояние запасов. Операции вынесены в отдельные разделы.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditor('new')}>+ Склад</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span><button onClick={() => void refresh()}>Повторить</button></div>}

      <div className="stats-grid inventory-stats">
        <InventoryStat label="Активные склады" value={String(stats.warehouses)} />
        <InventoryStat label="Позиции с учётом" value={String(stats.positions)} />
        <InventoryStat label="Ниже минимума" value={String(stats.lowStock)} warning={stats.lowStock > 0} />
        <InventoryStat label="Стоимость остатка" value={formatMoney(stats.stockValue)} />
      </div>

      <div className="inventory-warehouse-bar">
        <div className="inventory-warehouse-tabs">
          <button className={selectedWarehouseId === 'ALL' ? 'active' : ''} onClick={() => setSelectedWarehouseId('ALL')}>Все склады</button>
          {(data?.warehouses ?? []).map((warehouse) => (
            <button key={warehouse.id} className={selectedWarehouseId === warehouse.id ? 'active' : ''} onClick={() => setSelectedWarehouseId(warehouse.id)}>
              {warehouse.name}{!warehouse.isActive && <small>выкл.</small>}
            </button>
          ))}
        </div>
        {canManage && selectedWarehouseId !== 'ALL' && (
          <button className="text-button" onClick={() => {
            const warehouse = data?.warehouses.find((x) => x.id === selectedWarehouseId);
            if (warehouse) setEditor(warehouse);
          }}>Настроить склад</button>
        )}
      </div>

      <div className="inventory-panel">
        <div className="inventory-toolbar">
          <div>
            <strong>Остатки</strong>
            <small>{selectedWarehouseId === 'ALL' ? 'Суммарно по всем складам' : data?.warehouses.find((x) => x.id === selectedWarehouseId)?.name}</small>
          </div>
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Поиск по названию или SKU" />
        </div>

        {visibleItems.length === 0 ? (
          <div className="inventory-empty"><strong>Нет позиций со складским учётом</strong><span>Включите складской учёт в карточке номенклатуры.</span></div>
        ) : (
          <div className="inventory-table-wrap">
            <table className="inventory-table">
              <thead><tr><th>Позиция</th><th>Остаток</th><th>Ср. себестоимость</th><th>Стоимость</th><th>Минимум</th><th>Статус</th></tr></thead>
              <tbody>
                {visibleItems.map((item) => {
                  const current = stockFor(item, selectedWarehouseId);
                  const averageCost = averageCostFor(item, selectedWarehouseId);
                  const stockValue = stockValueFor(item, selectedWarehouseId);
                  const low = item.isActive && item.minStock > 0 && current <= item.minStock;
                  return (
                    <tr key={item.id} className={!item.isActive ? 'row-inactive' : ''}>
                      <td><div className="inventory-item-name"><strong>{item.name}</strong><span>{item.sku ? 'SKU ' + item.sku + ' · ' : ''}{unitLabel(item.unit)}</span></div></td>
                      <td><strong className={low ? 'inventory-low-value' : ''}>{formatQuantity(current)} {unitLabel(item.unit)}</strong></td>
                      <td>{formatMoney(averageCost)}</td>
                      <td><strong>{formatMoney(stockValue)}</strong></td>
                      <td>{formatQuantity(item.minStock)} {unitLabel(item.unit)}</td>
                      <td><span className={'badge ' + (low ? 'inventory-warning-badge' : item.isActive ? 'success' : 'neutral')}>{!item.isActive ? 'Отключено' : low ? 'Мало' : 'В норме'}</span></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {editor && canManage && (
        <WarehouseEditor
          warehouse={editor === 'new' ? null : editor}
          token={token}
          onClose={() => setEditor(null)}
          onSaved={async () => { setEditor(null); await refresh(); }}
        />
      )}
    </section>
  );
}

function InventoryStat({ label, value, warning = false }: { label: string; value: string; warning?: boolean }) {
  return <div className={'stat-card' + (warning ? ' inventory-stat-warning' : '')}><span>{label}</span><div><strong>{value}</strong></div></div>;
}

function WarehouseEditor({
  warehouse,
  token,
  onClose,
  onSaved,
}: {
  warehouse: InventoryWarehouse | null;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [name, setName] = useState(warehouse?.name ?? '');
  const [isActive, setIsActive] = useState(warehouse?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;
    setSaving(true); setError(null);
    try {
      if (warehouse) {
        await updateWarehouse(token, warehouse.id, { name: name.trim(), isActive });
      } else {
        await createWarehouse(token, { name: name.trim() });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить склад');
    } finally { setSaving(false); }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header"><div><div className="eyebrow">СКЛАД</div><h2>{warehouse ? 'Настройки склада' : 'Новый склад'}</h2></div><button type="button" className="close-button" onClick={onClose}>×</button></div>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} maxLength={120} autoFocus /></label>
        {warehouse && <label className="toggle-row"><span><strong>Активен</strong></span><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /></label>}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button></div>
      </form>
    </div>
  );
}

function stockFor(item: InventoryNomenclatureItem, warehouseId: string) {
  if (warehouseId === 'ALL') return item.totalStock;
  return item.warehouseBalances.find((x) => x.warehouseId === warehouseId)?.quantity ?? 0;
}
function stockValueFor(item: InventoryNomenclatureItem, warehouseId: string) {
  if (warehouseId === 'ALL') return item.totalStockValue;
  return item.warehouseBalances.find((x) => x.warehouseId === warehouseId)?.stockValue ?? 0;
}
function averageCostFor(item: InventoryNomenclatureItem, warehouseId: string) {
  if (warehouseId === 'ALL') return item.averageCost;
  return item.warehouseBalances.find((x) => x.warehouseId === warehouseId)?.averageCost ?? 0;
}
function unitLabel(unit: string) { return UNIT_OPTIONS.find(([value]) => value === unit)?.[1] ?? unit; }
function formatMoney(value: number) { return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼'; }
function formatQuantity(value: number) { return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value); }
