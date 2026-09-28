import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeKitchen,
  type BackOfficeKitchenStation,
  createKitchenStation,
  getBackOfficeKitchen,
  updateKitchenStation,
} from './api';
import './kitchen.css';

type EditorState =
  | { kind: 'create' }
  | { kind: 'edit'; station: BackOfficeKitchenStation }
  | null;

export function KitchenPage({ token }: { token: string }) {
  const [data, setData] = useState<BackOfficeKitchen | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeKitchen(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить кухню');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const stats = useMemo(() => {
    const stations = data?.stations ?? [];
    return {
      activeStations: stations.filter((x) => x.isActive).length,
      totalStations: stations.length,
      routedProducts: stations.reduce((sum, station) => sum + station.activeProductCount, 0),
      unassignedProducts: data?.unassignedProducts.length ?? 0,
    };
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем настройки кухни…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НАСТРОЙКИ РЕСТОРАНА</div>
          <h1>Тип места приготовления</h1>
          <p>Настройте маршрутизацию: куда отправлять позицию, на каком принтере печатать и с какого склада списывать ингредиенты.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'create' })}>+ Добавить тип</button>
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="stats-grid">
        <KitchenStat label="Активные типы" value={stats.activeStations} detail={`${stats.totalStations} всего`} />
        <KitchenStat label="Назначено блюд" value={stats.routedProducts} detail="активных позиций" />
        <KitchenStat
          label="Без типа"
          value={stats.unassignedProducts}
          detail={stats.unassignedProducts === 0 ? 'всё распределено' : 'требуют настройки'}
          warning={stats.unassignedProducts > 0}
        />
      </div>

      {stats.unassignedProducts > 0 && (
        <div className="kitchen-warning-panel">
          <div className="kitchen-warning-icon">!</div>
          <div>
            <strong>Есть позиции без типа места приготовления</strong>
            <p>Назначьте тип места приготовления во вкладке «Продажа» карточки номенклатуры.</p>
            <div className="unassigned-chips">
              {data?.unassignedProducts.slice(0, 8).map((product) => (
                <span key={product.id}>{product.name}</span>
              ))}
              {(data?.unassignedProducts.length ?? 0) > 8 && (
                <span>+{(data?.unassignedProducts.length ?? 0) - 8}</span>
              )}
            </div>
          </div>
        </div>
      )}

      {(data?.stations.length ?? 0) === 0 ? (
        <div className="empty-state">
          <strong>Типов места приготовления пока нет</strong>
          <span>Создайте первый тип, например «Горячий цех», «Бар» или «Холодный цех».</span>
          <button className="primary-button" onClick={() => setEditor({ kind: 'create' })}>Создать тип</button>
        </div>
      ) : (
        <div className="kitchen-station-grid">
          {data?.stations.map((station) => (
            <article className={`kitchen-station-card ${!station.isActive ? 'inactive-card' : ''}`} key={station.id}>
              <div className="kitchen-station-header">
                <div className="station-title-block">
                  <div className="station-icon">K</div>
                  <div>
                    <div className="title-row">
                      <h2>{station.name}</h2>
                      <span className={`badge ${station.isActive ? 'success' : 'neutral'}`}>
                        {station.isActive ? 'Активна' : 'Отключена'}
                      </span>
                    </div>
                    <p>{station.activeProductCount} активных · {station.totalProductCount} всего</p>
                    <div className="station-printer-line">
                      Принтер: <strong>{station.printerName ?? 'не назначен'}</strong>
                    </div>
                    <div className="station-printer-line">
                      Склад списания: <strong>{station.warehouseName ?? 'не назначен'}</strong>
                    </div>
                  </div>
                </div>
                <button className="text-button" onClick={() => setEditor({ kind: 'edit', station })}>Настроить</button>
              </div>

              <div className="station-products">
                {station.products.length === 0 ? (
                  <div className="station-empty-products">
                    <strong>Позиции не назначены</strong>
                    <span>Выберите этот тип во вкладке «Продажа» номенклатуры.</span>
                  </div>
                ) : (
                  <>
                    {station.products.slice(0, 10).map((product) => (
                      <div className={`station-product-row ${!product.isActive ? 'inactive-product' : ''}`} key={product.id}>
                        <div className="station-product-avatar">{product.name.slice(0, 1).toUpperCase()}</div>
                        <div className="station-product-meta">
                          <strong>{product.name}</strong>
                          <span>{product.categoryName ?? 'Без категории'}{product.sku ? ` · ${product.sku}` : ''}</span>
                        </div>
                        <span className={`mini-dot ${product.isActive ? '' : 'off'}`} />
                      </div>
                    ))}
                    {station.products.length > 10 && (
                      <div className="station-more">Ещё {station.products.length - 10} позиций</div>
                    )}
                  </>
                )}
              </div>
            </article>
          ))}
        </div>
      )}

      {editor && (
        <KitchenStationEditor
          editor={editor}
          token={token}
          availablePrinters={data?.availablePrinters ?? []}
          availableWarehouses={data?.availableWarehouses ?? []}
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

function KitchenStat({
  label,
  value,
  detail,
  warning = false,
}: {
  label: string;
  value: number;
  detail: string;
  warning?: boolean;
}) {
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

function KitchenStationEditor({
  editor,
  token,
  availablePrinters,
  availableWarehouses,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  token: string;
  availablePrinters: BackOfficeKitchen['availablePrinters'];
  availableWarehouses: BackOfficeKitchen['availableWarehouses'];
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const station = editor.kind === 'edit' ? editor.station : null;
  const [name, setName] = useState(station?.name ?? '');
  const [printerId, setPrinterId] = useState(station?.printerId ?? '');
  const [warehouseId, setWarehouseId] = useState(station?.warehouseId ?? '');
  const [isActive, setIsActive] = useState(station?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;

    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'create') {
        await createKitchenStation(token, {
          name: name.trim(),
          printerId: printerId || null,
          warehouseId: warehouseId || null,
        });
      } else {
        await updateKitchenStation(token, editor.station.id, {
          name: name.trim(),
          printerId: printerId || null,
          warehouseId: warehouseId || null,
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить станцию');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">МАРШРУТИЗАЦИЯ ПРИГОТОВЛЕНИЯ</div>
            <h2>{editor.kind === 'create' ? 'Новый тип места приготовления' : 'Настройки типа'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Название</span>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            maxLength={100}
            placeholder="Например: Горячий цех"
            autoFocus
          />
        </label>

        <label>
          <span>Склад списания</span>
          <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
            <option value="">Не назначен</option>
            {availableWarehouses.map((warehouse) => (
              <option key={warehouse.id} value={warehouse.id}>
                {warehouse.name}
              </option>
            ))}
          </select>
          <small className="field-hint">
            Ингредиенты техкарты будут списываться с этого склада при продаже позиции.
          </small>
        </label>

        <label>
          <span>Кухонный принтер</span>
          <select value={printerId} onChange={(e) => setPrinterId(e.target.value)}>
            <option value="">Не назначен</option>
            {availablePrinters.map((printer) => (
              <option key={printer.id} value={printer.id}>
                {printer.name} · {printer.hostDeviceName} · {printer.address}
              </option>
            ))}
          </select>
          <small className="field-hint">
            Печать выполняет POS Agent кассы, к которой привязан этот принтер.
          </small>
        </label>

        {availablePrinters.length === 0 && (
          <div className="kitchen-inline-warning">
            Нет доступных принтеров. Сначала настройте принтер в разделе «Оборудование → POS принтеры».
          </div>
        )}

        {editor.kind === 'edit' && (
          <label className="toggle-row">
            <span>
              <strong>Тип активен</strong>
              <small>
                {station && station.activeProductCount > 0
                  ? `Назначено активных блюд: ${station.activeProductCount}`
                  : 'Отключённый тип не используется для новых отправок'}
              </small>
            </span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {editor.kind === 'edit' && station && station.activeProductCount > 0 && !isActive && (
          <div className="kitchen-inline-warning">
            Сначала назначьте активным позициям другой тип места приготовления или отключите продажу.
          </div>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>
            {saving ? 'Сохраняем…' : editor.kind === 'create' ? 'Создать' : 'Сохранить'}
          </button>
        </div>
      </form>
    </div>
  );
}
