import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeEquipment,
  type EquipmentDevice,
  type EquipmentPrinter,
  assignKitchenPrinter,
  createDevice,
  createPrinter,
  getBackOfficeEquipment,
  updateDevice,
  updatePrinter,
} from './api';
import './devices.css';

type EditorState =
  | { kind: 'printer-create' }
  | { kind: 'printer-edit'; printer: EquipmentPrinter }
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
          <p>Управляйте терминалами, экранами, принтерами и маршрутами печати кухни.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="secondary-button" onClick={() => setEditor({ kind: 'device-create' })}>+ Устройство</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'printer-create' })}>+ Принтер</button>
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
        <EquipmentStat label="Активные принтеры" value={stats.printers} detail={`${data?.printers.length ?? 0} всего`} />
        <EquipmentStat label="Станции без принтера" value={stats.unassignedStations} detail={stats.unassignedStations ? 'требуют настройки' : 'всё настроено'} warning={stats.unassignedStations > 0} />
      </div>

      <div className="equipment-section">
        <div className="equipment-section-header">
          <div>
            <h2>Принтеры</h2>
            <p>Сетевые ESC/POS-принтеры и Windows-очереди. Физическая печать будет подключена следующим этапом.</p>
          </div>
          <button className="primary-button compact" onClick={() => setEditor({ kind: 'printer-create' })}>+ Принтер</button>
        </div>

        {(data?.printers.length ?? 0) === 0 ? (
          <div className="equipment-empty">Принтеров пока нет. Добавьте первый принтер для кухни или кассы.</div>
        ) : (
          <div className="printer-grid">
            {data?.printers.map((printer) => (
              <button
                key={printer.id}
                className={`printer-card ${!printer.isActive ? 'inactive-card' : ''}`}
                onClick={() => setEditor({ kind: 'printer-edit', printer })}
              >
                <div className="printer-card-top">
                  <span className="equipment-icon">P</span>
                  <span className={`badge ${printer.isActive ? 'success' : 'neutral'}`}>{printer.isActive ? 'Активен' : 'Отключён'}</span>
                </div>
                <strong>{printer.name}</strong>
                <span>{printer.connectionType === 'Network' ? `${printer.address}:${printer.port ?? 9100}` : printer.address}</span>
                <small>{printer.connectionType === 'Network' ? 'Сетевой принтер' : 'Windows очередь'}</small>
                <div className="printer-links">
                  <span>Кухня: {printer.kitchenStationCount}</span>
                  <span>POS: {printer.posDeviceCount}</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="equipment-section">
        <div className="equipment-section-header">
          <div>
            <h2>Устройства</h2>
            <p>Кассы, планшеты, KDS, киоски, экраны покупателя и Restaurant Node.</p>
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
            <p>Укажите физический принтер для каждой кухонной станции.</p>
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
        <EquipmentEditor
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

function EquipmentEditor({
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
  const isPrinter = editor.kind.startsWith('printer');
  const printer = editor.kind === 'printer-edit' ? editor.printer : null;
  const device = editor.kind === 'device-edit' ? editor.device : null;
  const editing = editor.kind.endsWith('edit');

  const [name, setName] = useState(printer?.name ?? device?.name ?? '');
  const [connectionType, setConnectionType] = useState(printer?.connectionType ?? data.printerConnectionTypes[0] ?? 'Network');
  const [address, setAddress] = useState(printer?.address ?? '');
  const [port, setPort] = useState<number>(printer?.port ?? 9100);
  const [type, setType] = useState(device?.type ?? data.deviceTypes[0] ?? 'Pos');
  const [receiptPrinterId, setReceiptPrinterId] = useState(device?.receiptPrinterId ?? '');
  const [isActive, setIsActive] = useState(printer?.isActive ?? device?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title = editor.kind === 'printer-create' ? 'Новый принтер' :
    editor.kind === 'printer-edit' ? 'Настройки принтера' :
    editor.kind === 'device-create' ? 'Новое устройство' :
    'Настройки устройства';

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'printer-create') {
        await createPrinter(token, {
          name: name.trim(),
          connectionType,
          address: address.trim(),
          port: connectionType === 'Network' ? port : null,
        });
      } else if (editor.kind === 'printer-edit') {
        await updatePrinter(token, editor.printer.id, {
          name: name.trim(),
          connectionType,
          address: address.trim(),
          port: connectionType === 'Network' ? port : null,
          isActive,
        });
      } else if (editor.kind === 'device-create') {
        await createDevice(token, {
          name: name.trim(),
          type,
          receiptPrinterId: type === 'Pos' ? (receiptPrinterId || null) : null,
        });
      } else {
        await updateDevice(token, editor.device.id, {
          name: name.trim(),
          type,
          receiptPrinterId: type === 'Pos' ? (receiptPrinterId || null) : null,
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить оборудование');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card equipment-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">{isPrinter ? 'ПРИНТЕР' : 'УСТРОЙСТВО'}</div>
            <h2>{title}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Название</span>
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus />
        </label>

        {isPrinter ? (
          <div className="form-grid">
            <label className="full-field">
              <span>Подключение</span>
              <select value={connectionType} onChange={(e) => setConnectionType(e.target.value)}>
                {data.printerConnectionTypes.map((item) => (
                  <option value={item} key={item}>{item === 'Network' ? 'Сеть TCP/IP' : 'Windows очередь'}</option>
                ))}
              </select>
            </label>
            <label className={connectionType === 'Network' ? '' : 'full-field'}>
              <span>{connectionType === 'Network' ? 'IP / Hostname' : 'Имя Windows-принтера'}</span>
              <input value={address} onChange={(e) => setAddress(e.target.value)} maxLength={250} placeholder={connectionType === 'Network' ? '192.168.1.50' : 'EPSON TM-T20III'} />
            </label>
            {connectionType === 'Network' && (
              <label>
                <span>Порт</span>
                <input type="number" min={1} max={65535} value={port} onChange={(e) => setPort(Number(e.target.value))} />
              </label>
            )}
          </div>
        ) : (
          <div className="form-grid">
            <label className="full-field">
              <span>Тип устройства</span>
              <select value={type} onChange={(e) => setType(e.target.value)}>
                {data.deviceTypes.map((item) => <option value={item} key={item}>{deviceLabels[item] ?? item}</option>)}
              </select>
            </label>
            {type === 'Pos' && (
              <label className="full-field">
                <span>Чековый принтер</span>
                <select value={receiptPrinterId} onChange={(e) => setReceiptPrinterId(e.target.value)}>
                  <option value="">Не назначен</option>
                  {data.printers.filter((x) => x.isActive).map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}
                </select>
              </label>
            )}
          </div>
        )}

        {editing && (
          <label className="toggle-row">
            <span>
              <strong>Активно</strong>
              <small>{isPrinter ? 'Принтер доступен для маршрутизации' : 'Устройство разрешено для работы'}</small>
            </span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim() || (isPrinter && !address.trim())}>
            {saving ? 'Сохраняем…' : editing ? 'Сохранить' : 'Создать'}
          </button>
        </div>
      </form>
    </div>
  );
}
