import { FormEvent, type ReactNode, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeGroups,
  type GroupDepartmentOption,
  type GroupHallOption,
  getBackOfficeGroups,
  setRestaurantGroupPrinting,
  updateGroupHall,
  updateRestaurantDepartment,
} from './api';
import {
  type BackOfficePosPrinters,
  activateDiscoveredPrinter,
  assignPosReceiptPrinter,
  configurePosPrinter,
  getBackOfficePosPrinters,
} from './posPrintersApi';
import './printing.css';

type PrinterOption = {
  id: string;
  deviceId: string;
  deviceName: string;
  name: string;
  connectionType: string;
  isConfigured: boolean;
  isOnline: boolean;
};

export function PrintingPage({ token }: { token: string }) {
  const [groups, setGroups] = useState<BackOfficeGroups | null>(null);
  const [posData, setPosData] = useState<BackOfficePosPrinters | null>(null);
  const [loading, setLoading] = useState(true);
  const [savingKey, setSavingKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [networkEditor, setNetworkEditor] = useState(false);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const [nextGroups, nextPos] = await Promise.all([
        getBackOfficeGroups(token),
        getBackOfficePosPrinters(token),
      ]);
      setGroups(nextGroups);
      setPosData(nextPos);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить настройки печати');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const printers = useMemo<PrinterOption[]>(() => {
    const map = new Map<string, PrinterOption>();
    for (const pos of posData?.posDevices ?? []) {
      for (const printer of pos.configuredPrinters) {
        map.set(printer.id, {
          id: printer.id,
          deviceId: pos.id,
          deviceName: pos.name,
          name: printer.name,
          connectionType: printer.connectionType,
          isConfigured: true,
          isOnline: printer.isOnline,
        });
      }
      for (const printer of pos.discoveredWindowsPrinters) {
        if (!map.has(printer.id)) {
          map.set(printer.id, {
            id: printer.id,
            deviceId: pos.id,
            deviceName: pos.name,
            name: printer.queueName,
            connectionType: 'WindowsQueue',
            isConfigured: printer.isConfigured,
            isOnline: printer.isOnline,
          });
        }
      }
    }
    return [...map.values()].sort((a, b) =>
      a.deviceName.localeCompare(b.deviceName) || a.name.localeCompare(b.name));
  }, [posData]);

  async function ensureConfigured(printerId: string | null) {
    if (!printerId) return;
    const printer = printers.find((x) => x.id === printerId);
    if (!printer) throw new Error('Принтер не найден.');
    if (!printer.isConfigured) {
      await activateDiscoveredPrinter(token, printer.deviceId, printer.id);
    }
  }

  async function run(key: string, action: () => Promise<void>) {
    setSavingKey(key);
    setError(null);
    try {
      await action();
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить настройку печати');
    } finally {
      setSavingKey(null);
    }
  }

  async function setReceipt(deviceId: string, printerId: string) {
    await run('receipt:' + deviceId, async () => {
      await ensureConfigured(printerId || null);
      await assignPosReceiptPrinter(token, deviceId, printerId || null);
    });
  }

  async function setGroupDefault(groupId: string, printerId: string) {
    await run('group:' + groupId, async () => {
      await ensureConfigured(printerId || null);
      await setRestaurantGroupPrinting(token, groupId, printerId || null);
    });
  }

  async function setHallPrinter(hall: GroupHallOption, printerId: string) {
    await run('hall:' + hall.id, async () => {
      await ensureConfigured(printerId || null);
      await updateGroupHall(token, hall.groupId, hall.id, {
        name: hall.name,
        sortOrder: hall.sortOrder,
        precheckPrinterId: printerId || null,
        isActive: hall.isActive,
      });
    });
  }

  async function setDepartmentPrinter(department: GroupDepartmentOption, printerId: string) {
    await run('department:' + department.id, async () => {
      await ensureConfigured(printerId || null);
      await updateRestaurantDepartment(token, department.groupId, department.id, {
        name: department.name,
        preparationPlaceTypeId: department.preparationPlaceTypeId,
        warehouseId: department.warehouseId,
        printerId: printerId || null,
        isActive: department.isActive,
      });
    });
  }

  if (!groups && loading) {
    return <div className="empty-state">Загружаем настройки печати…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НАСТРОЙКИ РЕСТОРАНА</div>
          <h1>Печать</h1>
          <p>Все принтеры и все назначения собраны в одном месте. Windows-принтеры появляются автоматически через POS Agent.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => setNetworkEditor(true)}>+ Сетевой принтер</button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="printing-section">
        <div className="printing-section-head">
          <div>
            <h2>Принтеры</h2>
            <p>Windows-принтеры не нужно добавлять вручную: POS Agent обнаруживает их сам. Сетевой принтер добавляется только по IP.</p>
          </div>
        </div>

        <div className="printing-pos-grid">
          {(posData?.posDevices ?? []).map((pos) => (
            <article className="printing-pos-card" key={pos.id}>
              <div className="printing-pos-head">
                <div>
                  <strong>{pos.name}</strong>
                  <span className={'connection-state ' + (pos.isOnline ? 'online' : '')}>
                    <span />{pos.isOnline ? 'Agent online' : 'Agent offline'}
                  </span>
                </div>
                <small>{pos.discoveredWindowsPrinters.length} Windows · {pos.configuredPrinters.length} настроено</small>
              </div>
              <div className="printing-printer-list">
                {printers.filter((x) => x.deviceId === pos.id).map((printer) => (
                  <div className="printing-printer-row" key={printer.id}>
                    <span className={'mini-dot ' + (!printer.isOnline ? 'off' : '')} />
                    <div>
                      <strong>{printer.name}</strong>
                      <small>
                        {printer.connectionType === 'WindowsQueue' ? 'Windows' : 'Сетевой'}
                        {!printer.isConfigured ? ' · найден автоматически' : ''}
                      </small>
                    </div>
                  </div>
                ))}
                {printers.filter((x) => x.deviceId === pos.id).length === 0 && (
                  <div className="printing-empty-inline">Принтеры пока не обнаружены.</div>
                )}
              </div>
            </article>
          ))}
        </div>
      </div>

      <div className="printing-section">
        <div className="printing-section-head">
          <div>
            <h2>Маршрутизация печати</h2>
            <p>Выберите, куда печатаются чеки, предчеки и кухонные заказы. Изменения сохраняются сразу.</p>
          </div>
        </div>

        <div className="printing-groups">
          {(groups?.groups ?? []).map((group) => {
            const groupDevices = (posData?.posDevices ?? []).filter((x) => group.deviceIds.includes(x.id));
            const halls = (groups?.halls ?? []).filter((x) => x.groupId === group.id);
            const departments = (groups?.departments ?? []).filter((x) => x.groupId === group.id);
            return (
              <article className="printing-group-card" key={group.id}>
                <div className="printing-group-title">
                  <div>
                    <span className="eyebrow">ГРУППА</span>
                    <h3>{group.name}</h3>
                  </div>
                  <span>{groupDevices.length} POS · {halls.length} залов · {departments.length} отделений</span>
                </div>

                <PrintingBlock title="Чеки оплаты" subtitle="Чек печатается на принтере того POS, где проходит оплата.">
                  {groupDevices.map((device) => (
                    <RouteRow
                      key={device.id}
                      label={device.name}
                      detail={group.mainCashRegisterId === device.id ? 'Главная касса' : 'POS терминал'}
                      value={device.receiptPrinterId ?? ''}
                      printers={printers}
                      saving={savingKey === 'receipt:' + device.id}
                      emptyLabel="Не назначен"
                      onChange={(value) => void setReceipt(device.id, value)}
                    />
                  ))}
                  {groupDevices.length === 0 && <div className="printing-empty-inline">В группе нет POS-терминалов.</div>}
                </PrintingBlock>

                <PrintingBlock title="Предчеки" subtitle="Один принтер можно назначить группе, а для отдельного зала выбрать исключение.">
                  <RouteRow
                    label="По умолчанию для группы"
                    detail="Используется всеми залами без собственного принтера"
                    value={group.defaultPrecheckPrinterId ?? ''}
                    printers={printers}
                    saving={savingKey === 'group:' + group.id}
                    emptyLabel="Не назначен"
                    onChange={(value) => void setGroupDefault(group.id, value)}
                  />
                  {halls.map((hall) => (
                    <RouteRow
                      key={hall.id}
                      label={hall.name}
                      detail={hall.precheckPrinterId ? 'Свой принтер' : 'Использует настройку группы'}
                      value={hall.precheckPrinterId ?? ''}
                      printers={printers}
                      saving={savingKey === 'hall:' + hall.id}
                      emptyLabel={
                        group.defaultPrecheckPrinterName
                          ? 'По умолчанию · ' + group.defaultPrecheckPrinterName
                          : 'По умолчанию группы'
                      }
                      onChange={(value) => void setHallPrinter(hall, value)}
                    />
                  ))}
                </PrintingBlock>

                <PrintingBlock title="Кухня и бар" subtitle="Для производственных отделений принтер назначается отдельно.">
                  {departments.map((department) => (
                    <RouteRow
                      key={department.id}
                      label={department.name}
                      detail={department.preparationPlaceTypeName ?? 'Тип приготовления не назначен'}
                      value={department.printerId ?? ''}
                      printers={printers}
                      saving={savingKey === 'department:' + department.id}
                      emptyLabel="Не назначен"
                      onChange={(value) => void setDepartmentPrinter(department, value)}
                    />
                  ))}
                  {departments.length === 0 && <div className="printing-empty-inline">В группе нет отделений.</div>}
                </PrintingBlock>
              </article>
            );
          })}
        </div>
      </div>

      {networkEditor && posData && (
        <NetworkPrinterEditor
          token={token}
          posData={posData}
          onClose={() => setNetworkEditor(false)}
          onSaved={async () => {
            setNetworkEditor(false);
            await refresh();
          }}
        />
      )}
    </section>
  );
}

