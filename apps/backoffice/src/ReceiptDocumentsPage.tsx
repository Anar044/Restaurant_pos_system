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

  if (!data && loading) return <div className="empty-state">Загружаем приходные накладные…</div>;

  const documents = data?.documents.filter((x) => x.type === 'RECEIPT') ?? [];

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Приходные накладные</h1>
          <p>
            Местные закупки с раздельным учётом стоимости без ƏDV, входного ƏDV,
            суммы поставщику и фактической стоимости запасов.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditor('new')}>+ Приходная накладная</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="warehouse-doc-panel">
        {documents.length === 0 ? (
          <div className="warehouse-doc-empty">
            <strong>Приходных накладных пока нет</strong>
            <span>Создайте первый документ поступления.</span>
          </div>
        ) : (
          <div className="warehouse-doc-table-wrap">
            <table className="warehouse-doc-table">
              <thead>
                <tr>
                  <th>Дата</th>
                  <th>№</th>
                  <th>Поставщик</th>
                  <th>Документ покупки</th>
                  <th>Склад</th>
                  <th>Без ƏDV</th>
                  <th>ƏDV</th>
                  <th>Итого</th>
                  <th>Статус</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {documents.map((document) => (
                  <tr key={document.id}>
                    <td>{formatDate(document.documentDate)}</td>
                    <td><strong>{document.number}</strong></td>
                    <td>{document.supplierName ?? '—'}</td>
                    <td>
                      <strong>{purchaseDocumentKindLabel(document.purchaseDocumentKind)}</strong>
                      {document.purchaseReferenceNumber && <small className="receipt-doc-reference">№ {document.purchaseReferenceNumber}</small>}
                    </td>
                    <td>{document.warehouseName ?? '—'}</td>
                    <td>{money(document.netAmount)}</td>
                    <td>{money(document.vatAmount)}</td>
                    <td><strong>{money(document.totalAmount)}</strong></td>
                    <td>
                      <span className={'badge ' + (document.status === 'POSTED' ? 'success' : 'neutral')}>
                        {statusLabel(document.status)}
                      </span>
                    </td>
                    <td>
                      <div className="warehouse-doc-actions">
                        {document.status === 'DRAFT' && canManage && (
                          <>
                            <button className="text-button" onClick={() => setEditor(document)}>Открыть</button>
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
    ? 'Входной ƏDV отражается отдельно на 241-1, но ещё не считается подтверждённым для налогового əvəzləşdirmə.'
    : inputVatCreditStatus === 'ELIGIBLE'
      ? 'Входной ƏDV отражается на 241-1 как сумма, которую можно принять к əvəzləşdirmə.'
      : inputVatCreditStatus === 'CREDITED'
        ? 'ƏDV помечается как уже принятый к əvəzləşdirmə.'
        : 'ƏDV не идёт на 241-1 и включается в стоимость запасов.';

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card receipt-document-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">ПРИХОДНАЯ НАКЛАДНАЯ · МЕСТНАЯ ЗАКУПКА</div>
            <h2>{document ? document.number : 'Новый документ'}</h2>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="receipt-tax-profile">
          <div>
            <span>Налоговый профиль ресторана</span>
            <strong>{taxRegimeLabel(data.taxProfile.taxRegime)}</strong>
          </div>
          <small>
            {isVatPayer
              ? 'Входной ƏDV может учитываться отдельно от стоимости запасов.'
              : 'Входной ƏDV не принимается к əvəzləşdirmə и увеличивает стоимость запасов.'}
          </small>
        </div>

        <div className="receipt-document-grid">
          <label>
            <span>Внутренний номер документа</span>
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
              {activeWarehouses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </label>
          <label>
            <span>{isRetailReceipt ? 'Магазин / поставщик' : 'Поставщик'}</span>
            <select value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
              <option value="">Выберите поставщика</option>
              {activeSuppliers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </label>
          <label>
            <span>Документ покупки</span>
            <select
              value={purchaseDocumentKind}
              onChange={(e) => {
                const next = e.target.value;
                setPurchaseDocumentKind(next);
                if (next === 'RETAIL_RECEIPT') {
                  setVatPriceMode('INCLUDED');
                  setInputVatCreditStatus('NON_CREDITABLE');
                }
              }}
            >
              {data.purchaseDocumentKinds.map((x) => (
                <option key={x.code} value={x.code}>{x.name}</option>
              ))}
            </select>
          </label>
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
            />
          </label>

          {isRetailReceipt ? (
            <label>
              <span>ƏDV в кассовом чеке</span>
              <select value={retailVatMode} onChange={(e) => setRetailVatMode(e.target.value)}>
                {data.retailVatModes.map((x) => (
                  <option key={x.code} value={x.code}>{x.name}</option>
                ))}
              </select>
            </label>
          ) : (
            <label>
              <span>Как указана цена поставщика</span>
              <select value={vatPriceMode} onChange={(e) => setVatPriceMode(e.target.value)}>
                {data.vatPriceModes.map((x) => <option key={x.code} value={x.code}>{x.name}</option>)}
              </select>
            </label>
          )}

          {isSupplierInvoice && (
            <label>
              <span>e-Qaimə / документ ƏDV</span>
              <input
                value={eInvoiceNumber}
                onChange={(e) => setEInvoiceNumber(e.target.value)}
                maxLength={120}
                placeholder="Необязательно"
              />
            </label>
          )}
        </div>

        {isRetailReceipt ? (
          <div className="receipt-vat-credit-box muted-box retail-receipt-note">
            <strong>
              {retailVatMode === 'VAT_18'
                ? 'В чеке указан ƏDV 18% — налог будет выделен только для аналитики'
                : 'ƏDV в чеке не указан — вся сумма будет принята как стоимость покупки'}
            </strong>
            <small>
              {retailVatMode === 'VAT_18'
                ? 'Для розничного кассового чека система автоматически ставит «Не подлежит əvəzləşdirmə»: на 241-1 сумма не попадёт, а полный итог чека войдёт в стоимость запасов.'
                : 'ƏDV отдельно не выделяется, 241-1 = 0, а полная сумма покупки увеличивает стоимость запасов.'}
            </small>
          </div>
        ) : isVatPayer && hasVat18 ? (
          <div className="receipt-vat-credit-box">
            <label>
              <span>Статус входного ƏDV</span>
              <select
                value={inputVatCreditStatus}
                onChange={(e) => setInputVatCreditStatus(e.target.value)}
              >
                {data.inputVatCreditStatuses.map((x) => (
                  <option key={x.code} value={x.code}>{x.name}</option>
                ))}
              </select>
            </label>
            <small>{creditStatusDescription}</small>
          </div>
        ) : (
          <div className="receipt-vat-credit-box muted-box">
            <strong>{hasVat18 ? 'Входной ƏDV включается в себестоимость' : 'В документе пока нет строк с ƏDV 18%'}</strong>
            <small>
              {hasVat18
                ? 'Текущий налоговый профиль ресторана не позволяет относить этот ƏDV на 241-1.'
                : 'Выберите ƏDV 18% в нужной строке, если поставщик начисляет ƏDV.'}
            </small>
          </div>
        )}

        <div className="receipt-lines">
          <div className="receipt-lines-head">
            <div>
              <strong>Позиции</strong>
              <span>
                {isRetailReceipt
                  ? 'Для кассового чека налоговый режим применяется ко всем строкам документа.'
                  : 'Налоговый статус задаётся по каждой строке документа.'}
              </span>
            </div>
            <button
              type="button"
              className="secondary-button compact"
              onClick={() => setLines((x) => [
                ...x,
                { productId: '', quantity: '', unitPrice: '', vatTaxCode: 'NO_VAT' },
              ])}
            >
              + Строка
            </button>
          </div>

          <div className="receipt-lines-scroll">
            <div className="receipt-line receipt-line-header receipt-line-vat">
              <span>Номенклатура</span>
              <span>Количество</span>
              <span>Цена</span>
              <span>{isRetailReceipt ? 'ƏDV в чеке' : 'ƏDV'}</span>
              <span>Без ƏDV</span>
              <span>ƏDV сумма</span>
              <span>Итого</span>
              <span />
            </div>

            {lines.map((line, index) => {
              const item = activeItems.find((x) => x.id === line.productId);
              const preview = previews[index];
              return (
                <div className="receipt-line receipt-line-vat" key={index}>
                  <select
                    value={line.productId}
                    onChange={(e) => patchLine(index, { productId: e.target.value })}
                  >
                    <option value="">Выберите позицию</option>
                    {activeItems.map((x) => (
                      <option key={x.id} value={x.id}>
                        {x.name}{x.sku ? ' · ' + x.sku : ''}
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

                  {isRetailReceipt ? (
                    <span className="receipt-line-tax-label">
                      {retailVatMode === 'VAT_18' ? 'ƏDV 18%' : 'Не указан'}
                    </span>
                  ) : (
                    <select
                      value={line.vatTaxCode}
                      onChange={(e) => patchLine(index, { vatTaxCode: e.target.value })}
                    >
                      {data.purchaseVatCodes.map((x) => (
                        <option key={x.code} value={x.code}>{x.name}</option>
                      ))}
                    </select>
                  )}

                  <strong>{money(preview.netAmount)}</strong>
                  <strong>{money(preview.vatAmount)}</strong>
                  <strong>{money(preview.grossAmount)}</strong>

                  <button
                    type="button"
                    className="text-button"
                    onClick={() => setLines((x) => x.filter((_, i) => i !== index))}
                    disabled={lines.length === 1}
                  >
                    Удалить
                  </button>
                </div>
              );
            })}
          </div>

          <div className="receipt-tax-totals">
            <div><span>{hasVat18 ? 'Без ƏDV' : 'Стоимость'}</span><strong>{money(totals.net)}</strong></div>
            <div><span>ƏDV</span><strong>{money(totals.vat)}</strong></div>
            <div><span>К оплате поставщику</span><strong>{money(totals.gross)}</strong></div>
            <div><span>На склад</span><strong>{money(totals.inventory)}</strong></div>
            {!isRetailReceipt && isVatPayer && totals.recoverable > 0 && (
              <div className="accent"><span>На 241-1</span><strong>{money(totals.recoverable)}</strong></div>
            )}
          </div>
        </div>

        <label>
          <span>Комментарий</span>
          <input value={comment} onChange={(e) => setComment(e.target.value)} maxLength={500} />
        </label>

        {activeSuppliers.length === 0 && (
          <div className="error-box">Сначала создайте поставщика в отдельном разделе «Поставщики».</div>
        )}
        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions receipt-modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
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
        </div>
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

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value) + ' ₼';
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value));
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value));
}

function dateInputValue(value: string) {
  const date = new Date(value);
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
}
