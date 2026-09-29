import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeStockDocuments,
  type StockDocument,
  type StockSupplier,
  type UpsertReceiptDocumentInput,
  createReceiptDocument,
  createStockSupplier,
  getBackOfficeStockDocuments,
  postStockDocument,
  updateReceiptDocument,
  updateStockSupplier,
} from './api';
import './warehouse-documents.css';

type Tab = 'documents' | 'suppliers';

type Editor =
  | { kind: 'receipt-create' }
  | { kind: 'receipt-edit'; document: StockDocument }
  | { kind: 'supplier-create' }
  | { kind: 'supplier-edit'; supplier: StockSupplier }
  | null;

export function WarehouseDocumentsPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeStockDocuments | null>(null);
  const [tab, setTab] = useState<Tab>('documents');
  const [editor, setEditor] = useState<Editor>(null);
  const [postingId, setPostingId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeStockDocuments(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить складские документы');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  async function post(document: StockDocument) {
    if (!window.confirm(
      'Провести приходную накладную ' + document.number + '? После проведения она изменит остатки склада.',
    )) return;

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

  if (!data && loading) {
    return <div className="empty-state">Загружаем складские документы…</div>;
  }

  const documents = data?.documents ?? [];
  const suppliers = data?.suppliers ?? [];

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Складские документы</h1>
          <p>
            Документ сначала сохраняется как черновик. Остатки меняются только после проведения.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>
            Обновить
          </button>
          {canManage && tab === 'documents' && (
            <button className="primary-button" onClick={() => setEditor({ kind: 'receipt-create' })}>
              + Приходная накладная
            </button>
          )}
          {canManage && tab === 'suppliers' && (
            <button className="primary-button" onClick={() => setEditor({ kind: 'supplier-create' })}>
              + Поставщик
            </button>
          )}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="warehouse-doc-tabs">
        <button className={tab === 'documents' ? 'active' : ''} onClick={() => setTab('documents')}>
          Документы
        </button>
        <button className={tab === 'suppliers' ? 'active' : ''} onClick={() => setTab('suppliers')}>
          Поставщики
        </button>
      </div>

      {tab === 'documents' ? (
        <div className="warehouse-doc-panel">
          <div className="warehouse-doc-panel-head">
            <div>
              <strong>Документы склада</strong>
              <span>{documents.length} последних документов</span>
            </div>
          </div>

          {documents.length === 0 ? (
            <div className="warehouse-doc-empty">
              <strong>Документов пока нет</strong>
              <span>Создайте первую приходную накладную.</span>
            </div>
          ) : (
            <div className="warehouse-doc-table-wrap">
              <table className="warehouse-doc-table">
                <thead>
                  <tr>
                    <th>Дата</th>
                    <th>№</th>
                    <th>Тип</th>
                    <th>Склад</th>
                    <th>Поставщик</th>
                    <th>Позиций</th>
                    <th>Сумма</th>
                    <th>Статус</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {documents.map((document) => (
                    <tr key={document.id}>
                      <td>{formatDate(document.documentDate)}</td>
                      <td><strong>{document.number}</strong></td>
                      <td>Приходная накладная</td>
                      <td>{document.warehouseName ?? '—'}</td>
                      <td>{document.supplierName ?? '—'}</td>
                      <td>{document.lines.length}</td>
                      <td><strong>{money(document.totalAmount)}</strong></td>
                      <td>
                        <span className={'badge ' + (document.status === 'POSTED' ? 'success' : document.status === 'DRAFT' ? 'neutral' : '')}>
                          {statusLabel(document.status)}
                        </span>
                      </td>
                      <td>
                        <div className="warehouse-doc-actions">
                          {canManage && document.status === 'DRAFT' && (
                            <>
                              <button className="text-button" onClick={() => setEditor({ kind: 'receipt-edit', document })}>
                                Открыть
                              </button>
                              <button
                                className="primary-button compact"
                                disabled={postingId === document.id}
                                onClick={() => void post(document)}
                              >
                                {postingId === document.id ? 'Проводим…' : 'Провести'}
                              </button>
                            </>
                          )}
                          {document.status === 'POSTED' && document.postedAt && (
                            <small>{formatDateTime(document.postedAt)}</small>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : (
        <div className="warehouse-doc-panel">
          <div className="warehouse-doc-panel-head">
            <div>
              <strong>Поставщики</strong>
              <span>Внешние поставщики и внутренние подразделения.</span>
            </div>
          </div>

          <div className="supplier-grid">
            {suppliers.map((supplier) => (
              <button
                key={supplier.id}
                className={'supplier-card ' + (!supplier.isActive ? 'inactive' : '')}
                onClick={() => canManage && setEditor({ kind: 'supplier-edit', supplier })}
                disabled={!canManage}
              >
                <span>{supplier.type === 'INTERNAL' ? 'Внутренний' : 'Внешний'}</span>
                <strong>{supplier.name}</strong>
                <small>{supplier.taxId ? 'VÖEN: ' + supplier.taxId : 'VÖEN не указан'}</small>
                <small>{supplier.phone || 'Телефон не указан'}</small>
                <b>{supplier.isActive ? 'Активен' : 'Отключён'}</b>
              </button>
            ))}
            {suppliers.length === 0 && (
              <div className="warehouse-doc-empty">
                <strong>Поставщиков пока нет</strong>
                <span>Добавьте поставщика перед созданием приходной накладной.</span>
              </div>
            )}
          </div>
        </div>
      )}

      {editor && data && canManage && (
        editor.kind === 'receipt-create' || editor.kind === 'receipt-edit' ? (
          <ReceiptEditor
            token={token}
            data={data}
            document={editor.kind === 'receipt-edit' ? editor.document : null}
            onClose={() => setEditor(null)}
            onSaved={async () => {
              setEditor(null);
              await refresh();
            }}
          />
        ) : (
          <SupplierEditor
            token={token}
            supplier={editor.kind === 'supplier-edit' ? editor.supplier : null}
            onClose={() => setEditor(null)}
            onSaved={async () => {
              setEditor(null);
              await refresh();
            }}
          />
        )
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
  const [documentDate, setDocumentDate] = useState(
    document ? dateInputValue(document.documentDate) : dateInputValue(new Date().toISOString()),
  );
  const [warehouseId, setWarehouseId] = useState(document?.warehouseId ?? activeWarehouses[0]?.id ?? '');
  const [supplierId, setSupplierId] = useState(document?.supplierId ?? activeSuppliers[0]?.id ?? '');
  const [comment, setComment] = useState(document?.comment ?? '');
  const [lines, setLines] = useState<Array<{ productId: string; quantity: string; unitPrice: string }>>(
    document?.lines.length
      ? document.lines.map((line) => ({
          productId: line.productId,
          quantity: String(line.quantity),
          unitPrice: String(line.unitPrice),
        }))
      : [{ productId: activeItems[0]?.id ?? '', quantity: '', unitPrice: '' }],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const total = useMemo(
    () => lines.reduce((sum, line) => {
      const quantity = decimal(line.quantity);
      const price = decimal(line.unitPrice);
      return sum + (Number.isFinite(quantity) ? quantity : 0) * (Number.isFinite(price) ? price : 0);
    }, 0),
    [lines],
  );

  function patchLine(index: number, patch: Partial<{ productId: string; quantity: string; unitPrice: string }>) {
    setLines((current) => current.map((line, lineIndex) =>
      lineIndex === index ? { ...line, ...patch } : line));
  }

  function addLine() {
    setLines((current) => [...current, { productId: '', quantity: '', unitPrice: '' }]);
  }

  function removeLine(index: number) {
    setLines((current) => current.filter((_, lineIndex) => lineIndex !== index));
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null;
    const shouldPost = submitter?.value === 'post';

    const parsedLines = lines
      .filter((line) => line.productId)
      .map((line) => ({
        productId: line.productId,
        quantity: decimal(line.quantity),
        unitPrice: decimal(line.unitPrice),
      }));

    if (
      !warehouseId ||
      !supplierId ||
      parsedLines.length === 0 ||
      parsedLines.some((line) =>
        !Number.isFinite(line.quantity) ||
        line.quantity <= 0 ||
        !Number.isFinite(line.unitPrice) ||
        line.unitPrice < 0)
    ) {
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

    setSaving(true);
    setError(null);
    try {
      const saved = document
        ? await updateReceiptDocument(token, document.id, input)
        : await createReceiptDocument(token, input);

      if (shouldPost) {
        await postStockDocument(token, saved.id);
      }

      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить приходную накладную');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card receipt-document-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">СКЛАДСКИЙ ДОКУМЕНТ</div>
            <h2>{document ? 'Приходная накладная ' + document.number : 'Новая приходная накладная'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="receipt-document-grid">
          <label>
            <span>Номер документа</span>
            <input value={number} onChange={(e) => setNumber(e.target.value)} placeholder="Сформируется автоматически" />
          </label>
          <label>
            <span>Дата</span>
            <input type="date" value={documentDate} onChange={(e) => setDocumentDate(e.target.value)} />
          </label>
          <label>
            <span>Склад</span>
            <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
              <option value="">Выберите склад</option>
              {activeWarehouses.map((warehouse) => (
                <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Поставщик</span>
            <select value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
              <option value="">Выберите поставщика</option>
              {activeSuppliers.map((supplier) => (
                <option key={supplier.id} value={supplier.id}>{supplier.name}</option>
              ))}
            </select>
          </label>
        </div>

        <div className="receipt-lines">
          <div className="receipt-lines-head">
            <div>
              <strong>Позиции</strong>
              <span>Количество и закупочная цена</span>
            </div>
            <button type="button" className="secondary-button compact" onClick={addLine}>+ Строка</button>
          </div>

          <div className="receipt-line receipt-line-header">
            <span>Номенклатура</span>
            <span>Количество</span>
            <span>Цена</span>
            <span>Сумма</span>
            <span />
          </div>

          {lines.map((line, index) => {
            const item = activeItems.find((x) => x.id === line.productId);
            const quantity = decimal(line.quantity);
            const unitPrice = decimal(line.unitPrice);
            const amount =
              Number.isFinite(quantity) && Number.isFinite(unitPrice)
                ? quantity * unitPrice
                : 0;

            return (
              <div className="receipt-line" key={index}>
                <select value={line.productId} onChange={(e) => patchLine(index, { productId: e.target.value })}>
                  <option value="">Выберите позицию</option>
                  {activeItems.map((product) => (
                    <option key={product.id} value={product.id}>
                      {product.name}{product.sku ? ' · ' + product.sku : ''}
                    </option>
                  ))}
                </select>
                <div className="receipt-number-input">
                  <input
                    value={line.quantity}
                    onChange={(e) => patchLine(index, { quantity: e.target.value })}
                    inputMode="decimal"
                    placeholder="0"
                  />
                  <small>{item?.unit ?? ''}</small>
                </div>
                <input
                  value={line.unitPrice}
                  onChange={(e) => patchLine(index, { unitPrice: e.target.value })}
                  inputMode="decimal"
                  placeholder="0.00"
                />
                <strong>{money(amount)}</strong>
                <button type="button" className="text-button" onClick={() => removeLine(index)} disabled={lines.length === 1}>
                  Удалить
                </button>
              </div>
            );
          })}

          <div className="receipt-total">
            <span>Итого</span>
            <strong>{money(total)}</strong>
          </div>
        </div>

        <label>
          <span>Комментарий</span>
          <input
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            maxLength={500}
            placeholder="Например: накладная поставщика №45"
          />
        </label>

        {activeSuppliers.length === 0 && (
          <div className="error-box">Сначала создайте хотя бы одного активного поставщика.</div>
        )}
        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions receipt-modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button
            type="submit"
            className="secondary-button"
            disabled={saving || !warehouseId || !supplierId || activeSuppliers.length === 0}
            value="draft"
          >
            {saving ? 'Сохраняем…' : 'Сохранить черновик'}
          </button>
          <button
            type="submit"
            className="primary-button"
            disabled={saving || !warehouseId || !supplierId || activeSuppliers.length === 0}
            value="post"
          >
            {saving ? 'Проводим…' : 'Сохранить и провести'}
          </button>
        </div>
      </form>
    </div>
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
          <div>
            <div className="eyebrow">ПОСТАВЩИК</div>
            <h2>{supplier ? 'Настройки поставщика' : 'Новый поставщик'}</h2>
          </div>
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
          <button className="primary-button" disabled={saving || !name.trim()}>
            {saving ? 'Сохраняем…' : 'Сохранить'}
          </button>
        </div>
      </form>
    </div>
  );
}

function decimal(value: string) {
  return Number(value.replace(',', '.'));
}

function statusLabel(status: StockDocument['status']) {
  return status === 'POSTED' ? 'Проведён' : status === 'CANCELLED' ? 'Отменён' : 'Черновик';
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼';
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value));
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));
}

function dateInputValue(value: string) {
  const date = new Date(value);
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
}
