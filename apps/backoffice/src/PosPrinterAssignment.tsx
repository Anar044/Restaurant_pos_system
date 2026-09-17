import { useEffect, useState } from 'react';
import {
  type BackOfficePosPrinters,
  assignPosReceiptPrinter,
  getBackOfficePosPrinters,
} from './api';

export function PosPrinterAssignment({
  token,
  onChanged,
}: {
  token: string;
  onChanged?: () => void | Promise<void>;
}) {
  const [data, setData] = useState<BackOfficePosPrinters | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingDeviceId, setSavingDeviceId] = useState<string | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficePosPrinters(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить локальные принтеры POS');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  async function assign(deviceId: string, printerId: string) {
    setSavingDeviceId(deviceId);
    setError(null);
    try {
      await assignPosReceiptPrinter(token, deviceId, printerId || null);
      await refresh();
      await onChanged?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось назначить чековый принтер');
    } finally {
      setSavingDeviceId(null);
    }
  }

  async function copyId(deviceId: string) {
    try {
      await navigator.clipboard.writeText(deviceId);
      setCopiedId(deviceId);
      window.setTimeout(() => setCopiedId((current) => current === deviceId ? null : current), 1600);
    } catch {
      setCopiedId(null);
    }
  }

  return (
    <div className="equipment-section pos-printer-section">
      <div className="equipment-section-header">
        <div>
          <h2>POS и локальные Windows-принтеры</h2>
          <p>
            POS Agent на каждой кассе автоматически находит принтеры, установленные в Windows.
            Сначала выберите POS, затем его локальный чековый принтер.
          </p>
        </div>
        <button className="secondary-button compact" onClick={() => void refresh()} disabled={loading}>
          Обновить
        </button>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      {!data && loading ? (
        <div className="equipment-empty">Загружаем POS и локальные принтеры…</div>
      ) : (data?.posDevices.length ?? 0) === 0 ? (
        <div className="equipment-empty">
          Сначала создайте устройство типа «POS-терминал». После этого его Device ID можно указать в POS Agent.
        </div>
      ) : (
        <div className="pos-agent-grid">
          {data?.posDevices.map((device) => {
            const availableWindows = device.windowsPrinters.filter((printer) => printer.isActive);
            return (
              <article className={`pos-agent-card ${!device.isActive ? 'inactive-card' : ''}`} key={device.id}>
                <div className="pos-agent-card-header">
                  <div>
                    <div className="title-row">
                      <h3>{device.name}</h3>
                      <span className={`connection-state ${device.isOnline ? 'online' : ''}`}>
                        <span />{device.isOnline ? 'Agent online' : device.lastSeenAt ? 'Agent offline' : 'Agent не подключался'}
                      </span>
                    </div>
                    <div className="pos-device-id-row">
                      <code>{device.id}</code>
                      <button type="button" className="mini-action" onClick={() => void copyId(device.id)}>
                        {copiedId === device.id ? '✓' : 'Копировать ID'}
                      </button>
                    </div>
                  </div>
                  <span className={`badge ${device.isActive ? 'success' : 'neutral'}`}>
                    {device.isActive ? 'Активна' : 'Отключена'}
                  </span>
                </div>

                <div className="pos-agent-printers">
                  <div className="pos-agent-printers-heading">
                    <strong>Найдено в Windows: {device.windowsPrinters.length}</strong>
                    <small>Список приходит с этого компьютера через POS Agent</small>
                  </div>

                  {device.windowsPrinters.length === 0 ? (
                    <div className="pos-agent-empty">
                      <strong>Принтеры ещё не получены</strong>
                      <span>Запустите POS Agent на этой кассе и нажмите «Обновить».</span>
                    </div>
                  ) : (
                    <div className="windows-printer-list">
                      {device.windowsPrinters.map((printer) => (
                        <div className={`windows-printer-row ${!printer.isActive ? 'inactive-product' : ''}`} key={printer.id}>
                          <span className="equipment-icon small">P</span>
                          <div>
                            <strong>{printer.queueName}</strong>
                            <small>{printer.isOnline ? 'Обнаружен недавно' : 'Сейчас не обнаружен'}</small>
                          </div>
                          {printer.isSelectedReceipt && <span className="badge success">Чековый</span>}
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                <label className="pos-receipt-select">
                  <span>Чековый принтер этой кассы</span>
                  <select
                    value={device.receiptPrinterId ?? ''}
                    disabled={savingDeviceId === device.id || !device.isActive}
                    onChange={(e) => void assign(device.id, e.target.value)}
                  >
                    <option value="">Не назначен</option>
                    {availableWindows.map((printer) => (
                      <option value={printer.id} key={printer.id}>{printer.queueName} · Windows этого POS</option>
                    ))}
                    {data?.networkPrinters.map((printer) => (
                      <option value={printer.id} key={printer.id}>{printer.name} · сеть {printer.address}:{printer.port ?? 9100}</option>
                    ))}
                  </select>
                  <small>
                    Windows-принтер можно назначить только той кассе, где POS Agent его обнаружил.
                  </small>
                </label>
              </article>
            );
          })}
        </div>
      )}
    </div>
  );
}
