import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficePosPrinters,
  type PosPrinterDevice,
  assignPosReceiptPrinter,
  configurePosPrinter,
  getBackOfficePosPrinters,
} from './posPrintersApi';

export function PosPrinterAssignment({
  token,
  onChanged,
}: {
  token: string;
  onChanged?: () => void | Promise<void>;
}) {
  const [data, setData] = useState<BackOfficePosPrinters | null>(null);
  const [selectedPosId, setSelectedPosId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingReceipt, setSavingReceipt] = useState(false);
  const [showPrinterEditor, setShowPrinterEditor] = useState(false);
  const [copiedId, setCopiedId] = useState(false);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const next = await getBackOfficePosPrinters(token);
      setData(next);
      setSelectedPosId((current) => {
        if (current && next.posDevices.some((x) => x.id === current)) return current;
        return next.posDevices.find((x) => x.isActive)?.id ?? next.posDevices[0]?.id ?? '';
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить POS и принтеры');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const selectedPos = useMemo(
    () => data?.posDevices.find((x) => x.id === selectedPosId) ?? null,
    [data, selectedPosId],
  );

  async function assignReceipt(printerId: string) {
    if (!selectedPos) return;
    setSavingReceipt(true);
    setError(null);
    try {
      await assignPosReceiptPrinter(token, selectedPos.id, printerId || null);
      await refresh();
      await onChanged?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось назначить чековый принтер');
    } finally {
      setSavingReceipt(false);
    }
  }

  async function copyId() {
    if (!selectedPos) return;
    try {
      await navigator.clipboard.writeText(selectedPos.id);
      setCopiedId(true);
      window.setTimeout(() => setCopiedId(false), 1600);
    } catch {
      setCopiedId(false);
    }
  }

  return (
    <div className="equipment-section pos-printer-section">
      <div className="equipment-section-header">
        <div>
          <h2>POS и принтеры</h2>
          <p>Выберите кассу. Затем добавьте к ней столько Windows- или сетевых принтеров, сколько нужно.</p>
        </div>
        <button className="secondary-button compact" onClick={() => void refresh()} disabled={loading}>Обновить</button>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      {!data && loading ? (
        <div className="equipment-empty">Загружаем POS и принтеры…</div>
      ) : (data?.posDevices.length ?? 0) === 0 ? (
        <div className="equipment-empty">Сначала создайте устройство типа «POS-терминал».</div>
      ) : (
        <>
          <div className="pos-selection-bar">
            <label>
              <span>POS</span>
              <select value={selectedPosId} onChange={(e) => setSelectedPosId(e.target.value)}>
                {data?.posDevices.map((pos) => (
                  <option key={pos.id} value={pos.id}>{pos.name}{pos.isActive ? '' : ' · отключён'}</option>
                ))}
              </select>
            </label>
            <button
              className="primary-button"
              disabled={!selectedPos?.isActive}
              onClick={() => setShowPrinterEditor(true)}
            >
              + Добавить принтер
            </button>
          </div>

          {selectedPos && (
            <PosDetails
              pos={selectedPos}
              copiedId={copiedId}
              savingReceipt={savingReceipt}
              onCopyId={copyId}
              onAssignReceipt={assignReceipt}
              onAddPrinter={() => setShowPrinterEditor(true)}
            />
          )}
        </>
      )}

      {showPrinterEditor && selectedPos && (
        <PrinterEditor
          token={token}
          pos={selectedPos}
          onClose={() => setShowPrinterEditor(false)}
          onSaved={async () => {
            setShowPrinterEditor(false);
            await refresh();
            await onChanged?.();
          }}
        />
      )}
    </div>
  );
}

function PosDetails({
  pos,
  copiedId,
  savingReceipt,
  onCopyId,
  onAssignReceipt,
  onAddPrinter,
}: {
  pos: PosPrinterDevice;
  copiedId: boolean;
  savingReceipt: boolean;
  onCopyId: () => Promise<void>;
  onAssignReceipt: (printerId: string) => Promise<void>;
  onAddPrinter: () => void;
}) {
  const unconfiguredWindows = pos.discoveredWindowsPrinters.filter((x) => !x.isConfigured);

  return (
    <article className={`pos-selected-card ${!pos.isActive ? 'inactive-card' : ''}`}>
      <div className="pos-agent-card-header">
        <div>
          <div className="title-row">
            <h3>{pos.name}</h3>
            <span className={`connection-state ${pos.isOnline ? 'online' : ''}`}>
              <span />{pos.isOnline ? 'Agent online' : pos.lastSeenAt ? 'Agent offline' : 'Agent не подключался'}
            </span>
          </div>
          <div className="pos-device-id-row">
            <code>{pos.id}</code>
            <button type="button" className="mini-action" onClick={() => void onCopyId()}>{copiedId ? '✓' : 'Копировать ID'}</button>
          </div>
        </div>
        <div className="pos-summary-badges">
          <span className="badge neutral">Windows найдено: {pos.discoveredWindowsPrinters.length}</span>
          <span className="badge neutral">Настроено: {pos.configuredPrinters.length}</span>
        </div>
      </div>

      <div className="configured-printers-header">
        <div>
          <strong>Принтеры этой кассы</strong>
          <span>Один POS может иметь любое нужное количество принтеров.</span>
        </div>
        <button className="secondary-button compact" disabled={!pos.isActive} onClick={onAddPrinter}>+ Добавить принтер</button>
      </div>

      {pos.configuredPrinters.length === 0 ? (
        <div className="pos-agent-empty">
          <strong>Пока нет настроенных принтеров</strong>
          <span>{unconfiguredWindows.length > 0 ? `POS Agent уже нашёл ${unconfiguredWindows.length} Windows-принтер(а/ов). Нажмите «Добавить принтер».` : 'Запустите POS Agent, если хотите добавить Windows-принтер, либо добавьте сетевой.'}</span>
        </div>
      ) : (
        <div className="configured-printer-grid">
          {pos.configuredPrinters.map((printer) => (
            <div className={`configured-printer-card ${!printer.isActive ? 'inactive-card' : ''}`} key={printer.id}>
              <div className="printer-card-top">
                <span className="equipment-icon">P</span>
                <div className="printer-card-badges">
                  {printer.isSelectedReceipt && <span className="badge success">Чековый</span>}
                  <span className={`badge ${printer.isActive ? 'success' : 'neutral'}`}>{printer.isActive ? 'Активен' : 'Отключён'}</span>
                </div>
              </div>
              <strong>{printer.name}</strong>
              <span>{printer.connectionType === 'WindowsQueue' ? printer.address : `${printer.address}:${printer.port ?? 9100}`}</span>
              <small>{printer.connectionType === 'WindowsQueue' ? 'Windows-принтер этой кассы' : 'Сетевой принтер этой кассы'}</small>
            </div>
          ))}
        </div>
      )}

      <label className="pos-receipt-select">
        <span>Чековый принтер этой кассы</span>
        <select
          value={pos.receiptPrinterId ?? ''}
          disabled={savingReceipt || !pos.isActive}
          onChange={(e) => void onAssignReceipt(e.target.value)}
        >
          <option value="">Не назначен</option>
          {pos.configuredPrinters.filter((x) => x.isActive).map((printer) => (
            <option value={printer.id} key={printer.id}>{printer.name}</option>
          ))}
        </select>
        <small>Это один из настроенных принтеров выбранного POS. Остальные можно использовать для кухни, бара и других задач.</small>
      </label>
    </article>
  );
}

function PrinterEditor({
  token,
  pos,
  onClose,
  onSaved,
}: {
  token: string;
  pos: PosPrinterDevice;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const availableWindows = pos.discoveredWindowsPrinters.filter((x) => !x.isConfigured);
  const [connectionType, setConnectionType] = useState<'WindowsQueue' | 'Network'>(availableWindows.length > 0 ? 'WindowsQueue' : 'Network');
  const [name, setName] = useState('');
  const [queueName, setQueueName] = useState(availableWindows[0]?.queueName ?? '');
  const [address, setAddress] = useState('');
  const [port, setPort] = useState(9100);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      await configurePosPrinter(token, pos.id, {
        name: name.trim(),
        connectionType,
        queueName: connectionType === 'WindowsQueue' ? queueName : null,
        address: connectionType === 'Network' ? address.trim() : null,
        port: connectionType === 'Network' ? port : null,
      });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось добавить принтер');
    } finally {
      setSaving(false);
    }
  }

  const invalid = !name.trim() ||
    (connectionType === 'WindowsQueue' && !queueName) ||
    (connectionType === 'Network' && !address.trim());

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card equipment-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">{pos.name}</div>
            <h2>Новый принтер</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label>
          <span>Тип подключения</span>
          <select value={connectionType} onChange={(e) => setConnectionType(e.target.value as 'WindowsQueue' | 'Network')}>
            <option value="WindowsQueue" disabled={availableWindows.length === 0}>Windows этой кассы{availableWindows.length ? ` · доступно ${availableWindows.length}` : ' · ничего нового не найдено'}</option>
            <option value="Network">Сетевой TCP/IP</option>
          </select>
        </label>

        <label>
          <span>Название в системе</span>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Например: Чековый принтер" maxLength={100} autoFocus />
        </label>

        {connectionType === 'WindowsQueue' ? (
          <label>
            <span>Принтер Windows</span>
            <select value={queueName} onChange={(e) => setQueueName(e.target.value)}>
              <option value="">Выберите принтер</option>
              {availableWindows.map((printer) => (
                <option value={printer.queueName} key={printer.id}>{printer.queueName}{printer.isDefault ? ' · по умолчанию' : ''}</option>
              ))}
            </select>
            <small>Список получен напрямую с Windows-компьютера {pos.name} через POS Agent.</small>
          </label>
        ) : (
          <div className="form-grid">
            <label>
              <span>IP / Hostname</span>
              <input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="192.168.1.50" maxLength={250} />
            </label>
            <label>
              <span>Порт</span>
              <input type="number" min={1} max={65535} value={port} onChange={(e) => setPort(Number(e.target.value))} />
            </label>
          </div>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || invalid}>{saving ? 'Добавляем…' : 'Добавить'}</button>
        </div>
      </form>
    </div>
  );
}
