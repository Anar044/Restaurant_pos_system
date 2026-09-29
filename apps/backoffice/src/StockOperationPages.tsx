import { FormEvent, type Dispatch, type ReactNode, type SetStateAction, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeInventory,
  type InventoryMovement,
  countInventory,
  getBackOfficeInventory,
  transferStock,
  writeOffStock,
} from './api';
import './stock-operations.css';

type Mode = 'transfer' | 'writeoff' | 'inventory';

export function StockTransfersPage({ token, canManage }: { token: string; canManage: boolean }) {
  return <StockOperationsPage token={token} canManage={canManage} mode="transfer" />;
}

export function StockWriteOffsPage({ token, canManage }: { token: string; canManage: boolean }) {
  return <StockOperationsPage token={token} canManage={canManage} mode="writeoff" />;
}

export function StockInventoryPage({ token, canManage }: { token: string; canManage: boolean }) {
  return <StockOperationsPage token={token} canManage={canManage} mode="inventory" />;
}

function StockOperationsPage({
  token,
  canManage,
  mode,
}: {
  token: string;
  canManage: boolean;
  mode: Mode;
}) {
  const [data, setData] = useState<BackOfficeInventory | null>(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeInventory(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить складские операции');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const operations = useMemo(() => {
    const movements = data?.recentMovements ?? [];
    if (mode === 'transfer') {
      return groupOperations(movements.filter((x) => x.type === 'TRANSFER_IN' || x.type === 'TRANSFER_OUT'));
    }
    if (mode === 'writeoff') {
      return groupOperations(movements.filter((x) => x.type === 'WRITE_OFF'));
    }
    return groupOperations(movements.filter((x) => x.type === 'INVENTORY_GAIN' || x.type === 'INVENTORY_LOSS'));
  }, [data, mode]);

  const title = mode === 'transfer' ? 'Перемещения' : mode === 'writeoff' ? 'Списания' : 'Инвентаризация';
  const description =
    mode === 'transfer'
      ? 'Перемещение товаров между складами с переносом количества и себестоимости.'
      : mode === 'writeoff'
        ? 'Списание товаров со склада с фиксацией причины и себестоимости.'
        : 'Сверка фактического остатка с учётным и автоматическая фиксация излишков/недостач.';
  const actionLabel = mode === 'transfer' ? '+ Перемещение' : mode === 'writeoff' ? '+ Списание' : '+ Инвентаризация';

  if (!data && loading) {
    return <div className="empty-state">Загружаем {title.toLocaleLowerCase()}…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditorOpen(true)}>{actionLabel}</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="stock-op-panel">
        <div className="stock-op-panel-head">
          <div>
            <strong>Последние операции</strong>
            <span>Операции сгруппированы по документу/операции.</span>
          </div>
        </div>
        {operations.length === 0 ? (
          <div className="stock-op-empty">Операций пока нет.</div>
        ) : (
          <div className="stock-op-table-wrap">
            <table className="stock-op-table">
              <thead>
                <tr>
                  <th>Дата</th>
                  {mode === 'transfer' && <><th>Со склада</th><th>На склад</th></>}
                  {mode !== 'transfer' && <th>Склад</th>}
                  <th>Позиций</th>
                  <th>Количество</th>
                  <th>Стоимость</th>
                  <th>Комментарий</th>
                </tr>
              </thead>
              <tbody>
                {operations.map((operation) => (
                  <tr key={operation.operationId}>
                    <td>{dateTime(operation.createdAt)}</td>
                    {mode === 'transfer' ? (
                      <>
                        <td>{operation.fromWarehouse ?? '—'}</td>
                        <td>{operation.toWarehouse ?? '—'}</td>
                      </>
                    ) : (
                      <td>{operation.warehouse ?? '—'}</td>
                    )}
                    <td>{operation.itemCount}</td>
                    <td>{formatQuantity(operation.quantity)}</td>
                    <td>{money(operation.cost)}</td>
                    <td>{operation.note ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {editorOpen && data && canManage && (
        mode === 'transfer' ? (
          <TransferEditor token={token} data={data} onClose={() => setEditorOpen(false)} onSaved={async () => { setEditorOpen(false); await refresh(); }} />
        ) : mode === 'writeoff' ? (
          <WriteOffEditor token={token} data={data} onClose={() => setEditorOpen(false)} onSaved={async () => { setEditorOpen(false); await refresh(); }} />
        ) : (
          <InventoryEditor token={token} data={data} onClose={() => setEditorOpen(false)} onSaved={async () => { setEditorOpen(false); await refresh(); }} />
        )
      )}
    </section>
  );
}

function TransferEditor({
  token,
  data,
  onClose,
  onSaved,
}: {
  token: string;
  data: BackOfficeInventory;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const warehouses = data.warehouses.filter((x) => x.isActive);
  const [fromWarehouseId, setFromWarehouseId] = useState(warehouses[0]?.id ?? '');
  const [toWarehouseId, setToWarehouseId] = useState(warehouses[1]?.id ?? '');
  const [lines, setLines] = useState([{ productId: data.items.find((x) => x.isActive)?.id ?? '', quantity: '' }]);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const payload = parseLines(lines);
    if (!fromWarehouseId || !toWarehouseId || fromWarehouseId === toWarehouseId || !payload) {
      setError('Выберите разные склады и заполните количество во всех строках.');
      return;
    }
    setSaving(true); setError(null);
    try {
      await transferStock(token, { fromWarehouseId, toWarehouseId, lines: payload, note: note.trim() || null });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести перемещение');
    } finally { setSaving(false); }
  }

  return (
    <OperationModal title="Новое перемещение" onClose={onClose}>
      <form onSubmit={submit} className="stock-op-form">
        <div className="form-grid">
          <label><span>Со склада</span><select value={fromWarehouseId} onChange={(e) => setFromWarehouseId(e.target.value)}>{warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label><span>На склад</span><select value={toWarehouseId} onChange={(e) => setToWarehouseId(e.target.value)}>{warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        </div>
        <LinesEditor data={data} warehouseId={fromWarehouseId} lines={lines} setLines={setLines} />
        <label><span>Комментарий</span><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} /></label>
        {error && <div className="error-box">{error}</div>}
        <ModalActions saving={saving} onClose={onClose} label="Провести перемещение" />
      </form>
    </OperationModal>
  );
}

function WriteOffEditor({
  token,
  data,
  onClose,
  onSaved,
}: {
  token: string;
  data: BackOfficeInventory;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const warehouses = data.warehouses.filter((x) => x.isActive);
  const [warehouseId, setWarehouseId] = useState(warehouses[0]?.id ?? '');
  const [lines, setLines] = useState([{ productId: data.items.find((x) => x.isActive)?.id ?? '', quantity: '' }]);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const payload = parseLines(lines);
    if (!warehouseId || !payload || !note.trim()) {
      setError('Выберите склад, заполните позиции и укажите причину списания.');
      return;
    }
    setSaving(true); setError(null);
    try {
      await writeOffStock(token, { warehouseId, lines: payload, note: note.trim() });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести списание');
    } finally { setSaving(false); }
  }

  return (
    <OperationModal title="Новое списание" onClose={onClose}>
      <form onSubmit={submit} className="stock-op-form">
        <label><span>Склад</span><select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>{warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        <LinesEditor data={data} warehouseId={warehouseId} lines={lines} setLines={setLines} />
        <label><span>Причина списания</span><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="Порча, бой, служебное питание…" /></label>
        {error && <div className="error-box">{error}</div>}
        <ModalActions saving={saving} onClose={onClose} label="Провести списание" />
      </form>
    </OperationModal>
  );
}

function InventoryEditor({
  token,
  data,
  onClose,
  onSaved,
}: {
  token: string;
  data: BackOfficeInventory;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const warehouses = data.warehouses.filter((x) => x.isActive);
  const [warehouseId, setWarehouseId] = useState(warehouses[0]?.id ?? '');
  const [values, setValues] = useState<Record<string, string>>({});
  const [query, setQuery] = useState('');
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const visible = data.items.filter((item) =>
    item.isActive &&
    (!query.trim() ||
      item.name.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()) ||
      (item.sku ?? '').toLocaleLowerCase().includes(query.trim().toLocaleLowerCase())));

  async function submit(event: FormEvent) {
    event.preventDefault();
    const lines = Object.entries(values)
      .filter(([, value]) => value.trim() !== '')
      .map(([productId, value]) => ({ productId, countedQuantity: Number(value.replace(',', '.')) }));

    if (!warehouseId || lines.length === 0 || lines.some((x) => !Number.isFinite(x.countedQuantity) || x.countedQuantity < 0)) {
      setError('Введите фактический остаток хотя бы для одной позиции.');
      return;
    }

    setSaving(true); setError(null);
    try {
      await countInventory(token, { warehouseId, lines, note: note.trim() || null });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести инвентаризацию');
    } finally { setSaving(false); }
  }

  return (
    <OperationModal title="Новая инвентаризация" onClose={onClose} wide>
      <form onSubmit={submit} className="stock-op-form">
        <div className="form-grid">
          <label><span>Склад</span><select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>{warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label><span>Поиск</span><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Название или SKU" /></label>
        </div>
        <div className="stock-count-list">
          <div className="stock-count-row header"><span>Позиция</span><span>По системе</span><span>Фактически</span><span>Разница</span></div>
          {visible.map((item) => {
            const current = stockFor(item, warehouseId);
            const raw = values[item.id] ?? '';
            const counted = raw.trim() ? Number(raw.replace(',', '.')) : null;
            const delta = counted !== null && Number.isFinite(counted) ? counted - current : null;
            return (
              <div className="stock-count-row" key={item.id}>
                <div><strong>{item.name}</strong><small>{unitLabel(item.unit)}</small></div>
                <span>{formatQuantity(current)}</span>
                <input value={raw} onChange={(e) => setValues((v) => ({ ...v, [item.id]: e.target.value }))} inputMode="decimal" placeholder="—" />
                <span>{delta === null ? '—' : (delta >= 0 ? '+' : '') + formatQuantity(delta)}</span>
              </div>
            );
          })}
        </div>
        <label><span>Комментарий</span><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} /></label>
        {error && <div className="error-box">{error}</div>}
        <ModalActions saving={saving} onClose={onClose} label="Провести инвентаризацию" />
      </form>
    </OperationModal>
  );
}

function LinesEditor({
  data,
  warehouseId,
  lines,
  setLines,
}: {
  data: BackOfficeInventory;
  warehouseId: string;
  lines: Array<{ productId: string; quantity: string }>;
  setLines: Dispatch<SetStateAction<Array<{ productId: string; quantity: string }>>>;
}) {
  function patch(index: number, value: Partial<{ productId: string; quantity: string }>) {
    setLines((current) => current.map((line, i) => i === index ? { ...line, ...value } : line));
  }
  return (
    <div className="stock-lines">
      <div className="stock-lines-head"><strong>Позиции</strong><button type="button" className="secondary-button compact" onClick={() => setLines((x) => [...x, { productId: '', quantity: '' }])}>+ Строка</button></div>
      {lines.map((line, index) => {
        const item = data.items.find((x) => x.id === line.productId);
        return (
          <div className="stock-line" key={index}>
            <select value={line.productId} onChange={(e) => patch(index, { productId: e.target.value })}>
              <option value="">Выберите позицию</option>
              {data.items.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
            <input value={line.quantity} onChange={(e) => patch(index, { quantity: e.target.value })} inputMode="decimal" placeholder="Количество" />
            <span>{item ? 'Остаток: ' + formatQuantity(stockFor(item, warehouseId)) + ' ' + unitLabel(item.unit) : ''}</span>
            <button type="button" className="text-button" onClick={() => setLines((x) => x.filter((_, i) => i !== index))} disabled={lines.length === 1}>Удалить</button>
          </div>
        );
      })}
    </div>
  );
}

function OperationModal({ title, onClose, wide = false, children }: { title: string; onClose: () => void; wide?: boolean; children: ReactNode }) {
  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className={'modal-card ' + (wide ? 'stock-op-wide' : 'stock-op-modal')}>
        <div className="modal-header"><div><div className="eyebrow">СКЛАДСКАЯ ОПЕРАЦИЯ</div><h2>{title}</h2></div><button type="button" className="close-button" onClick={onClose}>×</button></div>
        {children}
      </div>
    </div>
  );
}

function ModalActions({ saving, onClose, label }: { saving: boolean; onClose: () => void; label: string }) {
  return <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving}>{saving ? 'Проводим…' : label}</button></div>;
}

function groupOperations(movements: InventoryMovement[]) {
  const map = new Map<string, InventoryMovement[]>();
  for (const movement of movements) {
    const key = movement.operationId || movement.id;
    map.set(key, [...(map.get(key) ?? []), movement]);
  }
  return [...map.entries()].map(([operationId, rows]) => {
    const out = rows.find((x) => x.type === 'TRANSFER_OUT');
    const input = rows.find((x) => x.type === 'TRANSFER_IN');
    return {
      operationId,
      createdAt: rows[0].createdAt,
      fromWarehouse: out?.warehouseName ?? null,
      toWarehouse: input?.warehouseName ?? null,
      warehouse: rows[0].warehouseName,
      itemCount: new Set(rows.map((x) => x.productId)).size,
      quantity: rows.filter((x) => x.quantityDelta < 0 || !rows.some((y) => y.type === 'TRANSFER_OUT')).reduce((sum, x) => sum + Math.abs(x.quantityDelta), 0),
      cost: rows.filter((x) => x.costDelta !== null && (x.costDelta! < 0 || !rows.some((y) => y.type === 'TRANSFER_OUT'))).reduce((sum, x) => sum + Math.abs(x.costDelta ?? 0), 0),
      note: rows.find((x) => x.note)?.note ?? null,
    };
  }).sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
}

function parseLines(lines: Array<{ productId: string; quantity: string }>) {
  const result = lines.filter((x) => x.productId).map((x) => ({ productId: x.productId, quantity: Number(x.quantity.replace(',', '.')) }));
  if (!result.length || result.some((x) => !Number.isFinite(x.quantity) || x.quantity <= 0)) return null;
  return result;
}

function stockFor(item: BackOfficeInventory['items'][number], warehouseId: string) {
  return item.warehouseBalances.find((x) => x.warehouseId === warehouseId)?.quantity ?? 0;
}
function unitLabel(unit: string) { return ({ pcs: 'шт', kg: 'кг', g: 'г', l: 'л', ml: 'мл' } as Record<string, string>)[unit] ?? unit; }
function formatQuantity(value: number) { return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 3 }).format(value); }
function money(value: number) { return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼'; }
function dateTime(value: string) { return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)); }
