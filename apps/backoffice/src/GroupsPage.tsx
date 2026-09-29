import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeGroups,
  type DiningTable,
  type GroupDepartmentOption,
  type GroupHallOption,
  type RestaurantGroupOption,
  createGroupHall,
  createGroupPreparationType,
  createRestaurantDepartment,
  createRestaurantGroup,
  createTable,
  getBackOfficeGroups,
  setRestaurantGroupDevices,
  updateGroupHall,
  updateRestaurantDepartment,
  updateRestaurantGroup,
  updateTable,
} from './api';
import './groups.css';

type Tab = 'main' | 'devices' | 'departments' | 'halls';

type Editor =
  | { kind: 'group-create' }
  | { kind: 'group-edit'; group: RestaurantGroupOption }
  | { kind: 'department-create'; groupId: string }
  | { kind: 'department-edit'; department: GroupDepartmentOption }
  | { kind: 'hall-create'; groupId: string }
  | { kind: 'hall-edit'; hall: GroupHallOption }
  | { kind: 'table-create'; hall: GroupHallOption }
  | { kind: 'table-edit'; hall: GroupHallOption; table: DiningTable }
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
      setError(e instanceof Error ? e.message : 'Не удалось загрузить структуру ресторана');
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
  const halls = useMemo(
    () => (data?.halls ?? []).filter((x) => x.groupId === selectedGroupId),
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

  if (!data && loading) return <div className="empty-state">Загружаем структуру ресторана…</div>;

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НАСТРОЙКИ РЕСТОРАНА</div>
          <h1>Группы ресторана</h1>
          <p>В каждой группе настраиваются главная касса, POS официантов, производственные отделения, залы и столы.</p>
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
          {(data?.groups ?? []).map((item) => {
            const groupDepartments = data?.departments.filter((x) => x.groupId === item.id).length ?? 0;
            const groupHalls = data?.halls.filter((x) => x.groupId === item.id).length ?? 0;
            return (
              <button
                key={item.id}
                className={'group-list-item ' + (item.id === selectedGroupId ? 'active ' : '') + (!item.isActive ? 'inactive' : '')}
                onClick={() => { setSelectedGroupId(item.id); setTab('main'); }}
              >
                <strong>{item.name}</strong>
                <span>{item.deviceIds.length} терминалов · {groupDepartments} отделений · {groupHalls} залов</span>
              </button>
            );
          })}
        </aside>

        <div className="group-content">
          {group ? (
            <>
              <div className="group-header">
                <div>
                  <div className="eyebrow">ГРУППА РЕСТОРАНА</div>
                  <h2>{group.name}</h2>
                  <p>{group.deviceIds.length} терминалов · {departments.length} отделений · {halls.length} залов</p>
                </div>
                <button className="secondary-button compact" onClick={() => setEditor({ kind: 'group-edit', group })}>Настроить</button>
              </div>

              <div className="group-tabs">
                <button className={tab === 'main' ? 'active' : ''} onClick={() => setTab('main')}>Основное</button>
                <button className={tab === 'devices' ? 'active' : ''} onClick={() => setTab('devices')}>Терминалы</button>
                <button className={tab === 'departments' ? 'active' : ''} onClick={() => setTab('departments')}>Отделения</button>
                <button className={tab === 'halls' ? 'active' : ''} onClick={() => setTab('halls')}>Залы и столы</button>
              </div>

              {tab === 'main' && (
                <div className="group-summary-grid">
                  <SummaryCard label="Терминалы" value={group.deviceIds.length} text="главная касса и POS официантов" />
                  <SummaryCard label="Отделения" value={departments.length} text="места приготовления и списания" />
                  <SummaryCard label="Залы" value={halls.length} text={halls.reduce((sum, hall) => sum + hall.tables.length, 0) + ' столов'} />
                  <SummaryCard
                    label="Настроено типов приготовления"
                    value={departments.filter((x) => x.preparationPlaceTypeId && x.isActive).length}
                    text="тип напрямую назначен отделению"
                  />
                </div>
              )}

              {tab === 'devices' && (
                <DevicesTab data={data!} group={group} saving={saving} onSave={saveDevices} />
              )}

              {tab === 'departments' && (
                <div>
                  <div className="group-section-head">
                    <div>
                      <strong>Отделения группы</strong>
                      <span>Здесь задаются структура, тип приготовления и склад списания. Принтеры настраиваются отдельно в разделе «Печать».</span>
                    </div>
                    <div className="heading-actions">
                      <button className="secondary-button compact" onClick={() => setEditor({ kind: 'type-create' })}>+ Тип приготовления</button>
                      <button className="primary-button compact" onClick={() => setEditor({ kind: 'department-create', groupId: group.id })}>+ Отделение</button>
                    </div>
                  </div>

                  <div className="department-grid">
                    {departments.map((department) => (
                      <button key={department.id} className="department-card" onClick={() => setEditor({ kind: 'department-edit', department })}>
                        <div className="department-card-head">
                          <strong>{department.name}</strong>
                          <span className={'badge ' + (department.isActive ? 'success' : 'neutral')}>{department.isActive ? 'Активно' : 'Отключено'}</span>
                        </div>
                        <div className="department-lines">
                          <span><b>Тип приготовления:</b> {department.preparationPlaceTypeName ?? '—'}</span>
                          <span><b>Склад списания:</b> {department.warehouseName ?? '—'}</span>
                          <span><b>Печать:</b> {department.printerName ?? 'Не настроено'} · раздел «Печать»</span>
                        </div>
                      </button>
                    ))}
                  </div>
                  {departments.length === 0 && <div className="empty-state">В группе пока нет производственных отделений.</div>}
                </div>
              )}

              {tab === 'halls' && (
                <div>
                  <div className="group-section-head">
                    <div>
                      <strong>Залы и столы группы</strong>
                      <span>Каждый зал принадлежит группе и имеет свои столы. Маршрутизация предчеков настраивается в разделе «Печать».</span>
                    </div>
                    <button className="primary-button compact" onClick={() => setEditor({ kind: 'hall-create', groupId: group.id })}>+ Зал</button>
                  </div>

                  <div className="group-hall-list">
                    {halls.map((hall) => (
                      <article key={hall.id} className={'group-hall-card ' + (!hall.isActive ? 'inactive-card' : '')}>
                        <div className="group-hall-header">
                          <div>
                            <div className="department-card-head">
                              <strong>{hall.name}</strong>
                              <span className={'badge ' + (hall.isActive ? 'success' : 'neutral')}>{hall.isActive ? 'Активно' : 'Отключено'}</span>
                            </div>
                            <div className="department-lines compact-lines">
                              <span><b>Предчек:</b> {hall.precheckPrinterName ?? (group.defaultPrecheckPrinterName ? 'По умолчанию · ' + group.defaultPrecheckPrinterName : 'Не настроено')}</span>
                              <span><b>Столов:</b> {hall.tables.length}</span>
                              <span><b>Порядок:</b> {hall.sortOrder}</span>
                            </div>
                          </div>
                          <div className="heading-actions">
                            <button className="secondary-button compact" onClick={() => setEditor({ kind: 'hall-edit', hall })}>Настроить зал</button>
                            <button className="primary-button compact" onClick={() => setEditor({ kind: 'table-create', hall })}>+ Стол</button>
                          </div>
                        </div>

                        <div className="group-table-grid">
                          {hall.tables.map((table) => (
                            <button
                              key={table.id}
                              className={'group-table-card ' + (!table.isActive ? 'inactive' : '')}
                              onClick={() => setEditor({ kind: 'table-edit', hall, table })}
                            >
                              <div className="group-table-card-top">
                                <span className="table-symbol">▢</span>
                                <span className={'mini-dot ' + (!table.isActive ? 'off' : '')} />
                              </div>
                              <strong>{table.name}</strong>
                              <span>{table.seats} мест</span>
                              <small>Порядок: {table.sortOrder}</small>
                            </button>
                          ))}
                          {hall.tables.length === 0 && (
                            <button className="group-table-card add-table" onClick={() => setEditor({ kind: 'table-create', hall })}>
                              <span className="plus-circle">+</span>
                              <strong>Добавить стол</strong>
                            </button>
                          )}
                        </div>
                      </article>
                    ))}
                  </div>
                  {halls.length === 0 && <div className="empty-state">В группе пока нет залов.</div>}
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
        <div><strong>Терминалы группы</strong><span>Выберите главную кассу и POS-терминалы официантов, которые работают в этой группе.</span></div>
        <button className="primary-button compact" disabled={saving} onClick={() => void onSave(ids, mainId || null)}>Сохранить</button>
      </div>
      <div className="device-group-list">
        {data.devices.filter((x) => x.isActive).map((device) => {
          const checked = ids.includes(device.id);
          return (
            <div className="device-group-row" key={device.id}>
              <label>
                <input type="checkbox" checked={checked} onChange={(e) => toggle(device.id, e.target.checked)} />
                <span><strong>{device.name}</strong><small>{device.type}</small></span>
              </label>
              <label className="main-register-radio">
                <input type="radio" name="mainRegister" checked={mainId === device.id} disabled={!checked} onChange={() => setMainId(device.id)} />
                <span>Главная касса</span>
              </label>
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
  const hall =
    editor.kind === 'hall-edit' ? editor.hall :
    editor.kind === 'table-create' ? editor.hall :
    editor.kind === 'table-edit' ? editor.hall :
    null;
  const table = editor.kind === 'table-edit' ? editor.table : null;
  const group = editor.kind === 'group-edit' ? editor.group : null;

  const [name, setName] = useState(table?.name ?? department?.name ?? hall?.name ?? group?.name ?? '');
  const [preparationPlaceTypeId, setPreparationPlaceTypeId] = useState(department?.preparationPlaceTypeId ?? '');
  const [warehouseId, setWarehouseId] = useState(department?.warehouseId ?? '');
  const [sortOrder, setSortOrder] = useState(table?.sortOrder ?? hall?.sortOrder ?? 0);
  const [seats, setSeats] = useState(table?.seats ?? 4);
  const [hallId, setHallId] = useState(table?.hallId ?? hall?.id ?? '');
  const [isActive, setIsActive] = useState(table?.isActive ?? department?.isActive ?? hall?.isActive ?? group?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const departmentMode = editor.kind === 'department-create' || editor.kind === 'department-edit';
  const hallMode = editor.kind === 'hall-create' || editor.kind === 'hall-edit';
  const tableMode = editor.kind === 'table-create' || editor.kind === 'table-edit';
  const editing = editor.kind === 'group-edit' || editor.kind === 'department-edit' || editor.kind === 'hall-edit' || editor.kind === 'table-edit';

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;

    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'group-create') {
        await createRestaurantGroup(token, name.trim());
      } else if (editor.kind === 'group-edit') {
        await updateRestaurantGroup(token, editor.group.id, { name: name.trim(), isActive });
      } else if (editor.kind === 'department-create') {
        await createRestaurantDepartment(token, editor.groupId, {
          name: name.trim(),
          preparationPlaceTypeId: preparationPlaceTypeId || null,
          warehouseId: warehouseId || null,
          printerId: null,
        });
      } else if (editor.kind === 'department-edit') {
        await updateRestaurantDepartment(token, editor.department.groupId, editor.department.id, {
          name: name.trim(),
          preparationPlaceTypeId: preparationPlaceTypeId || null,
          warehouseId: warehouseId || null,
          printerId: editor.department.printerId,
          isActive,
        });
      } else if (editor.kind === 'hall-create') {
        await createGroupHall(token, editor.groupId, {
          name: name.trim(),
          sortOrder,
          precheckPrinterId: null,
        });
      } else if (editor.kind === 'hall-edit') {
        await updateGroupHall(token, editor.hall.groupId, editor.hall.id, {
          name: name.trim(),
          sortOrder,
          precheckPrinterId: editor.hall.precheckPrinterId,
          isActive,
        });
      } else if (editor.kind === 'table-create') {
        await createTable(token, editor.hall.id, {
          name: name.trim(),
          seats,
          sortOrder,
        });
      } else if (editor.kind === 'table-edit') {
        await updateTable(token, editor.table.id, {
          hallId,
          name: name.trim(),
          seats,
          sortOrder,
          isActive,
        });
      } else if (editor.kind === 'type-create') {
        await createGroupPreparationType(token, name.trim());
      }

      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить');
    } finally {
      setSaving(false);
    }
  }

  const title =
    editor.kind === 'group-create' ? 'Новая группа' :
    editor.kind === 'group-edit' ? 'Настройки группы' :
    editor.kind === 'type-create' ? 'Новый тип места приготовления' :
    editor.kind === 'department-create' ? 'Новое отделение' :
    editor.kind === 'department-edit' ? 'Настройки отделения' :
    editor.kind === 'hall-create' ? 'Новый зал' :
    editor.kind === 'hall-edit' ? 'Настройки зала' :
    editor.kind === 'table-create' ? 'Новый стол · ' + editor.hall.name :
    'Настройки стола';

  const tableHalls = tableMode && hall
    ? data.halls.filter((x) => x.groupId === hall.groupId && x.isActive)
    : [];

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div><div className="eyebrow">СТРУКТУРА РЕСТОРАНА</div><h2>{title}</h2></div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>

        {departmentMode && (
          <>
            <label>
              <span>Тип места приготовления</span>
              <select value={preparationPlaceTypeId} onChange={(e) => setPreparationPlaceTypeId(e.target.value)}>
                <option value="">Не назначен</option>
                {data.types.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
              <small className="field-hint">Блюда с этим типом будут печататься именно в это отделение внутри выбранной группы.</small>
            </label>

            <label>
              <span>Склад списания</span>
              <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
                <option value="">Не назначен</option>
                {data.warehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>
            </label>

            <div className="full-field pos-agent-info-box">
              <strong>Принтер отделения настраивается централизованно</strong>
              <span>Откройте «Настройки ресторана → Печать», чтобы выбрать принтер кухни или бара.</span>
            </div>
          </>
        )}

        {hallMode && (
          <>
            <label>
              <span>Порядок</span>
              <input type="number" min={0} value={sortOrder} onChange={(e) => setSortOrder(Math.max(0, Number(e.target.value) || 0))} />
            </label>
            <div className="full-field pos-agent-info-box">
              <strong>Принтер предчека настраивается в разделе «Печать»</strong>
              <span>Зал может использовать общий принтер группы или собственное исключение.</span>
            </div>
          </>
        )}

        {tableMode && (
          <>
            {editor.kind === 'table-edit' && (
              <label>
                <span>Зал</span>
                <select value={hallId} onChange={(e) => setHallId(e.target.value)}>
                  {tableHalls.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
                </select>
                <small className="field-hint">Стол можно перенести только между залами той же группы.</small>
              </label>
            )}
            <label>
              <span>Количество мест</span>
              <input type="number" min={1} max={100} value={seats} onChange={(e) => setSeats(Math.min(100, Math.max(1, Number(e.target.value) || 1)))} />
            </label>
            <label>
              <span>Порядок</span>
              <input type="number" min={0} value={sortOrder} onChange={(e) => setSortOrder(Math.max(0, Number(e.target.value) || 0))} />
            </label>
          </>
        )}

        {editing && (
          <label className="toggle-row">
            <span>
              <strong>Активно</strong>
              {tableMode && <small>Стол доступен для работы на POS</small>}
            </span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button>
        </div>
      </form>
    </div>
  );
}
