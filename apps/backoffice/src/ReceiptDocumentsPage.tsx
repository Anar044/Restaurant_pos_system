import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeStockDocuments,
  type StockDocument,
  type UpsertReceiptDocumentInput,
  createReceiptDocument,
  getBackOfficeStockDocuments,
  postStockDocument,
  updateReceiptDocument,
} from './api';
import './warehouse-documents.css';

export function ReceiptDocumentsPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeStockDocuments | null>(null);
  const [editor, setEditor] = useState<StockDocument | 'new' | null>(null);
  const [postingId, setPostingId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeStockDocuments(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить приходные накладные');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  async function post(document: StockDocument) {
    if (!window.confirm('Провести приходную накладную ' + document.number + '? После проведения остатки и стоимость склада изменятся.')) return;
    setPostingId(document.id);
    setError(null);
    try {
      await postStockDocument(token, document.id);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести документ');
    } finally {
      setPostingId(null);
    }
  }

  if (!data && loading) return <div className="empty-state">Загружаем приходные накладные…</div>;

  const documents = data?.documents.filter((x) => x.type === 'RECEIPT') ?? [];

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Приходные накладные</h1>
          <p>Поступление товара от поставщика: склад, количество, закупочная цена и проведение документа.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditor('new')}>+ Приходная накладная</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="warehouse-doc-panel">
        {documents.length === 0 ? (
          <div className="warehouse-doc-empty"><strong>Приходных накладных пока нет</strong><span>Создайте первый документ поступления.</span></div>
        ) : (
          <div className="warehouse-doc-table-wrap">
            <table className="warehouse-doc-table">
              <thead>
                <tr><th>Дата</th><th>№</th><th>Поставщик</th><th>Склад</th><th>Позиций</th><th>Сумма</th><th>Статус</th><th /></tr>
              </thead>
              <tbody>
                {documents.map((document) => (
                  <tr key={document.id}>
                    <td>{formatDate(document.documentDate)}</td>
                    <td><strong>{document.number}</strong></td>
                    <td>{document.supplierName ?? '—'}</td>
                    <td>{document.warehouseName ?? '—'}</td>
                    <td>{document.lines.length}</td>
                    <td><strong>{money(document.totalAmount)}</strong></td>
                    <td><span className={'badge ' + (document.status === 'POSTED' ? 'success' : 'neutral')}>{statusLabel(document.status)}</span></td>
                    <td>
                      <div className="warehouse-doc-actions">
                        {document.status === 'DRAFT' && canManage && (
                          <>
                            <button className="text-button" onClick={() => setEditor(document)}>Открыть</button>
                            <button className="primary-button compact" disabled={postingId === document.id} onClick={() => void post(document)}>
                              {postingId === document.id ? 'Проводим…' : 'Провести'}
                            </button>
                          </>
                        )}
                        {document.status === 'POSTED' && document.postedAt && <small>{formatDateTime(document.postedAt)}</small>}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {editor && data && canManage && (
        <ReceiptEditor
          token={token}
          data={data}
          document={editor === 'new' ? null : editor}
          onClose={() => setEditor(null)}
          onSaved={async () => { setEditor(null); await refresh(); }}
        />
      )}
    </section>
  );
}

function ReceiptEditor({
  token,
  data,
  document,
  onClose,
  onSaved,
}: {
  token: string;
  data: BackOfficeStockDocuments;
  document: StockDocument | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const activeWarehouses = data.warehouses.filter((x) => x.isActive);
  const activeSuppliers = data.suppliers.filter((x) => x.isActive);
  const activeItems = data.items.filter((x) => x.isActive);

  const [number, setNumber] = useState(document?.number ?? '');
  const [documentDate, setDocumentDate] = useState(document ? dateInputValue(document.documentDate) : dateInputValue(new Date().toISOString()));
  const [warehouseId, setWarehouseId] = useState(document?.warehouseId ?? activeWarehouses[0]?.id ?? '');
  const [supplierId, setSupplierId] = useState(document?.supplierId ?? activeSuppliers[0]?.id ?? '');
  const [comment, setComment] = useState(document?.comment ?? '');
  const [lines, setLines] = useState<Array<{ productId: string; quantity: string; unitPrice: string }>>(
    document?.lines.length
      ? document.lines.map((line) => ({ productId: line.productId, quantity: String(line.quantity), unitPrice: String(line.unitPrice) }))
      : [{ productId: activeItems[0]?.id ?? '', quantity: '', unitPrice: '' }],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const total = useMemo(() => lines.reduce((sum, line) => {
    const quantity = decimal(line.quantity);
    const price = decimal(line.unitPrice);
    return sum + (Number.isFinite(quantity) ? quantity : 0) * (Number.isFinite(price) ? price : 0);
  }, 0), [lines]);

  function patchLine(index: number, patch: Partial<{ productId: string; quantity: string; unitPrice: string }>) {
    setLines((current) => current.map((line, i) => i === index ? { ...line, ...patch } : line));
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null;
    const shouldPost = submitter?.value === 'post';
    const parsedLines = lines.filter((x) => x.productId).map((x) => ({
      productId: x.productId,
      quantity: decimal(x.quantity),
      unitPrice: decimal(x.unitPrice),
    }));

    if (!warehouseId || !supplierId || !parsedLines.length || parsedLines.some((x) => !Number.isFinite(x.quantity) || x.quantity <= 0 || !Number.isFinite(x.unitPrice) || x.unitPrice < 0)) {
      setError('Выберите склад, поставщика и заполните количество/цену во всех строках.');
      return;
    }

    const input: UpsertReceiptDocumentInput = {
      number: number.trim() || null,
      documentDate: documentDate ? new Date(documentDate + 'T12:00:00').toISOString() : null,
      warehouseId,
      supplierId,
      comment: comment.trim() || null,
      lines: parsedLines,
    };

    setSaving(true); setError(null);
    try {
      const saved = document
        ? await updateReceiptDocument(token, document.id, input)
        : await createReceiptDocument(token, input);
      if (shouldPost) await postStockDocument(token, saved.id);
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить приходную накладную');
    } finally { setSaving(false); }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card receipt-document-modal" onSubmit={submit}>
        <div className="modal-header">
          <div><div className="eyebrow">ПРИХОДНАЯ НАКЛАДНАЯ</div><h2>{document ? document.number : 'Новый документ'}</h2></div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="receipt-document-grid">
          <label><span>Номер документа</span><input value={number} onChange={(e) => setNumber(e.target.value)} placeholder="Сформируется автоматически" /></label>
          <label><span>Дата</span><input type="date" value={documentDate} onChange={(e) => setDocumentDate(e.target.value)} /></label>
          <label><span>Склад</span><select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}><option value="">Выберите склад</option>{activeWarehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label><span>Поставщик</span><select value={supplierId} onChange={(e) => setSupplierId(e.target.value)}><option value="">Выберите поставщика</option>{activeSuppliers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        </div>

        <div className="receipt-lines">
          <div className="receipt-lines-head"><div><strong>Позиции</strong><span>Количество и закупочная цена</span></div><button type="button" className="secondary-button compact" onClick={() => setLines((x) => [...x, { productId: '', quantity: '', unitPrice: '' }])}>+ Строка</button></div>
          <div className="receipt-line receipt-line-header"><span>Номенклатура</span><span>Количество</span><span>Цена</span><span>Сумма</span><span /></div>
          {lines.map((line, index) => {
            const item = activeItems.find((x) => x.id === line.productId);
            const quantity = decimal(line.quantity);
            const unitPrice = decimal(line.unitPrice);
            const amount = Number.isFinite(quantity) && Number.isFinite(unitPrice) ? quantity * unitPrice : 0;
            return (
              <div className="receipt-line" key={index}>
                <select value={line.productId} onChange={(e) => patchLine(index, { productId: e.target.value })}><option value="">Выберите позицию</option>{activeItems.map((x) => <option key={x.id} value={x.id}>{x.name}{x.sku ? ' · ' + x.sku : ''}</option>)}</select>
                <div className="receipt-number-input"><input value={line.quantity} onChange={(e) => patchLine(index, { quantity: e.target.value })} inputMode="decimal" placeholder="0" /><small>{item?.unit ?? ''}</small></div>
                <input value={line.unitPrice} onChange={(e) => patchLine(index, { unitPrice: e.target.value })} inputMode="decimal" placeholder="0.00" />
                <strong>{money(amount)}</strong>
                <button type="button" className="text-button" onClick={() => setLines((x) => x.filter((_, i) => i !== index))} disabled={lines.length === 1}>Удалить</button>
              </div>
            );
          })}
          <div className="receipt-total"><span>Итого</span><strong>{money(total)}</strong></div>
        </div>

        <label><span>Комментарий</span><input value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} /></label>
        {activeSuppliers.length === 0 && <div className="error-box">Сначала создайте поставщика в отдельном разделе «Поставщики».</div>}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions receipt-modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button type="submit" className="secondary-button" value="draft" disabled={saving || !warehouseId || !supplierId}>{saving ? 'Сохраняем…' : 'Сохранить черновик'}</button>
          <button type="submit" className="primary-button" value="post" disabled={saving || !warehouseId || !supplierId}>{saving ? 'Проводим…' : 'Сохранить и провести'}</button>
        </div>
      </form>
    </div>
  );
}

function decimal(value: string) { return Number(value.replace(',', '.')); }
function statusLabel(status: StockDocument['status']) { return status === 'POSTED' ? 'Проведён' : status === 'CANCELLED' ? 'Отменён' : 'Черновик'; }
function money(value: number) { return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼'; }
function formatDate(value: string) { return new Intl.DateTimeFormat('ru-RU').format(new Date(value)); }
function formatDateTime(value: string) { return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)); }
function dateInputValue(value: string) { const date = new Date(value); const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000); return local.toISOString().slice(0, 10); }
