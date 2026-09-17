import { FormEvent, useMemo, useState } from 'react';
import {
  type DiningTable,
  type Hall,
  createHall,
  createTable,
  updateHall,
  updateTable,
} from './api';

type EditorState =
  | { kind: 'hall-create' }
  | { kind: 'hall-edit'; hall: Hall }
  | { kind: 'table-create'; hall: Hall }
  | { kind: 'table-edit'; hall: Hall; table: DiningTable }
  | null;

export function HallsPage({
  halls,
  loading,
  token,
  onRefresh,
}: {
  halls: Hall[];
  loading: boolean;
  token: string;
  onRefresh: () => Promise<void>;
}) {
  const [editor, setEditor] = useState<EditorState>(null);

  const stats = useMemo(() => {
    const tables = halls.flatMap((hall) => hall.tables);
    return {
      halls: halls.filter((x) => x.isActive).length,
      tables: tables.length,
      activeTables: tables.filter((x) => x.isActive).length,
      seats: tables.filter((x) => x.isActive).reduce((sum, table) => sum + table.seats, 0),
    };
  }, [halls]);

  return (
    <>
      <section>
        <div className="page-heading">
          <div>
            <div className="eyebrow">СТРУКТУРА РЕСТОРАНА</div>
            <h1>Залы и столы</h1>
            <p>Настройте залы, столы, количество мест и порядок отображения на кассе.</p>
          </div>
          <div className="heading-actions">
            <button className="secondary-button" onClick={() => void onRefresh()} disabled={loading}>Обновить</button>
            <button className="primary-button" onClick={() => setEditor({ kind: 'hall-create' })}>+ Добавить зал</button>
          </div>
        </div>

        <div className="stats-grid">
          <StatCard label="Активные залы" value={stats.halls} detail={`${halls.length} всего`} />
          <StatCard label="Столы" value={stats.activeTables} detail={`${stats.tables} всего`} />
          <StatCard label="Посадочных мест" value={stats.seats} detail="в активных столах" />
        </div>

        {loading && halls.length === 0 ? (
          <div className="empty-state">Загружаем структуру ресторана…</div>
        ) : halls.length === 0 ? (
          <div className="empty-state">
            <strong>Залов пока нет</strong>
            <span>Создайте первый зал, затем добавьте в него столы.</span>
            <button className="primary-button" onClick={() => setEditor({ kind: 'hall-create' })}>Создать зал</button>
          </div>
        ) : (
          <div className="hall-list">
            {halls.map((hall) => (
              <article className={`hall-card ${!hall.isActive ? 'inactive-card' : ''}`} key={hall.id}>
                <div className="hall-header">
                  <div>
                    <div className="title-row">
                      <h2>{hall.name}</h2>
                      <span className={`badge ${hall.isActive ? 'success' : 'neutral'}`}>
                        {hall.isActive ? 'Активен' : 'Отключён'}
                      </span>
                    </div>
                    <p>{hall.tables.length} столов · порядок {hall.sortOrder}</p>
                  </div>
                  <div className="hall-actions">
                    <button className="text-button" onClick={() => setEditor({ kind: 'hall-edit', hall })}>Настроить</button>
                    <button className="primary-button compact" onClick={() => setEditor({ kind: 'table-create', hall })}>+ Стол</button>
                  </div>
                </div>

                <div className="table-grid">
                  {hall.tables.map((table) => (
                    <button
                      className={`table-card ${!table.isActive ? 'inactive' : ''}`}
                      key={table.id}
                      onClick={() => setEditor({ kind: 'table-edit', hall, table })}
                    >
                      <div className="table-card-top">
                        <span className="table-symbol">▢</span>
                        <span className={`mini-dot ${table.isActive ? '' : 'off'}`} />
                      </div>
                      <strong>{table.name}</strong>
                      <span>{table.seats} мест</span>
                      <small>Порядок: {table.sortOrder}</small>
                    </button>
                  ))}
                  {hall.tables.length === 0 && (
                    <button className="table-card add-table" onClick={() => setEditor({ kind: 'table-create', hall })}>
                      <span className="plus-circle">+</span>
                      <strong>Добавить стол</strong>
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      {editor && (
        <EditorModal
          editor={editor}
          halls={halls}
          token={token}
          onClose={() => setEditor(null)}
          onSaved={async () => {
            setEditor(null);
            await onRefresh();
          }}
        />
      )}
    </>
  );
}

function StatCard({ label, value, detail }: { label: string; value: number; detail: string }) {
  return (
    <div className="stat-card">
      <span>{label}</span>
      <div>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </div>
  );
}

function EditorModal({
  editor,
  halls,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<EditorState, null>;
  halls: Hall[];
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const isHall = editor.kind === 'hall-create' || editor.kind === 'hall-edit';
  const editing = editor.kind === 'hall-edit' || editor.kind === 'table-edit';

  let currentHall: Hall | null = null;
  if (editor.kind === 'hall-edit' || editor.kind === 'table-create' || editor.kind === 'table-edit') {
    currentHall = editor.hall;
  }
  const currentTable = editor.kind === 'table-edit' ? editor.table : null;

  const [name, setName] = useState(currentTable?.name ?? currentHall?.name ?? '');
  const [sortOrder, setSortOrder] = useState(currentTable?.sortOrder ?? currentHall?.sortOrder ?? 0);
  const [seats, setSeats] = useState(currentTable?.seats ?? 4);
  const [hallId, setHallId] = useState(currentTable?.hallId ?? currentHall?.id ?? halls[0]?.id ?? '');
  const [isActive, setIsActive] = useState(currentTable?.isActive ?? currentHall?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const title =
    editor.kind === 'hall-create' ? 'Новый зал' :
    editor.kind === 'hall-edit' ? 'Настройки зала' :
    editor.kind === 'table-create' ? `Новый стол · ${editor.hall.name}` :
    'Настройки стола';

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'hall-create') {
        await createHall(token, { name: name.trim(), sortOrder });
      } else if (editor.kind === 'hall-edit') {
        await updateHall(token, editor.hall.id, { name: name.trim(), sortOrder, isActive });
      } else if (editor.kind === 'table-create') {
        await createTable(token, editor.hall.id, { name: name.trim(), seats, sortOrder });
      } else {
        await updateTable(token, editor.table.id, {
          hallId,
          name: name.trim(),
          seats,
          sortOrder,
          isActive,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить изменения');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">{isHall ? 'ЗАЛ' : 'СТОЛ'}</div>
            <h2>{title}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="form-grid">
          <label className="full-field">
            <span>Название</span>
            <input value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus />
          </label>

          {!isHall && editing && (
            <label className="full-field">
              <span>Зал</span>
              <select value={hallId} onChange={(e) => setHallId(e.target.value)}>
                {halls.map((hall) => <option value={hall.id} key={hall.id}>{hall.name}</option>)}
              </select>
            </label>
          )}

          {!isHall && (
            <label>
              <span>Количество мест</span>
              <input type="number" min={1} max={100} value={seats} onChange={(e) => setSeats(Number(e.target.value))} />
            </label>
          )}

          <label>
            <span>Порядок</span>
            <input type="number" min={0} value={sortOrder} onChange={(e) => setSortOrder(Number(e.target.value))} />
          </label>
        </div>

        {editing && (
          <label className="toggle-row">
            <span>
              <strong>Активен</strong>
              <small>{isHall ? 'Показывать зал на кассе' : 'Стол доступен для работы'}</small>
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
