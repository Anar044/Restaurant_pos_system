import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeStockDocuments,
  type StockDocument,
  type UpsertReceiptDocumentInput,
  createReceiptDocument,
  deleteReceiptDocument,
  duplicateReceiptDocument,
  getBackOfficeStockDocuments,
  postStockDocument,
  reverseReceiptDocument,
  updateReceiptDocument,
} from './api';
import './warehouse-documents.css';

type ReceiptStatusFilter = 'ALL' | 'DRAFT' | 'POSTED' | 'CANCELLED';

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
  const [actionId, setActionId] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<ReceiptStatusFilter>('ALL');
  const [search, setSearch] = useState('');
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
    if (!window.confirm(
      'Провести приходную накладную ' + document.number +
      '? После проведения остатки, стоимость склада и бухгалтерские проводки изменятся.',
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

  async function reloadDocuments(openDocumentId?: string) {
    const next = await getBackOfficeStockDocuments(token);
    setData(next);

    if (openDocumentId) {
      const document = next.documents.find((x) => x.id === openDocumentId);
      if (!document) {
        throw new Error('Документ создан, но не найден после обновления журнала. Нажмите «Обновить» и повторите открытие.');
      }

      setStatusFilter('DRAFT');
      setSearch('');
      setEditor(document);
    }
  }

  async function removeDraft(document: StockDocument) {
    if (!window.confirm(
      'Удалить черновик ' + document.number + '? Это действие нельзя отменить.',
    )) return;

    setActionId('delete:' + document.id);
    setError(null);
    try {
      await deleteReceiptDocument(token, document.id);
      if (editor !== 'new' && editor?.id === document.id) setEditor(null);
      await reloadDocuments();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось удалить черновик');
    } finally {
      setActionId(null);
    }
  }

  async function duplicateDocument(document: StockDocument) {
    setActionId('duplicate:' + document.id);
    setError(null);
    try {
      const result = await duplicateReceiptDocument(token, document.id);
      setEditor(null);
      await reloadDocuments(result.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось дублировать документ');
    } finally {
      setActionId(null);
    }
  }

  async function reverseDocument(document: StockDocument, createCorrectionDraft: boolean) {
    const defaultReason = createCorrectionDraft
      ? 'Исправление приходной накладной'
      : 'Отмена приходной накладной';
    const reason = window.prompt(
      createCorrectionDraft
        ? 'Укажите причину исправления. Исходный документ будет сторнирован, а новая копия откроется как черновик.'
        : 'Укажите причину сторно. Складские движения и бухгалтерские проводки будут отменены.',
      defaultReason,
    );
    if (reason === null) return;

    if (!window.confirm(
      createCorrectionDraft
        ? 'Сторнировать ' + document.number + ' и создать исправленную копию?'
        : 'Сторнировать ' + document.number + '? Это создаст обратные складские и бухгалтерские движения.',
    )) return;

    setActionId((createCorrectionDraft ? 'correct:' : 'reverse:') + document.id);
    setError(null);
    try {
      const result = await reverseReceiptDocument(token, document.id, {
        createCorrectionDraft,
        reason: reason.trim() || null,
      });
      await reloadDocuments(result.correctionDocumentId ?? undefined);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сторнировать документ');
    } finally {
      setActionId(null);
    }
  }

  const documents = useMemo(() => {
    const source = data?.documents.filter((x) => x.type === 'RECEIPT') ?? [];
    const query = search.trim().toLowerCase();

    return source.filter((document) => {
      if (statusFilter !== 'ALL' && document.status !== statusFilter) return false;
      if (!query) return true;

      return [
        document.number,
        document.purchaseReferenceNumber,
        document.eInvoiceNumber,
        document.supplierName,
        document.warehouseName,
        purchaseDocumentKindLabel(document.purchaseDocumentKind),
      ].some((value) => value?.toLowerCase().includes(query));
    });
  }, [data, search, statusFilter]);

  const counts = useMemo(() => {
    const source = data?.documents.filter((x) => x.type === 'RECEIPT') ?? [];
    return {
      ALL: source.length,
      DRAFT: source.filter((x) => x.status === 'DRAFT').length,
      POSTED: source.filter((x) => x.status === 'POSTED').length,
      CANCELLED: source.filter((x) => x.status === 'CANCELLED').length,
    };
  }, [data]);

  if (!data && loading) return <div className="empty-state">Загружаем приходные накладные…</div>;

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Приходные накладные</h1>
          <p>
            Закупки, входной ƏDV, фактическая стоимость запасов и задолженность поставщикам
            в одном документном журнале.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditor('new')}>+ Новая накладная</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="receipt-journal-toolbar">
        <div className="receipt-status-tabs">
          {([
            ['ALL', 'Все'],
            ['DRAFT', 'Черновики'],
            ['POSTED', 'Проведённые'],
            ['CANCELLED', 'Отменённые'],
          ] as Array<[ReceiptStatusFilter, string]>).map(([key, label]) => (
            <button
              type="button"
              key={key}
              className={statusFilter === key ? 'receipt-status-tab active' : 'receipt-status-tab'}
              onClick={() => setStatusFilter(key)}
            >
              {label}
              <span>{counts[key]}</span>
            </button>
          ))}
        </div>

        <input
          className="receipt-journal-search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Поиск по №, поставщику, складу…"
        />
      </div>

      <div className="warehouse-doc-panel">
        {documents.length === 0 ? (
          <div className="warehouse-doc-empty">
            <strong>Документы не найдены</strong>
            <span>Измените фильтр или создайте новую приходную накладную.</span>
          </div>
        ) : (
          <div className="warehouse-doc-table-wrap">
            <table className="warehouse-doc-table receipt-journal-table">
              <thead>
                <tr>
                  <th>Документ</th>
                  <th>Дата</th>
                  <th>Поставщик / магазин</th>
                  <th>Склад</th>
                  <th>Тип покупки</th>
                  <th>Итого</th>
                  <th>ƏDV</th>
                  <th>Статус</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {documents.map((document) => (
                  <tr key={document.id}>
                    <td>
                      <button
                        type="button"
                        className="receipt-document-link"
                        onClick={() => setEditor(document)}
                      >
                        <strong>{document.number}</strong>
                        {document.purchaseReferenceNumber && (
                          <small>Внешний № {document.purchaseReferenceNumber}</small>
                        )}
                      </button>
                    </td>
                    <td>{formatDate(document.documentDate)}</td>
                    <td>{document.supplierName ?? '—'}</td>
                    <td>{document.warehouseName ?? '—'}</td>
                    <td>{purchaseDocumentKindLabel(document.purchaseDocumentKind)}</td>
                    <td><strong>{money(document.totalAmount)}</strong></td>
                    <td>{money(document.vatAmount)}</td>
                    <td>
                      <span className={'badge ' + statusBadgeClass(document.status)}>
                        {statusLabel(document.status)}
                      </span>
                    </td>
                    <td>
                      <div className="warehouse-doc-actions receipt-document-actions">
                        <button className="text-button" onClick={() => setEditor(document)}>
                          {document.status === 'DRAFT' && canManage ? 'Изменить' : 'Просмотр'}
                        </button>

                        {canManage && (
                          <button
                            className="text-button"
                            disabled={actionId !== null}
                            onClick={() => void duplicateDocument(document)}
                          >
                            {actionId === 'duplicate:' + document.id ? 'Копируем…' : 'Дублировать'}
                          </button>
                        )}

                        {document.status === 'DRAFT' && canManage && (
                          <>
                            <button
                              className="primary-button compact"
                              disabled={postingId === document.id || actionId !== null}
                              onClick={() => void post(document)}
                            >
                              {postingId === document.id ? 'Проводим…' : 'Провести'}
                            </button>
                            <button
                              className="text-button danger-text-button"
                              disabled={actionId !== null || postingId !== null}
                              onClick={() => void removeDraft(document)}
                            >
                              {actionId === 'delete:' + document.id ? 'Удаляем…' : 'Удалить'}
                            </button>
                          </>
                        )}

                        {document.status === 'POSTED' && canManage && (
                          <>
                            <button
                              className="primary-button compact"
                              disabled={actionId !== null || postingId !== null}
                              onClick={() => void reverseDocument(document, true)}
                            >
                              {actionId === 'correct:' + document.id ? 'Готовим…' : 'Исправить'}
                            </button>
                            <button
                              className="text-button danger-text-button"
                              disabled={actionId !== null || postingId !== null}
                              onClick={() => void reverseDocument(document, false)}
                            >
                              {actionId === 'reverse:' + document.id ? 'Сторнируем…' : 'Сторно'}
                            </button>
                          </>
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

      {editor && data && (
        <ReceiptEditor
          token={token}
          data={data}
          document={editor === 'new' ? null : editor}
          readOnly={!canManage || (editor !== 'new' && editor.status !== 'DRAFT')}
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
  readOnly,
  onClose,
  onSaved,
}: {
  token: string;
  data: BackOfficeStockDocuments;
  document: StockDocument | null;
  readOnly: boolean;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const activeWarehouses = data.warehouses.filter((x) => x.isActive);
  const activeSuppliers = data.suppliers.filter((x) => x.isActive);
  const activeItems = data.items.filter((x) => x.isActive);
  const isVatPayer = data.taxProfile.taxRegime === 'VAT_18';

  const [number, setNumber] = useState(document?.number ?? '');
  const [documentDate, setDocumentDate] = useState(
    document
      ? dateInputValue(document.documentDate)
      : dateInputValue(new Date().toISOString()),
  );
  const [warehouseId, setWarehouseId] = useState(document?.warehouseId ?? activeWarehouses[0]?.id ?? '');
  const [supplierId, setSupplierId] = useState(document?.supplierId ?? activeSuppliers[0]?.id ?? '');
  const [purchaseDocumentKind, setPurchaseDocumentKind] = useState(
    document?.purchaseDocumentKind ?? 'SUPPLIER_INVOICE',
  );
  const [purchaseReferenceNumber, setPurchaseReferenceNumber] = useState(
    document?.purchaseReferenceNumber ?? '',
  );
  const [retailVatMode, setRetailVatMode] = useState(
    document?.purchaseDocumentKind === 'RETAIL_RECEIPT' &&
    document.lines.some((line) => line.vatTaxCode === 'VAT_18')
      ? 'VAT_18'
      : 'NOT_SPECIFIED',
  );
  const [vatPriceMode, setVatPriceMode] = useState(document?.vatPriceMode ?? 'INCLUDED');
  const [inputVatCreditStatus, setInputVatCreditStatus] = useState(
    document?.inputVatCreditStatus && document.inputVatCreditStatus !== 'NOT_APPLICABLE'
      ? document.inputVatCreditStatus
      : isVatPayer
        ? 'PENDING'
        : 'NON_CREDITABLE',
  );
  const [eInvoiceNumber, setEInvoiceNumber] = useState(document?.eInvoiceNumber ?? '');
  const [comment, setComment] = useState(document?.comment ?? '');
  const [lines, setLines] = useState<Array<{
    productId: string;
    quantity: string;
    unitPrice: string;
    vatTaxCode: string;
  }>>(
    document?.lines.length
      ? document.lines.map((line) => ({
          productId: line.productId,
          quantity: String(line.quantity),
          unitPrice: String(line.unitPrice),
          vatTaxCode: line.vatTaxCode ?? 'NO_VAT',
        }))
      : [{
          productId: activeItems[0]?.id ?? '',
          quantity: '',
          unitPrice: '',
          vatTaxCode: 'NO_VAT',
        }],
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isRetailReceipt = purchaseDocumentKind === 'RETAIL_RECEIPT';
  const isSupplierInvoice = purchaseDocumentKind === 'SUPPLIER_INVOICE';
  const effectiveVatPriceMode = isRetailReceipt ? 'INCLUDED' : vatPriceMode;
  const effectiveInputVatCreditStatus = isRetailReceipt
    ? 'NON_CREDITABLE'
    : inputVatCreditStatus;

  const previews = useMemo(
    () => lines.map((line) => {
      const quantity = decimal(line.quantity);
      const unitPrice = decimal(line.unitPrice);
      const effectiveVatTaxCode = isRetailReceipt
        ? retailVatMode === 'VAT_18' ? 'VAT_18' : 'NO_VAT'
        : line.vatTaxCode;

      return previewPurchaseLine(
        Number.isFinite(quantity) ? quantity : 0,
        Number.isFinite(unitPrice) ? unitPrice : 0,
        effectiveVatTaxCode,
        effectiveVatPriceMode,
        data.taxProfile.taxRegime,
        effectiveInputVatCreditStatus,
      );
    }),
    [
      lines,
      isRetailReceipt,
      retailVatMode,
      effectiveVatPriceMode,
      data.taxProfile.taxRegime,
      effectiveInputVatCreditStatus,
    ],
  );

  const hasVat18 = isRetailReceipt
    ? retailVatMode === 'VAT_18'
    : lines.some((line) => line.vatTaxCode === 'VAT_18');

  const totals = useMemo(
    () => previews.reduce(
      (sum, row) => ({
        net: round4(sum.net + row.netAmount),
        vat: round4(sum.vat + row.vatAmount),
        gross: round4(sum.gross + row.grossAmount),
        inventory: round4(sum.inventory + row.inventoryCostAmount),
        recoverable: round4(sum.recoverable + row.recoverableVatAmount),
      }),
      { net: 0, vat: 0, gross: 0, inventory: 0, recoverable: 0 },
    ),
    [previews],
  );

  function patchLine(
    index: number,
    patch: Partial<{ productId: string; quantity: string; unitPrice: string; vatTaxCode: string }>,
  ) {
    if (readOnly) return;
    setLines((current) => current.map((line, i) => i === index ? { ...line, ...patch } : line));
  }

  function changeDocumentKind(next: string) {
    if (readOnly) return;

    setPurchaseDocumentKind(next);

    if (next === 'RETAIL_RECEIPT') {
      setVatPriceMode('INCLUDED');
      setInputVatCreditStatus('NON_CREDITABLE');
      setEInvoiceNumber('');
      return;
    }

    if (isVatPayer && inputVatCreditStatus === 'NON_CREDITABLE') {
      setInputVatCreditStatus('PENDING');
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (readOnly) return;

    const submitter = (event.nativeEvent as SubmitEvent).submitter as HTMLButtonElement | null;
    const shouldPost = submitter?.value === 'post';

    const parsedLines = lines.filter((x) => x.productId).map((x) => ({
      productId: x.productId,
      quantity: decimal(x.quantity),
      unitPrice: decimal(x.unitPrice),
      vatTaxCode: isRetailReceipt
        ? retailVatMode === 'VAT_18' ? 'VAT_18' : 'NO_VAT'
        : x.vatTaxCode,
    }));

    if (
      !warehouseId ||
      !supplierId ||
      !parsedLines.length ||
      parsedLines.some((x) =>
        !Number.isFinite(x.quantity) ||
        x.quantity <= 0 ||
        !Number.isFinite(x.unitPrice) ||
        x.unitPrice < 0
      )
    ) {
      setError('Выберите склад, поставщика и заполните количество/цену во всех строках.');
      return;
    }

    const input: UpsertReceiptDocumentInput = {
      number: number.trim() || null,
      documentDate: documentDate ? new Date(documentDate + 'T12:00:00').toISOString() : null,
      warehouseId,
      supplierId,
      purchaseDocumentKind,
      purchaseReferenceNumber: purchaseReferenceNumber.trim() || null,
      retailVatMode: isRetailReceipt ? retailVatMode : null,
      vatPriceMode: effectiveVatPriceMode,
      inputVatCreditStatus:
        !isRetailReceipt && isVatPayer && hasVat18
          ? inputVatCreditStatus
          : null,
      eInvoiceNumber: isSupplierInvoice ? eInvoiceNumber.trim() || null : null,
      comment: comment.trim() || null,
      lines: parsedLines,
    };

    setSaving(true);
    setError(null);
    try {
      const saved = document
        ? await updateReceiptDocument(token, document.id, input)
        : await createReceiptDocument(token, input);

      if (shouldPost) await postStockDocument(token, saved.id);
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить приходную накладную');
    } finally {
      setSaving(false);
    }
  }

  const creditStatusDescription = inputVatCreditStatus === 'PENDING'
    ? 'Входной ƏDV отражается отдельно на 241-1, но ещё ожидает подтверждения для əvəzləşdirmə.'
    : inputVatCreditStatus === 'ELIGIBLE'
      ? 'Входной ƏDV можно принять к əvəzləşdirmə.'
      : inputVatCreditStatus === 'CREDITED'
        ? 'ƏDV помечен как уже принятый к əvəzləşdirmə.'
        : 'ƏDV не идёт на 241-1 и включается в стоимость запасов.';

  return (
    <div className="modal-backdrop receipt-workspace-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="receipt-workspace" onSubmit={submit}>
        <header className="receipt-workspace-header">
          <div>
            <div className="eyebrow">ПРИХОДНАЯ НАКЛАДНАЯ · МЕСТНАЯ ЗАКУПКА</div>
            <div className="receipt-workspace-title">
              <h2>{document ? document.number : 'Новая приходная накладная'}</h2>
              {document && (
                <span className={'badge ' + statusBadgeClass(document.status)}>
                  {statusLabel(document.status)}
                </span>
              )}
            </div>
            <p>
              {readOnly
                ? 'Документ проведён. Данные доступны для просмотра, прямое редактирование отключено.'
                : 'Заполните реквизиты, выберите тип покупки и добавьте позиции.'}
            </p>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </header>

        {readOnly && document?.status === 'POSTED' && (
          <div className="receipt-posted-banner">
            <strong>Проведённый документ защищён от редактирования</strong>
            <span>
              Он уже изменил складские остатки и бухгалтерские проводки. Для изменения проведённого документа
              будет использоваться отдельная операция исправления / сторно.
            </span>
          </div>
        )}

        <div className="receipt-workspace-body">
          <main className="receipt-workspace-main">
            <section className="receipt-section">
              <div className="receipt-section-heading">
                <div>
                  <span>1</span>
                  <div>
                    <strong>Основные реквизиты</strong>
                    <small>Дата, склад и контрагент</small>
                  </div>
                </div>
              </div>

              <div className="receipt-form-grid receipt-form-grid-4">
                <label>
                  <span>Дата</span>
                  <input
                    type="date"
                    value={documentDate}
                    onChange={(e) => setDocumentDate(e.target.value)}
                    disabled={readOnly}
                  />
                </label>

                <label>
                  <span>Склад</span>
                  <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} disabled={readOnly}>
                    <option value="">Выберите склад</option>
                    {activeWarehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
                  </select>
                </label>

                <label>
                  <span>Поставщик / магазин</span>
                  <select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} disabled={readOnly}>
                    <option value="">Выберите поставщика</option>
                    {activeSuppliers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
                  </select>
                </label>

                <label>
                  <span>Внутренний №</span>
                  <input
                    value={number}
                    onChange={(e) => setNumber(e.target.value)}
                    placeholder="Автоматически"
                    disabled={readOnly}
                  />
                </label>
              </div>
            </section>

            <section className="receipt-section">
              <div className="receipt-section-heading">
                <div>
                  <span>2</span>
                  <div>
                    <strong>Документ покупки</strong>
                    <small>Выберите, на основании чего оформляется поступление</small>
                  </div>
                </div>
              </div>

              <div className="receipt-kind-cards">
                {data.purchaseDocumentKinds.map((option) => (
                  <button
                    key={option.code}
                    type="button"
                    disabled={readOnly}
                    className={purchaseDocumentKind === option.code ? 'receipt-kind-card active' : 'receipt-kind-card'}
                    onClick={() => changeDocumentKind(option.code)}
                  >
                    <strong>{option.name}</strong>
                    <small>{purchaseKindDescription(option.code)}</small>
                  </button>
                ))}
              </div>

              <div className="receipt-document-options">
                <label>
                  <span>
                    {isRetailReceipt
                      ? '№ кассового чека'
                      : isSupplierInvoice
                        ? '№ накладной / документа'
                        : '№ / реквизиты документа'}
                  </span>
                  <input
                    value={purchaseReferenceNumber}
                    onChange={(e) => setPurchaseReferenceNumber(e.target.value)}
                    maxLength={120}
                    placeholder="Необязательно"
                    disabled={readOnly}
                  />
                </label>

                {isRetailReceipt ? (
                  <div className="receipt-choice-group">
                    <span>ƏDV в кассовом чеке</span>
                    <div className="receipt-choice-buttons">
                      {data.retailVatModes.map((option) => (
                        <button
                          type="button"
                          key={option.code}
                          disabled={readOnly}
                          className={retailVatMode === option.code ? 'receipt-choice-button active' : 'receipt-choice-button'}
                          onClick={() => setRetailVatMode(option.code)}
                        >
                          {option.name}
                        </button>
                      ))}
                    </div>
                  </div>
                ) : (
                  <div className="receipt-choice-group">
                    <span>Цена поставщика</span>
                    <div className="receipt-choice-buttons">
                      {data.vatPriceModes.map((option) => (
                        <button
                          type="button"
                          key={option.code}
                          disabled={readOnly}
                          className={vatPriceMode === option.code ? 'receipt-choice-button active' : 'receipt-choice-button'}
                          onClick={() => setVatPriceMode(option.code)}
                        >
                          {option.name}
                        </button>
                      ))}
                    </div>
                  </div>
                )}

                {isSupplierInvoice && (
                  <label>
                    <span>e-Qaimə / документ ƏDV</span>
                    <input
                      value={eInvoiceNumber}
                      onChange={(e) => setEInvoiceNumber(e.target.value)}
                      maxLength={120}
                      placeholder="Необязательно"
                      disabled={readOnly}
                    />
                  </label>
                )}
              </div>

              {isRetailReceipt ? (
                <div className="receipt-context-note">
                  <strong>
                    {retailVatMode === 'VAT_18'
                      ? 'Кассовый чек с указанным ƏDV 18%'
                      : 'Кассовый чек без отдельно указанного ƏDV'}
                  </strong>
                  <span>
                    {retailVatMode === 'VAT_18'
                      ? 'ƏDV выделяется для аналитики, но не идёт на 241-1. Полная сумма чека включается в стоимость запасов.'
                      : 'ƏDV отдельно не выделяется. Полная сумма покупки становится стоимостью запасов.'}
                  </span>
                </div>
              ) : (
                <div className="receipt-context-note">
                  <strong>Налоговый профиль: {taxRegimeLabel(data.taxProfile.taxRegime)}</strong>
                  <span>
                    {isVatPayer
                      ? 'Для строк с ƏDV 18% можно отдельно вести входной ƏDV и счёт 241-1.'
                      : 'Входной ƏDV не принимается к əvəzləşdirmə и увеличивает стоимость запасов.'}
                  </span>
                </div>
              )}

              {!isRetailReceipt && isVatPayer && hasVat18 && (
                <div className="receipt-vat-status-row">
                  <label>
                    <span>Статус входного ƏDV</span>
                    <select
                      value={inputVatCreditStatus}
                      onChange={(e) => setInputVatCreditStatus(e.target.value)}
                      disabled={readOnly}
                    >
                      {data.inputVatCreditStatuses.map((x) => (
                        <option key={x.code} value={x.code}>{x.name}</option>
                      ))}
                    </select>
                  </label>
                  <small>{creditStatusDescription}</small>
                </div>
              )}
            </section>

            <section className="receipt-section receipt-items-section">
              <div className="receipt-section-heading">
                <div>
                  <span>3</span>
                  <div>
                    <strong>Позиции</strong>
                    <small>
                      {isRetailReceipt
                        ? 'Режим ƏDV кассового чека применяется ко всем строкам'
                        : 'ƏDV можно задать отдельно для каждой позиции'}
                    </small>
                  </div>
                </div>

                {!readOnly && (
                  <button
                    type="button"
                    className="secondary-button compact"
                    onClick={() => setLines((current) => [
                      ...current,
                      { productId: '', quantity: '', unitPrice: '', vatTaxCode: 'NO_VAT' },
                    ])}
                  >
                    + Добавить позицию
                  </button>
                )}
              </div>

              <div className="receipt-item-list">
                {lines.map((line, index) => {
                  const item = activeItems.find((x) => x.id === line.productId);
                  const preview = previews[index];

                  return (
                    <article className="receipt-item-card" key={index}>
                      <div className="receipt-item-number">{index + 1}</div>

                      <div className="receipt-item-fields">
                        <label className="receipt-item-product">
                          <span>Номенклатура</span>
                          <select
                            value={line.productId}
                            onChange={(e) => patchLine(index, { productId: e.target.value })}
                            disabled={readOnly}
                          >
                            <option value="">Выберите позицию</option>
                            {activeItems.map((x) => (
                              <option key={x.id} value={x.id}>
                                {x.name}{x.sku ? ' · ' + x.sku : ''}
                              </option>
                            ))}
                          </select>
                        </label>

                        <label>
                          <span>Количество</span>
                          <div className="receipt-quantity-field">
                            <input
                              value={line.quantity}
                              onChange={(e) => patchLine(index, { quantity: e.target.value })}
                              inputMode="decimal"
                              placeholder="0"
                              disabled={readOnly}
                            />
                            <small>{item?.unit ?? ''}</small>
                          </div>
                        </label>

                        <label>
                          <span>Цена</span>
                          <input
                            value={line.unitPrice}
                            onChange={(e) => patchLine(index, { unitPrice: e.target.value })}
                            inputMode="decimal"
                            placeholder="0.00"
                            disabled={readOnly}
                          />
                        </label>

                        <label>
                          <span>ƏDV</span>
                          {isRetailReceipt ? (
                            <div className="receipt-readonly-value">
                              {retailVatMode === 'VAT_18' ? '18%' : 'Не указан'}
                            </div>
                          ) : (
                            <select
                              value={line.vatTaxCode}
                              onChange={(e) => patchLine(index, { vatTaxCode: e.target.value })}
                              disabled={readOnly}
                            >
                              {data.purchaseVatCodes.map((x) => (
                                <option key={x.code} value={x.code}>{x.name}</option>
                              ))}
                            </select>
                          )}
                        </label>
                      </div>

                      <div className="receipt-item-summary">
                        <div>
                          <span>Без ƏDV</span>
                          <strong>{money(preview.netAmount)}</strong>
                        </div>
                        <div>
                          <span>ƏDV</span>
                          <strong>{money(preview.vatAmount)}</strong>
                        </div>
                        <div>
                          <span>Итого</span>
                          <strong>{money(preview.grossAmount)}</strong>
                        </div>
                        <div>
                          <span>На склад</span>
                          <strong>{money(preview.inventoryCostAmount)}</strong>
                        </div>
                      </div>

                      {!readOnly && (
                        <button
                          type="button"
                          className="receipt-item-remove"
                          onClick={() => setLines((current) => current.filter((_, i) => i !== index))}
                          disabled={lines.length === 1}
                          title="Удалить позицию"
                        >
                          ×
                        </button>
                      )}
                    </article>
                  );
                })}
              </div>
            </section>

            <section className="receipt-section">
              <div className="receipt-section-heading">
                <div>
                  <span>4</span>
                  <div>
                    <strong>Комментарий</strong>
                    <small>Необязательная заметка к документу</small>
                  </div>
                </div>
              </div>

              <textarea
                className="receipt-comment"
                value={comment}
                onChange={(e) => setComment(e.target.value)}
                maxLength={500}
                rows={3}
                placeholder="Комментарий к поступлению…"
                disabled={readOnly}
              />
            </section>

            {activeSuppliers.length === 0 && (
              <div className="error-box">Сначала создайте поставщика в разделе «Поставщики».</div>
            )}
            {error && <div className="error-box">{error}</div>}
          </main>

          <aside className="receipt-workspace-summary">
            <div className="receipt-summary-card">
              <div className="receipt-summary-heading">
                <span>ИТОГ ДОКУМЕНТА</span>
                <strong>{money(totals.gross)}</strong>
              </div>

              <div className="receipt-summary-lines">
                <div>
                  <span>Без ƏDV</span>
                  <strong>{money(totals.net)}</strong>
                </div>
                <div>
                  <span>ƏDV</span>
                  <strong>{money(totals.vat)}</strong>
                </div>
                <div>
                  <span>На склад</span>
                  <strong>{money(totals.inventory)}</strong>
                </div>
                <div>
                  <span>На 241-1</span>
                  <strong>{money(isRetailReceipt ? 0 : totals.recoverable)}</strong>
                </div>
              </div>

              <div className="receipt-summary-divider" />

              <div className="receipt-summary-lines">
                <div>
                  <span>Поставщик</span>
                  <strong>{activeSuppliers.find((x) => x.id === supplierId)?.name ?? '—'}</strong>
                </div>
                <div>
                  <span>Склад</span>
                  <strong>{activeWarehouses.find((x) => x.id === warehouseId)?.name ?? '—'}</strong>
                </div>
                <div>
                  <span>Документ</span>
                  <strong>{purchaseDocumentKindLabel(purchaseDocumentKind)}</strong>
                </div>
              </div>
            </div>
          </aside>
        </div>

        <footer className="receipt-workspace-footer">
          <div>
            {readOnly ? (
              <span>Режим просмотра</span>
            ) : (
              <span>Черновик можно изменять до проведения.</span>
            )}
          </div>

          <div className="receipt-workspace-actions">
            <button type="button" className="secondary-button" onClick={onClose}>
              {readOnly ? 'Закрыть' : 'Отмена'}
            </button>

            {!readOnly && (
              <>
                <button
                  type="submit"
                  className="secondary-button"
                  value="draft"
                  disabled={saving || !warehouseId || !supplierId}
                >
                  {saving ? 'Сохраняем…' : 'Сохранить черновик'}
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  value="post"
                  disabled={saving || !warehouseId || !supplierId}
                >
                  {saving ? 'Проводим…' : 'Сохранить и провести'}
                </button>
              </>
            )}
          </div>
        </footer>
      </form>
    </div>
  );
}

type PurchasePreview = {
  netAmount: number;
  vatAmount: number;
  grossAmount: number;
  inventoryCostAmount: number;
  recoverableVatAmount: number;
};

function previewPurchaseLine(
  quantity: number,
  unitPrice: number,
  vatTaxCode: string,
  vatPriceMode: string,
  taxRegime: string,
  inputVatCreditStatus: string,
): PurchasePreview {
  const enteredAmount = round4(Math.max(0, quantity) * Math.max(0, unitPrice));

  let netAmount = enteredAmount;
  let vatAmount = 0;
  let grossAmount = enteredAmount;

  if (vatTaxCode === 'VAT_18') {
    if (vatPriceMode === 'INCLUDED') {
      grossAmount = enteredAmount;
      netAmount = round4(grossAmount * 100 / 118);
      vatAmount = round4(grossAmount - netAmount);
    } else {
      netAmount = enteredAmount;
      vatAmount = round4(netAmount * 18 / 100);
      grossAmount = round4(netAmount + vatAmount);
    }
  }

  const recoverable =
    vatTaxCode === 'VAT_18' &&
    taxRegime === 'VAT_18' &&
    ['PENDING', 'ELIGIBLE', 'CREDITED'].includes(inputVatCreditStatus);

  const recoverableVatAmount = recoverable ? vatAmount : 0;
  const inventoryCostAmount = round4(grossAmount - recoverableVatAmount);

  return {
    netAmount,
    vatAmount,
    grossAmount,
    inventoryCostAmount,
    recoverableVatAmount,
  };
}

function decimal(value: string) {
  return Number(value.replace(',', '.'));
}

function round4(value: number) {
  return Math.round((value + Number.EPSILON) * 10000) / 10000;
}

function purchaseDocumentKindLabel(value: string) {
  return ({
    SUPPLIER_INVOICE: 'Накладная / e-Qaimə',
    RETAIL_RECEIPT: 'Кассовый чек',
    OTHER: 'Другой документ',
  } as Record<string, string>)[value] ?? value;
}

function purchaseKindDescription(value: string) {
  return ({
    SUPPLIER_INVOICE: 'Поставка от контрагента с накладной или e-Qaimə.',
    RETAIL_RECEIPT: 'Покупка товара в магазине по кассовому чеку.',
    OTHER: 'Поступление по другому подтверждающему документу.',
  } as Record<string, string>)[value] ?? '';
}

function taxRegimeLabel(value: string) {
  return ({
    VAT_18: 'ƏDV — 18%',
    SIMPLIFIED_8: 'Sadələşdirilmiş vergi — 8%',
    SIMPLIFIED_2: 'Sadələşdirilmiş vergi — 2%',
    UNCONFIGURED: 'Не настроено',
  } as Record<string, string>)[value] ?? value;
}

function statusLabel(status: StockDocument['status']) {
  return status === 'POSTED' ? 'Проведён' : status === 'CANCELLED' ? 'Отменён' : 'Черновик';
}

function statusBadgeClass(status: StockDocument['status']) {
  return status === 'POSTED' ? 'success' : status === 'CANCELLED' ? 'danger' : 'neutral';
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value) + ' ₼';
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value));
}

function dateInputValue(value: string) {
  const date = new Date(value);
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
}
