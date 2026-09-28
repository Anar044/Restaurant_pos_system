import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeRouting,
  type PreparationPlaceOption,
  type PreparationPlaceTypeOption,
  type PreparationRouteOption,
  type SalesPointOption,
  createPreparationPlace,
  createPreparationPlaceType,
  createPreparationRoute,
  createSalesPoint,
  getBackOfficeRouting,
  updatePreparationPlace,
  updatePreparationPlaceType,
  updatePreparationRoute,
  updateSalesPoint,
} from './api';
import './routing.css';

export type RoutingPageMode = 'types' | 'places' | 'sales' | 'routes';

type Editor =
  | { kind: 'type'; item?: PreparationPlaceTypeOption }
  | { kind: 'place'; item?: PreparationPlaceOption }
  | { kind: 'sales'; item?: SalesPointOption }
  | { kind: 'route'; item?: PreparationRouteOption }
  | null;

export function RoutingPage({
  token,
  mode,
}: {
  token: string;
  mode: RoutingPageMode;
}) {
  const [data, setData] = useState<BackOfficeRouting | null>(null);
  const [editor, setEditor] = useState<Editor>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeRouting(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить маршрутизацию');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const meta = {
    types: {
      eyebrow: 'НАСТРОЙКИ РЕСТОРАНА',
      title: 'Типы мест приготовления',
      description: 'Логические типы: горячая кухня, бар, суши, холодный цех и другие.',
      action: '+ Тип',
    },
    places: {
      eyebrow: 'НАСТРОЙКИ РЕСТОРАНА',
      title: 'Места приготовления',
      description: 'Физические станции ресторана. Здесь задаются принтер/KDS и склад списания.',
      action: '+ Место',
    },
    sales: {
      eyebrow: 'НАСТРОЙКИ РЕСТОРАНА',
      title: 'Места продаж',
      description: 'Зал, доставка, самовывоз, киоск и другие точки, откуда приходит заказ.',
      action: '+ Место продажи',
    },
    routes: {
      eyebrow: 'НАСТРОЙКИ РЕСТОРАНА',
      title: 'Маршрутизация',
      description: 'Определяет, куда отправлять позицию для каждой пары «место продажи + тип приготовления».',
      action: '+ Маршрут',
    },
  }[mode];

  if (!data && loading) return <div className="empty-state">Загружаем настройки…</div>;

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">{meta.eyebrow}</div>
          <h1>{meta.title}</h1>
          <p>{meta.description}</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => setEditor({ kind: mode === 'types' ? 'type' : mode === 'places' ? 'place' : mode === 'sales' ? 'sales' : 'route' })}>{meta.action}</button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span><button onClick={() => void refresh()}>Повторить</button></div>}

      {mode === 'types' && <TypesView data={data!} onEdit={(item) => setEditor({ kind: 'type', item })} />}
      {mode === 'places' && <PlacesView data={data!} onEdit={(item) => setEditor({ kind: 'place', item })} />}
      {mode === 'sales' && <SalesView data={data!} onEdit={(item) => setEditor({ kind: 'sales', item })} />}
      {mode === 'routes' && <RoutesView data={data!} onEdit={(item) => setEditor({ kind: 'route', item })} />}

      {editor && (
        <RoutingEditor
          token={token}
          data={data!}
          editor={editor}
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

function TypesView({ data, onEdit }: { data: BackOfficeRouting; onEdit: (x: PreparationPlaceTypeOption) => void }) {
  return (
    <div className="routing-grid">
      {data.types.map((item) => (
        <button className="routing-card" key={item.id} onClick={() => onEdit(item)}>
          <span className={'badge ' + (item.isActive ? 'success' : 'neutral')}>{item.isActive ? 'Активен' : 'Отключён'}</span>
          <strong>{item.name}</strong>
          <small>{data.places.filter((x) => x.preparationPlaceTypeId === item.id).length} мест приготовления</small>
        </button>
      ))}
      {data.types.length === 0 && <RoutingEmpty text="Типов мест приготовления пока нет." />}
    </div>
  );
}

function PlacesView({ data, onEdit }: { data: BackOfficeRouting; onEdit: (x: PreparationPlaceOption) => void }) {
  return (
    <div className="routing-list">
      {data.places.map((item) => (
        <button className="routing-row" key={item.id} onClick={() => onEdit(item)}>
          <div>
            <strong>{item.name}</strong>
            <span>{item.preparationPlaceTypeName ?? 'Без типа'}</span>
          </div>
          <div><small>Принтер / KDS</small><strong>{item.printerName ?? 'Не назначен'}</strong></div>
          <div><small>Склад списания</small><strong>{item.warehouseName ?? 'Не назначен'}</strong></div>
          <span className={'badge ' + (item.isActive ? 'success' : 'neutral')}>{item.isActive ? 'Активно' : 'Отключено'}</span>
        </button>
      ))}
      {data.places.length === 0 && <RoutingEmpty text="Мест приготовления пока нет." />}
    </div>
  );
}

function SalesView({ data, onEdit }: { data: BackOfficeRouting; onEdit: (x: SalesPointOption) => void }) {
  const labels: Record<SalesPointOption['type'], string> = {
    HALL: 'Зал', DELIVERY: 'Доставка', PICKUP: 'Самовывоз', KIOSK: 'Киоск', OTHER: 'Другое',
  };
  return (
    <div className="routing-grid">
      {data.salesPoints.map((item) => (
        <button className="routing-card" key={item.id} onClick={() => onEdit(item)}>
          <span className="badge neutral">{labels[item.type]}</span>
          <strong>{item.name}</strong>
          <small>{item.hallName ? 'Зал: ' + item.hallName : 'Без привязки к залу'}</small>
        </button>
      ))}
      {data.salesPoints.length === 0 && <RoutingEmpty text="Мест продаж пока нет." />}
    </div>
  );
}

function RoutesView({ data, onEdit }: { data: BackOfficeRouting; onEdit: (x: PreparationRouteOption) => void }) {
  return (
    <div className="routing-list">
      {data.routes.map((item) => {
        const place = data.places.find((x) => x.id === item.preparationPlaceId);
        return (
          <button className="routing-row route" key={item.id} onClick={() => onEdit(item)}>
            <div><small>Место продажи</small><strong>{item.salesPointName}</strong></div>
            <div className="routing-arrow">→</div>
            <div><small>Тип приготовления</small><strong>{item.preparationPlaceTypeName}</strong></div>
            <div className="routing-arrow">→</div>
            <div><small>Где готовить</small><strong>{item.preparationPlaceName}</strong></div>
            <div><small>Склад</small><strong>{place?.warehouseName ?? 'Не назначен'}</strong></div>
          </button>
        );
      })}
      {data.routes.length === 0 && <RoutingEmpty text="Маршруты ещё не настроены." />}
    </div>
  );
}

function RoutingEmpty({ text }: { text: string }) {
  return <div className="empty-state"><strong>{text}</strong></div>;
}

function RoutingEditor({
  token,
  data,
  editor,
  onClose,
  onSaved,
}: {
  token: string;
  data: BackOfficeRouting;
  editor: Exclude<Editor, null>;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const item: any = editor.item;
  const [name, setName] = useState(item?.name ?? '');
  const [isActive, setIsActive] = useState(item?.isActive ?? true);
  const [typeId, setTypeId] = useState(item?.preparationPlaceTypeId ?? data.types.find((x) => x.isActive)?.id ?? '');
  const [printerId, setPrinterId] = useState(item?.printerId ?? '');
  const [warehouseId, setWarehouseId] = useState(item?.warehouseId ?? '');
  const [salesType, setSalesType] = useState<SalesPointOption['type']>(item?.type ?? 'HALL');
  const [hallId, setHallId] = useState(item?.hallId ?? '');
  const [salesPointId, setSalesPointId] = useState(item?.salesPointId ?? data.salesPoints.find((x) => x.isActive)?.id ?? '');
  const [placeId, setPlaceId] = useState(item?.preparationPlaceId ?? '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const availablePlaces = useMemo(
    () => data.places.filter((x) => x.isActive && x.preparationPlaceTypeId === typeId),
    [data.places, typeId],
  );

  useEffect(() => {
    if (editor.kind === 'route' && !availablePlaces.some((x) => x.id === placeId)) {
      setPlaceId(availablePlaces[0]?.id ?? '');
    }
  }, [typeId]);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'type') {
        if (editor.item) await updatePreparationPlaceType(token, editor.item.id, { name: name.trim(), isActive });
        else await createPreparationPlaceType(token, name.trim());
      } else if (editor.kind === 'place') {
        const input = { name: name.trim(), preparationPlaceTypeId: typeId, printerId: printerId || null, warehouseId: warehouseId || null, isActive };
        if (editor.item) await updatePreparationPlace(token, editor.item.id, input);
        else await createPreparationPlace(token, input);
      } else if (editor.kind === 'sales') {
        const input = { name: name.trim(), type: salesType, hallId: salesType === 'HALL' ? hallId || null : null, isActive };
        if (editor.item) await updateSalesPoint(token, editor.item.id, input);
        else await createSalesPoint(token, input);
      } else {
        const input = { salesPointId, preparationPlaceTypeId: typeId, preparationPlaceId: placeId, isActive };
        if (editor.item) await updatePreparationRoute(token, editor.item.id, input);
        else await createPreparationRoute(token, input);
      }
      await onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Не удалось сохранить');
    } finally {
      setSaving(false);
    }
  }

  const titles = {
    type: editor.item ? 'Настройки типа' : 'Новый тип места приготовления',
    place: editor.item ? 'Настройки места' : 'Новое место приготовления',
    sales: editor.item ? 'Настройки места продажи' : 'Новое место продажи',
    route: editor.item ? 'Настройки маршрута' : 'Новый маршрут',
  };

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div><div className="eyebrow">МАРШРУТИЗАЦИЯ</div><h2>{titles[editor.kind]}</h2></div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        {editor.kind !== 'route' && (
          <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>
        )}

        {editor.kind === 'place' && (
          <>
            <label><span>Тип места приготовления</span><select value={typeId} onChange={(e) => setTypeId(e.target.value)}>{data.types.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <label><span>Принтер / KDS</span><select value={printerId} onChange={(e) => setPrinterId(e.target.value)}><option value="">Не назначен</option>{data.printers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <label><span>Склад списания</span><select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}><option value="">Не назначен</option>{data.warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          </>
        )}

        {editor.kind === 'sales' && (
          <>
            <label><span>Тип</span><select value={salesType} onChange={(e) => setSalesType(e.target.value as SalesPointOption['type'])}>
              <option value="HALL">Зал</option><option value="DELIVERY">Доставка</option><option value="PICKUP">Самовывоз</option><option value="KIOSK">Киоск</option><option value="OTHER">Другое</option>
            </select></label>
            {salesType === 'HALL' && <label><span>Связанный зал</span><select value={hallId} onChange={(e) => setHallId(e.target.value)}><option value="">Не выбран</option>{data.halls.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>}
          </>
        )}

        {editor.kind === 'route' && (
          <>
            <label><span>Место продажи</span><select value={salesPointId} onChange={(e) => setSalesPointId(e.target.value)}>{data.salesPoints.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <label><span>Тип места приготовления</span><select value={typeId} onChange={(e) => setTypeId(e.target.value)}>{data.types.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <label><span>Место приготовления</span><select value={placeId} onChange={(e) => setPlaceId(e.target.value)}>{availablePlaces.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          </>
        )}

        {editor.item && <label className="toggle-row"><span><strong>Активно</strong></span><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /></label>}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving}>{saving ? 'Сохраняем…' : 'Сохранить'}</button></div>
      </form>
    </div>
  );
}
