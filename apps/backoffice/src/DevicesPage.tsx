import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeEquipment,
  type EquipmentDevice,
  assignKitchenPrinter,
  createDevice,
  getBackOfficeEquipment,
  updateDevice,
} from './api';
import { PosPrinterAssignment } from './PosPrinterAssignment';
import './devices.css';

type EditorState =
  | { kind: 'device-create' }
  | { kind: 'device-edit'; device: EquipmentDevice }
  | null;

const deviceLabels: Record<string, string> = {
  Pos: 'POS-терминал',
  WaiterTablet: 'Планшет официанта',
  KitchenDisplay: 'Kitchen Display',
  Kiosk: 'Киоск',
  CustomerDisplay: 'Экран покупателя',
  RestaurantNode: 'Restaurant Node',
};

export function DevicesPage({ token }: { token: string }) {
  const [data, setData] = useState<BackOfficeEquipment | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState>(null);
  const [savingStationId, setSavingStationId] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeEquipment(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить оборудование');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const stats = useMemo(() => {
    const devices = data?.devices ?? [];
    const printers = data?.printers ?? [];
    const stations = data?.kitchenStations ?? [];
    return {
      devices: devices.filter((x) => x.isActive).length,
      printers: printers.filter((x) => x.isActive).length,
      unassignedStations: stations.filter((x) => x.isActive && !x.printerId).length,
    };
  }, [data]);

  async function setStationPrinter(stationId: string, printerId: string) {
    setSavingStationId(stationId);
    setError(null);
    try {
      await assignKitchenPrinter(token, stationId, printerId || null);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить маршрут печати');
    } finally {
      setSavingStationId(null);
    }
  }

  if (!data && loading) {
    return <div className="empty-state">Загружаем оборудование…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">ИНФРАСТРУКТУРА РЕСТОРАНА</div>
          <h1>Оборудование</h1>
          <p>Сначала выберите POS, затем добавляйте и настраивайте все его принтеры.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'device-create' })}>+ Устройство</button>
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="stats-grid">
        <EquipmentStat label="Активные устройства" value={stats.devices} detail={`${data?.devices.length ?? 0} всего`} />
        <EquipmentStat label="Настроенные принтеры" value={stats.printers} detail="по всем POS" />
        <EquipmentStat label="Станции без принтера" value={stats.unassignedStations} detail={stats.unassignedStations ? 'требуют настройки' : 'всё настроено'} warning={stats.unassignedStations > 0} />
      </div>

      <PosPrinterAssignment token={token} onChanged={refresh} />

      <div className="equipment-section">
        <div className="equipment-section-header">
          <div>
            <h2>Другие устройства</h2>
            <p>POS, планшеты, KDS, киоски, экраны покупателя и Restaurant Node.</p>
          </div>
          <button className="secondary-button compact" onClick={() => setEditor({ kind: 'device-create' })}>+ Устройство</button>
        </div>

        {(data?.devices.length ?? 0) === 0 ? (
          <div className="equipment-empty">Устройства ещё не зарегистрированы.</div>
        ) : (
          <div className="equipment-table-wrap">
            <table className="equipment-table">
              <thead>
                <tr>
                  <th>Устройство</th>
                  <th>Тип</th>
                  <th>Чековый принтер</th>
                  <th>Связь</th>
                  <th>Статус</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {data?.devices.map((device) => (
                  <tr key={device.id} className={!device.isActive ? 'row-inactive' : ''}>
                    <td><strong>{device.name}</strong></td>
                    <td>{deviceLabels[device.type] ?? device.type}</td>
                    <td>{device.type === 'Pos' ? (device.receiptPrinterName ?? 'Не назначен') : '—'}</td>
                    <td>
                      <span className={`connection-state ${device.isOnline ? 'online' : ''}`}>
                        <span />{device.isOnline ? 'Online' : device.lastSeenAt ? 'Offline' : 'Не подключалось'}
                      </span>
                    </td>
                    <td><span className={`badge ${device.isActive ? 'success' : 'neutral'}`}>{device.isActive ? 'Активно' : 'Отключено'}</span></td>
                    <td className="equipment-action-cell"><button className="text-button" onClick={() => setEditor({ kind: 'device-edit', device })}>Настроить</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="equipment-section">
        <div className="equipment-section-header">
          <div>
            <h2>Маршруты кухонной печати</h2>
            <p>После настройки принтеров на POS назначьте нужный принтер каждой кухонной станции.</p>
          </div>
        </div>

        <div className="route-list">
          {data?.kitchenStations.map((station) => (
            <div className="route-row" key={station.id}>
              <div>
                <strong>{station.name}</strong>
                <span>{station.isActive ? 'Активная станция' : 'Станция отключена'}</span>
              </div>
              <div className="route-arrow">→</div>
              <select
                value={station.printerId ?? ''}
                disabled={savingStationId === station.id}
                onChange={(e) => void setStationPrinter(station.id, e.target.value)}
              >
                <option value="">Принтер не назначен</option>
                {data?.printers.filter((x) => x.isActive).map((printer) => (
                  <option value={printer.id} key={printer.id}>{printer.name}</option>
                ))}
              </select>
            </div>
          ))}
          {(data?.kitchenStations.length ?? 0) === 0 && <div className="equipment-empty">Кухонных станций пока нет.</div>}
        </div>
      </div>

      {editor && data && (
        <DeviceEditor
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

function EquipmentStat({ label, value, detail, warning = false }: { label: string; value: number; detail: string; warning?: boolean }) {
  return (
    <div className={`stat-card ${warning ? 'stat-warning' : ''}`}>
      <span>{label}</span>
      <div><strong>{value}</strong><small>{detail}</small></div>
    </div>
  );
}

function DeviceEditor({
  editor,
  data,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  data: BackOfficeEquipment;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const device = editor.kind === 'device-edit' ? editor.device : null;
  const editing = editor.kind === 'device-edit';

  const [name, setName] = useState(device?.name ?? '');
  const [type, setType] = useState(device?.type ?? data.deviceTypes[0] ?? 'Pos');
  const [isActive, setIsActive] = useState(device?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'device-create') {
        await createDevice(token, {
          name: name.trim(),
          type,
          receiptPrinterId: null,
        });
      } else {
        await updateDevice(token, editor.device.id, {
          name: name.trim(),
          type,
          receiptPrinterId: type === 'Pos' ? editor.device.receiptPrinterId : null,
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить устройство');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card equipment-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">УСТРОЙСТВО</div>
            <h2>{editing ? 'Настройки устройства' : 'Новое устройство'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Название</span>
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus />
        </label>

        <div className="form-grid">
          <label className="full-field">
            <span>Тип устройства</span>
            <select value={type} onChange={(e) => setType(e.target.value)}>
              {data.deviceTypes.map((item) => <option value={item} key={item}>{deviceLabels[item] ?? item}</option>)}
            </select>
          </label>
          {type === 'Pos' && (
            <div className="full-field pos-agent-info-box">
              <strong>Принтеры настраиваются внутри выбранного POS</strong>
              <span>После запуска POS Agent Windows-принтеры этой кассы появятся в списке автоматически.</span>
            </div>
          )}
        </div>

        {editing && (
          <label className="toggle-row">
            <span>
              <strong>Активно</strong>
              <small>Устройство разрешено для работы</small>
            </span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>
            {saving ? 'Сохраняем…' : editing ? 'Сохранить' : 'Создать'}
          </button>
        </div>
      </form>
    </div>
  );
}