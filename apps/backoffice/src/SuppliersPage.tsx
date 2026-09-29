import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeStockDocuments,
  type StockSupplier,
  createStockSupplier,
  getBackOfficeStockDocuments,
  updateStockSupplier,
} from './api';
import './suppliers.css';

type Tab = 'list' | 'balance' | 'documents';

export function SuppliersPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeStockDocuments | null>(null);
  const [tab, setTab] = useState<Tab>('list');
  const [editor, setEditor] = useState<StockSupplier | 'new' | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeStockDocuments(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить поставщиков');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const supplierRows = useMemo(() => {
    const suppliers = data?.suppliers ?? [];
    const documents = data?.documents ?? [];
    return suppliers.map((supplier) => {
      const posted = documents.filter(
        (document) => document.supplierId === supplier.id && document.status === 'POSTED',
      );
      const drafts = documents.filter(
        (document) => document.supplierId === supplier.id && document.status === 'DRAFT',
      );
      const lastDocument = posted
        .slice()
        .sort((a, b) => new Date(b.documentDate).getTime() - new Date(a.documentDate).getTime())[0];
      return {
        supplier,
        postedAmount: posted.reduce((sum, document) => sum + document.totalAmount, 0),
        postedCount: posted.length,
        draftCount: drafts.length,
        lastDocument,
      };
    });
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем поставщиков…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">КОНТРАГЕНТЫ</div>
          <h1>Поставщики</h1>
          <p>Карточки поставщиков, их поставки и взаиморасчёты собраны в одном отдельном разделе.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && tab === 'list' && (
            <button className="primary-button" onClick={() => setEditor('new')}>+ Поставщик</button>
          )}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="supplier-tabs">
        <button className={tab === 'list' ? 'active' : ''} onClick={() => setTab('list')}>Список поставщиков</button>
        <button className={tab === 'balance' ? 'active' : ''} onClick={() => setTab('balance')}>Баланс по поставщикам</button>
        <button className={tab === 'documents' ? 'active' : ''} onClick={() => setTab('documents')}>Документы поставщиков</button>
      </div>

      {tab === 'list' && (
        <div className="supplier-panel">
          <div className="supplier-list-grid">
            {supplierRows.map(({ supplier, postedAmount, postedCount, draftCount, lastDocument }) => (
              <button
                key={supplier.id}
                className={'supplier-main-card ' + (!supplier.isActive ? 'inactive' : '')}
                onClick={() => canManage && setEditor(supplier)}
                disabled={!canManage}
              >
                <div className="supplier-main-card-head">
                  <span>{supplier.type === 'INTERNAL' ? 'Внутренний' : 'Внешний'}</span>
                  <span className={'badge ' + (supplier.isActive ? 'success' : 'neutral')}>
                    {supplier.isActive ? 'Активен' : 'Отключён'}
                  </span>
                </div>
                <strong>{supplier.name}</strong>
                <small>{supplier.taxId ? 'VÖEN: ' + supplier.taxId : 'VÖEN не указан'}</small>
                <small>{supplier.phone || 'Телефон не указан'}</small>
                <div className="supplier-main-stats">
                  <span><b>{postedCount}</b> проведено</span>
                  <span><b>{draftCount}</b> черновиков</span>
                  <span><b>{money(postedAmount)}</b> поставки</span>
                </div>
                <small className="supplier-last">
                  {lastDocument ? 'Последняя поставка: ' + date(lastDocument.documentDate) : 'Поставок пока нет'}
                </small>
              </button>
            ))}
            {supplierRows.length === 0 && <div className="supplier-empty">Поставщиков пока нет.</div>}
          </div>
        </div>
      )}

      {tab === 'balance' && (
        <div className="supplier-panel">
          <div className="supplier-balance-note">
            <strong>Баланс по поставщикам</strong>
            <span>
              Сейчас здесь отражена задолженность, сформированная проведёнными приходными накладными.
              Оплаты поставщикам подключим к этому же экрану после перехода финансов на план счетов.
            </span>
          </div>
          <div className="supplier-table-wrap">
            <table className="supplier-table">
              <thead>
                <tr>
                  <th>Поставщик</th>
                  <th>Проведено накладных</th>
                  <th>Сумма поставок</th>
                  <th>Учтённые оплаты</th>
                  <th>Текущая задолженность*</th>
                  <th>Последняя поставка</th>
                </tr>
              </thead>
              <tbody>
                {supplierRows.map(({ supplier, postedAmount, postedCount, lastDocument }) => (
                  <tr key={supplier.id}>
                    <td><strong>{supplier.name}</strong></td>
                    <td>{postedCount}</td>
                    <td>{money(postedAmount)}</td>
                    <td>—</td>
                    <td><strong>{money(postedAmount)}</strong></td>
                    <td>{lastDocument ? date(lastDocument.documentDate) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <small className="supplier-footnote">* Пока оплаты поставщикам не подключены, показатель равен сумме проведённых приходных накладных.</small>
        </div>
      )}

      {tab === 'documents' && (
        <div className="supplier-panel">
          <div className="supplier-table-wrap">
            <table className="supplier-table">
              <thead>
                <tr>
                  <th>Дата</th>
                  <th>№</th>
                  <th>Поставщик</th>
                  <th>Склад</th>
                  <th>Сумма</th>
                  <th>Статус</th>
                </tr>
              </thead>
              <tbody>
                {(data?.documents ?? []).map((document) => (
                  <tr key={document.id}>
                    <td>{date(document.documentDate)}</td>
                    <td><strong>{document.number}</strong></td>
                    <td>{document.supplierName ?? '—'}</td>
                    <td>{document.warehouseName ?? '—'}</td>
                    <td>{money(document.totalAmount)}</td>
                    <td>
                      <span className={'badge ' + (document.status === 'POSTED' ? 'success' : 'neutral')}>
                        {document.status === 'POSTED' ? 'Проведён' : document.status === 'DRAFT' ? 'Черновик' : 'Отменён'}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {editor && canManage && (
        <SupplierEditor
          token={token}
          supplier={editor === 'new' ? null : editor}
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

function SupplierEditor({
  token,
  supplier,
  onClose,
  onSaved,
}: {
  token: string;
  supplier: StockSupplier | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [name, setName] = useState(supplier?.name ?? '');
  const [type, setType] = useState<'EXTERNAL' | 'INTERNAL'>(supplier?.type ?? 'EXTERNAL');
  const [taxId, setTaxId] = useState(supplier?.taxId ?? '');
  const [phone, setPhone] = useState(supplier?.phone ?? '');
  const [isActive, setIsActive] = useState(supplier?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;

    setSaving(true);
    setError(null);
    try {
      if (supplier) {
        await updateStockSupplier(token, supplier.id, {
          name: name.trim(),
          type,
          taxId: taxId.trim() || null,
          phone: phone.trim() || null,
          isActive,
        });
      } else {
        await createStockSupplier(token, {
          name: name.trim(),
          type,
          taxId: taxId.trim() || null,
          phone: phone.trim() || null,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить поставщика');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div><div className="eyebrow">ПОСТАВЩИК</div><h2>{supplier ? 'Карточка поставщика' : 'Новый поставщик'}</h2></div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>
        <label>
          <span>Тип</span>
          <select value={type} onChange={(e) => setType(e.target.value as 'EXTERNAL' | 'INTERNAL')}>
            <option value="EXTERNAL">Внешний поставщик</option>
            <option value="INTERNAL">Внутренний поставщик</option>
          </select>
        </label>
        <div className="form-grid">
          <label><span>VÖEN / ИНН</span><input value={taxId} onChange={(e) => setTaxId(e.target.value)} /></label>
          <label><span>Телефон</span><input value={phone} onChange={(e) => setPhone(e.target.value)} /></label>
        </div>
        {supplier && (
          <label className="toggle-row">
            <span><strong>Активен</strong><small>Отключённого поставщика нельзя выбрать в новой накладной.</small></span>
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

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼';
}

function date(value: string) {
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value));
}
