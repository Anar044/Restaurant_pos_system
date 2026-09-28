import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeGroups,
  type GroupDepartmentOption,
  type RestaurantGroupOption,
  createGroupPreparationType,
  createRestaurantDepartment,
  createRestaurantGroup,
  getBackOfficeGroups,
  setGroupCookingMap,
  setRestaurantGroupDevices,
  updateRestaurantDepartment,
  updateRestaurantGroup,
} from './api';
import './groups.css';

type Tab = 'main' | 'devices' | 'departments' | 'cooking';
type Editor =
  | { kind: 'group-create' }
  | { kind: 'group-edit'; group: RestaurantGroupOption }
  | { kind: 'department-create'; groupId: string }
  | { kind: 'department-edit'; department: GroupDepartmentOption }
  | { kind: 'type-create' }
  | null;

export function GroupsPage({ token }: { token: string }) {
  const [data, setData] = useState<BackOfficeGroups | null>(null);
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null);
  const [tab, setTab] = useState<Tab>('main');
  const [editor, setEditor] = useState<Editor>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const next = await getBackOfficeGroups(token);
      setData(next);
      setSelectedGroupId((current) =>
        current && next.groups.some((x) => x.id === current)
          ? current
          : next.groups[0]?.id ?? null,
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить группы и отделения');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const group = data?.groups.find((x) => x.id === selectedGroupId) ?? null;
  const departments = useMemo(
    () => (data?.departments ?? []).filter((x) => x.groupId === selectedGroupId),
    [data, selectedGroupId],
  );

  async function saveDevices(deviceIds: string[], mainCashRegisterId: string | null) {
    if (!group) return;
    setSaving(true);
    setError(null);
    try {
      await setRestaurantGroupDevices(token, group.id, { deviceIds, mainCashRegisterId });
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить терминалы группы');
    } finally {
      setSaving(false);
    }
  }

  async function changeMap(typeId: string, departmentId: string) {
    if (!group || !departmentId) return;
    setSaving(true);
    setError(null);
    try {
      await setGroupCookingMap(token, group.id, typeId, departmentId);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить карту приготовления');
    } finally {
      setSaving(false);
    }
  }

  if (!data && loading) return <div className="empty-state">Загружаем структуру ресторана…</div>;

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НАСТРОЙКИ РЕСТОРАНА</div>
          <h1>Группы и отделения</h1>
          <p>Кассы, официантские станции, залы, цеха, склады, принтеры и карта приготовления в одной структуре.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          <button className="primary-button" onClick={() => setEditor({ kind: 'group-create' })}>+ Группа</button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span><button onClick={() => void refresh()}>Повторить</button></div>}

      <div className="group-layout">
        <aside className="group-list">
          <div className="group-list-title"><strong>Группы</strong><small>{data?.groups.length ?? 0}</small></div>
          {(data?.groups ?? []).map((item) => (
            <button
              key={item.id}
              className={'group-list-item ' + (item.id === selectedGroupId ? 'active ' : '') + (!item.isActive ? 'inactive' : '')}
              onClick={() => { setSelectedGroupId(item.id); setTab('main'); }}
            >
              <strong>{item.name}</strong>
              <span>{item.deviceIds.length} терминалов</span>
            </button>
          ))}
        </aside>

        <div className="group-content">
          {group ? (
            <>
              <div className="group-header">
                <div>
                  <div className="eyebrow">ГРУППА РЕСТОРАНА</div>
                  <h2>{group.name}</h2>
                  <p>{departments.length} отделений · {group.deviceIds.length} терминалов</p>
                </div>
                <button className="secondary-button compact" onClick={() => setEditor({ kind: 'group-edit', group })}>Настроить</button>
              </div>

              <div className="group-tabs">
                <button className={tab === 'main' ? 'active' : ''} onClick={() => setTab('main')}>Основное</button>
                <button className={tab === 'devices' ? 'active' : ''} onClick={() => setTab('devices')}>Терминалы</button>
                <button className={tab === 'departments' ? 'active' : ''} onClick={() => setTab('departments')}>Отделения</button>
                <button className={tab === 'cooking' ? 'active' : ''} onClick={() => setTab('cooking')}>Карта приготовления</button>
              </div>

              {tab === 'main' && (
                <div className="group-summary-grid">
                  <SummaryCard label="Терминалы" value={group.deviceIds.length} text="кассы и официантские станции" />
                  <SummaryCard label="Отделения" value={departments.length} text="залы и производственные отделения" />
                  <SummaryCard
                    label="Типы приготовления"
                    value={data?.types.filter((x) => x.isActive).length ?? 0}
                    text="используются номенклатурой"
                  />
                  <SummaryCard
                    label="Настроено маршрутов"
                    value={data?.maps.filter((x) => x.groupId === group.id && x.isActive).length ?? 0}
                    text="тип → отделение"
                  />
                </div>
              )}

              {tab === 'devices' && (
                <DevicesTab
                  data={data!}
                  group={group}
                  saving={saving}
                  onSave={saveDevices}
                />
              )}

              {tab === 'departments' && (
                <div>
                  <div className="group-section-head">
                    <div><strong>Отделения группы</strong><span>Зал может участвовать в продажах; у производственного отделения задаются склад и принтер.</span></div>
                    <button className="primary-button compact" onClick={() => setEditor({ kind: 'department-create', groupId: group.id })}>+ Отделение</button>
                  </div>
                  <div className="department-grid">
                    {departments.map((department) => (
                      <button key={department.id} className="department-card" onClick={() => setEditor({ kind: 'department-edit', department })}>
                        <div className="department-card-head">
                          <strong>{department.name}</strong>
                          <span className={'badge ' + (department.isActive ? 'success' : 'neutral')}>{department.isActive ? 'Активно' : 'Отключено'}</span>
                        </div>
                        <div className="department-lines">
                          <span><b>Зал:</b> {department.hallName ?? '—'}</span>
                          <span><b>Склад:</b> {department.warehouseName ?? '—'}</span>
                          <span><b>Принтер:</b> {department.printerName ?? '—'}</span>
                        </div>
                      </button>
                    ))}
                  </div>
                  {departments.length === 0 && <div className="empty-state">В группе пока нет отделений.</div>}
                </div>
              )}

              {tab === 'cooking' && (
                <div>
                  <div className="group-section-head">
                    <div><strong>Карта приготовления</strong><span>Блюдо выбирает тип. Группа определяет, в какое отделение он направляется.</span></div>
                    <button className="secondary-button compact" onClick={() => setEditor({ kind: 'type-create' })}>+ Тип</button>
                  </div>
                  <div className="cooking-map-list">
                    {(data?.types ?? []).filter((x) => x.isActive).map((type) => {
                      const map = data?.maps.find((x) => x.groupId === group.id && x.preparationPlaceTypeId === type.id && x.isActive);
                      const selectedDepartment = map?.departmentId ?? '';
                      return (
                        <div className="cooking-map-row" key={type.id}>
                          <div><strong>{type.name}</strong><small>Тип места приготовления</small></div>
                          <span className="routing-arrow">→</span>
                          <select value={selectedDepartment} onChange={(e) => void changeMap(type.id, e.target.value)} disabled={saving}>
                            <option value="">Не настроено</option>
                            {departments.filter((x) => x.isActive).map((department) => (
                              <option key={department.id} value={department.id}>{department.name}</option>
                            ))}
                          </select>
                          <div className="map-result">
                            {selectedDepartment ? (() => {
                              const dep = departments.find((x) => x.id === selectedDepartment);
                              return <><span>Склад: {dep?.warehouseName ?? '—'}</span><span>Принтер: {dep?.printerName ?? '—'}</span></>;
                            })() : <span>Выберите отделение</span>}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </>
          ) : (
            <div className="empty-state">Создайте первую группу ресторана.</div>
          )}
        </div>
      </div>

      {editor && data && (
        <EditorModal
          editor={editor}
          data={data}
          token={token}
          onClose={() => setEditor(null)}
          onSaved={async () => { setEditor(null); await refresh(); }}
        />
      )}
    </section>
  );
}

function DevicesTab({
  data,
  group,
  saving,
  onSave,
}: {
  data: BackOfficeGroups;
  group: RestaurantGroupOption;
  saving: boolean;
  onSave: (deviceIds: string[], mainCashRegisterId: string | null) => Promise<void>;
}) {
  const [ids, setIds] = useState(group.deviceIds);
  const [mainId, setMainId] = useState(group.mainCashRegisterId ?? '');

  useEffect(() => {
    setIds(group.deviceIds);
    setMainId(group.mainCashRegisterId ?? '');
  }, [group.id, group.deviceIds.join('|'), group.mainCashRegisterId]);

  function toggle(id: string, checked: boolean) {
    setIds((current) => checked ? [...current.filter((x) => x !== id), id] : current.filter((x) => x !== id));
    if (!checked && mainId === id) setMainId('');
  }

  return (
    <div>
      <div className="group-section-head">
        <div><strong>Терминалы группы</strong><span>Главная касса координирует работу группы; остальные POS могут использоваться официантами.</span></div>
        <button className="primary-button compact" disabled={saving} onClick={() => void onSave(ids, mainId || null)}>Сохранить</button>
      </div>
      <div className="device-group-list">
        {data.devices.filter((x) => x.isActive).map((device) => {
          const checked = ids.includes(device.id);
          return (
            <div className="device-group-row" key={device.id}>
              <label><input type="checkbox" checked={checked} onChange={(e) => toggle(device.id, e.target.checked)} /><span><strong>{device.name}</strong><small>{device.type}</small></span></label>
              <label className="main-register-radio"><input type="radio" name="mainRegister" checked={mainId === device.id} disabled={!checked} onChange={() => setMainId(device.id)} /><span>Главная касса</span></label>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function SummaryCard({ label, value, text }: { label: string; value: number; text: string }) {
  return <div className="group-summary-card"><span>{label}</span><strong>{value}</strong><small>{text}</small></div>;
}

function EditorModal({
  editor,
  data,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<Editor, null>;
  data: BackOfficeGroups;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const department = editor.kind === 'department-edit' ? editor.department : null;
  const group = editor.kind === 'group-edit' ? editor.group : null;
  const [name, setName] = useState(department?.name ?? group?.name ?? '');
  const [hallId, setHallId] = useState(department?.hallId ?? '');
  const [warehouseId, setWarehouseId] = useState(department?.warehouseId ?? '');
  const [printerId, setPrinterId] = useState(department?.printerId ?? '');
  const [isActive, setIsActive] = useState(department?.isActive ?? group?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;
    setSaving(true); setError(null);
    try {
      if (editor.kind === 'group-create') await createRestaurantGroup(token, name.trim());
      else if (editor.kind === 'group-edit') await updateRestaurantGroup(token, editor.group.id, { name: name.trim(), isActive });
      else if (editor.kind === 'department-create') {
        await createRestaurantDepartment(token, editor.groupId, { name: name.trim(), hallId: hallId || null, warehouseId: warehouseId || null, printerId: printerId || null });
      } else if (editor.kind === 'department-edit') {
        await updateRestaurantDepartment(token, editor.department.groupId, editor.department.id, { name: name.trim(), hallId: hallId || null, warehouseId: warehouseId || null, printerId: printerId || null, isActive });
      } else if (editor.kind === 'type-create') {
        await createGroupPreparationType(token, name.trim());
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить');
    } finally { setSaving(false); }
  }

  const departmentMode = editor.kind === 'department-create' || editor.kind === 'department-edit';
  const title =
    editor.kind === 'group-create' ? 'Новая группа' :
    editor.kind === 'group-edit' ? 'Настройки группы' :
    editor.kind === 'type-create' ? 'Новый тип места приготовления' :
    editor.kind === 'department-create' ? 'Новое отделение' : 'Настройки отделения';

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header"><div><div className="eyebrow">СТРУКТУРА РЕСТОРАНА</div><h2>{title}</h2></div><button type="button" className="close-button" onClick={onClose}>×</button></div>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>

        {departmentMode && <>
          <label><span>Зал / зона продаж</span><select value={hallId} onChange={(e) => setHallId(e.target.value)}><option value="">Не используется как зал</option>{data.halls.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select><small className="field-hint">Если выбран зал со столами, отделение участвует в работе POS как зона продаж.</small></label>
          <label><span>Склад списания</span><select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}><option value="">Не назначен</option>{data.warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label><span>Принтер блюд</span><select value={printerId} onChange={(e) => setPrinterId(e.target.value)}><option value="">Не назначен</option>{data.printers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        </>}

        {(editor.kind === 'group-edit' || editor.kind === 'department-edit') && <label className="toggle-row"><span><strong>Активно</strong></span><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /></label>}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button></div>
      </form>
    </div>
  );
}