function PrintingBlock({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: ReactNode;
}) {
  return (
    <div className="printing-block">
      <div className="printing-block-title">
        <strong>{title}</strong>
        <span>{subtitle}</span>
      </div>
      <div className="printing-route-list">{children}</div>
    </div>
  );
}

function RouteRow({
  label,
  detail,
  value,
  printers,
  saving,
  emptyLabel,
  onChange,
}: {
  label: string;
  detail: string;
  value: string;
  printers: PrinterOption[];
  saving: boolean;
  emptyLabel: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="printing-route-row">
      <div>
        <strong>{label}</strong>
        <span>{detail}</span>
      </div>
      <div className="printing-route-control">
        <select value={value} disabled={saving} onChange={(e) => onChange(e.target.value)}>
          <option value="">{emptyLabel}</option>
          {printers.map((printer) => (
            <option key={printer.id} value={printer.id}>
              {printer.name} · {printer.deviceName}{printer.isConfigured ? '' : ' · найден'}
            </option>
          ))}
        </select>
        {saving && <small>Сохраняем…</small>}
      </div>
    </div>
  );
}

function NetworkPrinterEditor({
  token,
  posData,
  onClose,
  onSaved,
}: {
  token: string;
  posData: BackOfficePosPrinters;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [deviceId, setDeviceId] = useState(posData.posDevices.find((x) => x.isActive)?.id ?? posData.posDevices[0]?.id ?? '');
  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [port, setPort] = useState(9100);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!deviceId || !name.trim() || !address.trim()) return;
    setSaving(true);
    setError(null);
    try {
      await configurePosPrinter(token, deviceId, {
        name: name.trim(),
        connectionType: 'Network',
        queueName: null,
        address: address.trim(),
        port,
      });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось добавить сетевой принтер');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card equipment-modal" onSubmit={submit}>
        <div className="modal-header">
          <div><div className="eyebrow">ПЕЧАТЬ</div><h2>Сетевой принтер</h2></div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>
        <label>
          <span>Через какой POS Agent печатать</span>
          <select value={deviceId} onChange={(e) => setDeviceId(e.target.value)}>
            {posData.posDevices.map((pos) => <option key={pos.id} value={pos.id}>{pos.name}</option>)}
          </select>
        </label>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} placeholder="Например: Принтер бара" autoFocus /></label>
        <div className="form-grid">
          <label><span>IP / Hostname</span><input value={address} onChange={(e) => setAddress(e.target.value)} placeholder="192.168.1.50" /></label>
          <label><span>Порт</span><input type="number" min={1} max={65535} value={port} onChange={(e) => setPort(Number(e.target.value))} /></label>
        </div>
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !deviceId || !name.trim() || !address.trim()}>
            {saving ? 'Добавляем…' : 'Добавить'}
          </button>
        </div>
      </form>
    </div>
  );
}
