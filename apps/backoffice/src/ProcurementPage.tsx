import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  approvePurchaseRequisition,
  cancelPurchaseOrder,
  cancelPurchaseRequisition,
  confirmPurchaseOrder,
  createPurchaseOrder,
  createPurchaseOrderReceiptDraft,
  createPurchaseRequisition,
  getBackOfficeProcurement,
  sendPurchaseOrder,
  submitPurchaseRequisition,
  type BackOfficeProcurement,
  type ProcurementCatalogItem,
  type ProcurementNeed,
  type PurchaseOrder,
  type PurchaseRequisition,
} from './api';
import './procurement.css';

type ProcurementTab = 'needs' | 'requisitions' | 'orders';

type DraftLine = {
  key: string;
  productId: string;
  quantity: number;
  unitPrice: number;
};

type RequisitionDraft = {
  warehouseId: string;
  comment: string;
  lines: DraftLine[];
};

type OrderDraft = {
  requisition: PurchaseRequisition;
  supplierId: string;
  comment: string;
  lines: DraftLine[];
};

function lineKey() {
  return Math.random().toString(36).slice(2);
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value) + ' ₼';
}

function qty(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    maximumFractionDigits: 3,
  }).format(value);
}

function shortDate(value: string) {
  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(new Date(value));
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    DRAFT: 'Черновик',
    PENDING_APPROVAL: 'На согласовании',
    APPROVED: 'Согласовано',
    SENT: 'Отправлен',
    CONFIRMED: 'Подтверждён',
    PARTIALLY_RECEIVED: 'Частично принят',
    COMPLETED: 'Выполнен',
    POSTED: 'Проведён',
    CANCELLED: 'Отменён',
  };
  return labels[status] ?? status;
}

function statusTone(status: string) {
  if (status === 'COMPLETED' || status === 'POSTED' || status === 'APPROVED') return 'success';
  if (status === 'PENDING_APPROVAL' || status === 'SENT' || status === 'CONFIRMED' || status === 'PARTIALLY_RECEIVED') return 'warning';
  if (status === 'CANCELLED') return 'danger';
  return 'neutral';
}

function matchLabel(status: string) {
  if (status === 'MATCHED') return 'Совпадает';
  if (status === 'PRICE_MISMATCH') return 'Есть расхождение цены';
  if (status === 'PARTIAL') return 'Частичная приёмка';
  return 'Ожидает приёмки';
}

export function ProcurementPage({
  token,
  canManage,
  onOpenReceipts,
}: {
  token: string;
  canManage: boolean;
  onOpenReceipts: () => void;
}) {
  const [data, setData] = useState<BackOfficeProcurement | null>(null);
  const [tab, setTab] = useState<ProcurementTab>('needs');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionId, setActionId] = useState<string | null>(null);
  const [requisitionDraft, setRequisitionDraft] = useState<RequisitionDraft | null>(null);
  const [orderDraft, setOrderDraft] = useState<OrderDraft | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeProcurement(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить закупки');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token]);

  const activeWarehouses = useMemo(
    () => data?.warehouses.filter((x) => x.isActive) ?? [],
    [data],
  );
  const activeSuppliers = useMemo(
    () => data?.suppliers.filter((x) => x.isActive) ?? [],
    [data],
  );
  const activeItems = useMemo(
    () => data?.items.filter((x) => x.isActive) ?? [],
    [data],
  );

  function defaultWarehouseId() {
    return activeWarehouses[0]?.id ?? '';
  }

  function priceForItem(itemId: string) {
    return data?.items.find((x) => x.id === itemId)?.lastPurchasePrice ?? 0;
  }

  function openNeedAsRequisition(need: ProcurementNeed) {
    setRequisitionDraft({
      warehouseId: defaultWarehouseId(),
      comment: 'Автоматически предложено по минимальному остатку',
      lines: [{
        key: lineKey(),
        productId: need.id,
        quantity: need.shortage,
        unitPrice: need.lastPurchasePrice ?? 0,
      }],
    });
  }

  function openEmptyRequisition() {
    setRequisitionDraft({
      warehouseId: defaultWarehouseId(),
      comment: '',
      lines: [{
        key: lineKey(),
        productId: activeItems[0]?.id ?? '',
        quantity: 1,
        unitPrice: activeItems[0]?.lastPurchasePrice ?? 0,
      }],
    });
  }

  function updateRequisitionLine(key: string, patch: Partial<DraftLine>) {
    setRequisitionDraft((current) => current ? {
      ...current,
      lines: current.lines.map((line) =>
        line.key === key ? { ...line, ...patch } : line),
    } : current);
  }

  function updateOrderLine(key: string, patch: Partial<DraftLine>) {
    setOrderDraft((current) => current ? {
      ...current,
      lines: current.lines.map((line) =>
        line.key === key ? { ...line, ...patch } : line),
    } : current);
  }

  async function saveRequisition(event: FormEvent) {
    event.preventDefault();
    if (!requisitionDraft || actionId) return;
    if (!requisitionDraft.warehouseId) {
      setError('Выберите склад назначения.');
      return;
    }

    setActionId('create-requisition');
    setError(null);
    try {
      await createPurchaseRequisition(token, {
        warehouseId: requisitionDraft.warehouseId,
        comment: requisitionDraft.comment.trim() || null,
        lines: requisitionDraft.lines.map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          unitPrice: Number(line.unitPrice) || 0,
        })),
      });
      setRequisitionDraft(null);
      setTab('requisitions');
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось создать заявку');
    } finally {
      setActionId(null);
    }
  }

  async function runRequisitionAction(
    requisition: PurchaseRequisition,
    action: 'submit' | 'approve' | 'cancel',
  ) {
    const key = action + ':' + requisition.id;
    setActionId(key);
    setError(null);
    try {
      if (action === 'submit') await submitPurchaseRequisition(token, requisition.id);
      if (action === 'approve') await approvePurchaseRequisition(token, requisition.id);
      if (action === 'cancel') await cancelPurchaseRequisition(token, requisition.id);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось изменить статус заявки');
    } finally {
      setActionId(null);
    }
  }

  function openOrder(requisition: PurchaseRequisition) {
    setOrderDraft({
      requisition,
      supplierId: activeSuppliers[0]?.id ?? '',
      comment: requisition.comment ?? '',
      lines: requisition.lines.map((line) => ({
        key: lineKey(),
        productId: line.productId,
        quantity: line.quantity,
        unitPrice: line.expectedUnitPrice || priceForItem(line.productId),
      })),
    });
  }

  async function saveOrder(event: FormEvent) {
    event.preventDefault();
    if (!orderDraft || actionId) return;
    if (!orderDraft.supplierId) {
      setError('Выберите поставщика.');
      return;
    }

    setActionId('create-order');
    setError(null);
    try {
      await createPurchaseOrder(token, {
        requisitionId: orderDraft.requisition.id,
        supplierId: orderDraft.supplierId,
        comment: orderDraft.comment.trim() || null,
        lines: orderDraft.lines.map((line) => ({
          productId: line.productId,
          quantity: Number(line.quantity),
          unitPrice: Number(line.unitPrice),
        })),
      });
      setOrderDraft(null);
      setTab('orders');
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось создать заказ');
    } finally {
      setActionId(null);
    }
  }

  async function runOrderAction(
    order: PurchaseOrder,
    action: 'send' | 'confirm' | 'cancel' | 'receive',
  ) {
    const key = action + ':' + order.id;
    setActionId(key);
    setError(null);
    try {
      if (action === 'send') await sendPurchaseOrder(token, order.id);
      if (action === 'confirm') await confirmPurchaseOrder(token, order.id);
      if (action === 'cancel') await cancelPurchaseOrder(token, order.id);
      if (action === 'receive') {
        await createPurchaseOrderReceiptDraft(token, order.id);
        onOpenReceipts();
        return;
      }
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось выполнить действие с заказом');
    } finally {
      setActionId(null);
    }
  }

  if (!data && loading) {
    return <div className="empty-state">Загружаем контур закупок…</div>;
  }

  return (
    <section className="procurement-page">
      <div className="page-heading procurement-heading">
        <div>
          <div className="eyebrow">ЗАКУПКИ</div>
          <h1>Закупки и исполнение заказов</h1>
          <p>
            Потребность → заявка → согласование → заказ поставщику → фактическая приёмка.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>
            {loading ? 'Обновляем…' : 'Обновить'}
          </button>
          {canManage && (
            <button className="primary-button" onClick={openEmptyRequisition}>
              + Новая заявка
            </button>
          )}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="procurement-flow">
        {[
          ['1', 'Потребность', 'Остатки и min stock'],
          ['2', 'Заявка PR', 'Что и куда купить'],
          ['3', 'Согласование', 'Контроль до заказа'],
          ['4', 'Заказ PO', 'Поставщик, цена, срок'],
          ['5', 'Приёмка', 'Факт и расхождения'],
        ].map(([step, title, subtitle], index) => (
          <div className="procurement-flow-step" key={step}>
            <span>{step}</span>
            <div>
              <strong>{title}</strong>
              <small>{subtitle}</small>
            </div>
            {index < 4 && <b>→</b>}
          </div>
        ))}
      </div>

      <div className="procurement-kpis">
        <div className="procurement-kpi">
          <small>Позиций ниже минимума</small>
          <strong>{data?.stats.shortageItems ?? 0}</strong>
          <span>{money(data?.stats.shortageEstimatedAmount ?? 0)} оценочно</span>
        </div>
        <div className="procurement-kpi">
          <small>Ждут согласования</small>
          <strong>{data?.stats.pendingApprovals ?? 0}</strong>
          <span>заявок PR</span>
        </div>
        <div className="procurement-kpi">
          <small>Активные заказы</small>
          <strong>{data?.stats.activeOrders ?? 0}</strong>
          <span>{money(data?.stats.activeOrderAmount ?? 0)}</span>
        </div>
        <div className="procurement-kpi accent">
          <small>Контроль исполнения</small>
          <strong>{data?.orders.filter((x) => x.matchStatus === 'MATCHED').length ?? 0}</strong>
          <span>заказов совпали с приёмкой</span>
        </div>
      </div>

      <div className="procurement-tabs">
        <button className={tab === 'needs' ? 'active' : ''} onClick={() => setTab('needs')}>
          Потребность <span>{data?.needs.length ?? 0}</span>
        </button>
        <button className={tab === 'requisitions' ? 'active' : ''} onClick={() => setTab('requisitions')}>
          Заявки PR <span>{data?.requisitions.length ?? 0}</span>
        </button>
        <button className={tab === 'orders' ? 'active' : ''} onClick={() => setTab('orders')}>
          Заказы PO <span>{data?.orders.length ?? 0}</span>
        </button>
      </div>

      {tab === 'needs' && (
        <div className="procurement-panel">
          <div className="procurement-panel-head">
            <div>
              <h2>Автоматически найденная потребность</h2>
              <p>Показываем позиции, где общий остаток ниже установленного минимального остатка.</p>
            </div>
          </div>
          {(data?.needs.length ?? 0) === 0 ? (
            <div className="empty-state">Сейчас нет позиций ниже минимального остатка.</div>
          ) : (
            <div className="procurement-table-wrap">
              <table className="procurement-table">
                <thead>
                  <tr>
                    <th>Номенклатура</th>
                    <th>Остаток</th>
                    <th>Минимум</th>
                    <th>Нужно</th>
                    <th>Последняя цена</th>
                    <th>Оценка</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {data?.needs.map((need) => (
                    <tr key={need.id}>
                      <td>
                        <strong>{need.name}</strong>
                        <small>{need.sku || need.unit}</small>
                      </td>
                      <td className="danger-text">{qty(need.currentStock)} {need.unit}</td>
                      <td>{qty(need.minStock)} {need.unit}</td>
                      <td><strong>{qty(need.shortage)} {need.unit}</strong></td>
                      <td>{need.lastPurchasePrice == null ? '—' : money(need.lastPurchasePrice)}</td>
                      <td>{money(need.estimatedAmount)}</td>
                      <td>
                        {canManage && (
                          <button className="secondary-button compact" onClick={() => openNeedAsRequisition(need)}>
                            Создать заявку
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {tab === 'requisitions' && (
        <div className="procurement-panel">
          <div className="procurement-panel-head">
            <div>
              <h2>Заявки на закупку (PR)</h2>
              <p>Заявка фиксирует потребность до выбора поставщика и отправки заказа.</p>
            </div>
          </div>
          {(data?.requisitions.length ?? 0) === 0 ? (
            <div className="empty-state">Заявок пока нет.</div>
          ) : (
            <div className="procurement-card-list">
              {data?.requisitions.map((requisition) => (
                <article className="procurement-document-card" key={requisition.id}>
                  <div className="procurement-document-top">
                    <div>
                      <strong>{requisition.number}</strong>
                      <span>{shortDate(requisition.documentDate)} · {requisition.warehouseName ?? 'Склад не указан'}</span>
                    </div>
                    <span className={'procurement-status ' + statusTone(requisition.status)}>
                      {statusLabel(requisition.status)}
                    </span>
                  </div>
                  <div className="procurement-lines-preview">
                    {requisition.lines.map((line) => (
                      <div key={line.id}>
                        <span>{line.productName}</span>
                        <strong>{qty(line.quantity)} {line.unit}</strong>
                        <span>{line.expectedUnitPrice ? money(line.expectedUnitPrice) : 'цена не задана'}</span>
                      </div>
                    ))}
                  </div>
                  {requisition.comment && <p className="procurement-comment">{requisition.comment}</p>}
                  <div className="procurement-document-footer">
                    <strong>{requisition.totalAmount > 0 ? money(requisition.totalAmount) : 'Сумма будет уточнена в PO'}</strong>
                    {canManage && (
                      <div className="procurement-actions">
                        {requisition.status === 'DRAFT' && (
                          <button
                            className="primary-button compact"
                            disabled={actionId !== null}
                            onClick={() => void runRequisitionAction(requisition, 'submit')}
                          >
                            На согласование
                          </button>
                        )}
                        {requisition.status === 'PENDING_APPROVAL' && (
                          <button
                            className="primary-button compact"
                            disabled={actionId !== null}
                            onClick={() => void runRequisitionAction(requisition, 'approve')}
                          >
                            Согласовать
                          </button>
                        )}
                        {requisition.status === 'APPROVED' && (
                          <button
                            className="primary-button compact"
                            disabled={actionId !== null}
                            onClick={() => openOrder(requisition)}
                          >
                            Создать PO
                          </button>
                        )}
                        {!['CANCELLED'].includes(requisition.status) && (
                          <button
                            className="text-button danger-text"
                            disabled={actionId !== null}
                            onClick={() => void runRequisitionAction(requisition, 'cancel')}
                          >
                            Отменить
                          </button>
                        )}
                      </div>
                    )}
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      )}

      {tab === 'orders' && (
        <div className="procurement-panel">
          <div className="procurement-panel-head">
            <div>
              <h2>Заказы поставщикам (PO)</h2>
              <p>Здесь видно заказанное, подтверждённое и реально принятое количество.</p>
            </div>
          </div>
          {(data?.orders.length ?? 0) === 0 ? (
            <div className="empty-state">Заказов поставщикам пока нет.</div>
          ) : (
            <div className="procurement-card-list">
              {data?.orders.map((order) => (
                <article className="procurement-document-card purchase-order-card" key={order.id}>
                  <div className="procurement-document-top">
                    <div>
                      <strong>{order.number}</strong>
                      <span>
                        {shortDate(order.documentDate)} · {order.supplierName ?? 'Поставщик не указан'} · {order.warehouseName ?? 'Склад'}
                      </span>
                    </div>
                    <span className={'procurement-status ' + statusTone(order.effectiveStatus)}>
                      {statusLabel(order.effectiveStatus)}
                    </span>
                  </div>

                  <div className="procurement-order-progress">
                    <div>
                      <span style={{ width: Math.min(order.completionPercent, 100) + '%' }} />
                    </div>
                    <small>
                      Принято {qty(order.receivedQuantity)} из {qty(order.orderedQuantity)} · {order.completionPercent}%
                    </small>
                  </div>

                  <div className="procurement-lines-preview order-lines">
                    {order.lines.map((line) => (
                      <div key={line.id}>
                        <span>{line.productName}</span>
                        <strong>{qty(line.quantity)} {line.unit} × {money(line.unitPrice ?? 0)}</strong>
                        <span>
                          принято {qty(line.receivedQuantity ?? 0)} · осталось {qty(line.remainingQuantity ?? line.quantity)}
                        </span>
                      </div>
                    ))}
                  </div>

                  <div className="procurement-match-row">
                    <span className={'match-badge ' + order.matchStatus.toLowerCase()}>
                      {matchLabel(order.matchStatus)}
                    </span>
                    {order.priceMismatchCount > 0 && (
                      <small>Расхождений по цене: {order.priceMismatchCount}</small>
                    )}
                    {order.receipts.length > 0 && (
                      <small>Приёмок: {order.receipts.length}</small>
                    )}
                  </div>

                  <div className="procurement-document-footer">
                    <strong>{money(order.totalAmount)}</strong>
                    {canManage && order.effectiveStatus !== 'CANCELLED' && order.effectiveStatus !== 'COMPLETED' && (
                      <div className="procurement-actions">
                        {order.status === 'APPROVED' && (
                          <button
                            className="primary-button compact"
                            disabled={actionId !== null}
                            onClick={() => void runOrderAction(order, 'send')}
                          >
                            Отправлен поставщику
                          </button>
                        )}
                        {order.status === 'SENT' && (
                          <button
                            className="primary-button compact"
                            disabled={actionId !== null}
                            onClick={() => void runOrderAction(order, 'confirm')}
                          >
                            Поставщик подтвердил
                          </button>
                        )}
                        {['APPROVED', 'SENT', 'CONFIRMED'].includes(order.status) && (
                          <button
                            className="secondary-button compact"
                            disabled={actionId !== null}
                            onClick={() => void runOrderAction(order, 'receive')}
                          >
                            Создать приёмку
                          </button>
                        )}
                        <button
                          className="text-button danger-text"
                          disabled={actionId !== null}
                          onClick={() => void runOrderAction(order, 'cancel')}
                        >
                          Отменить
                        </button>
                      </div>
                    )}
                    {order.effectiveStatus === 'COMPLETED' && (
                      <button className="secondary-button compact" onClick={onOpenReceipts}>
                        Открыть приходные накладные
                      </button>
                    )}
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      )}

      {requisitionDraft && (
        <div className="procurement-modal-backdrop" onMouseDown={() => setRequisitionDraft(null)}>
          <form className="procurement-modal" onSubmit={saveRequisition} onMouseDown={(e) => e.stopPropagation()}>
            <div className="procurement-modal-head">
              <div>
                <div className="eyebrow">PURCHASE REQUISITION</div>
                <h2>Новая заявка на закупку</h2>
              </div>
              <button type="button" className="icon-button" onClick={() => setRequisitionDraft(null)}>×</button>
            </div>

            <label className="procurement-field">
              <span>Склад назначения</span>
              <select
                value={requisitionDraft.warehouseId}
                onChange={(e) => setRequisitionDraft({ ...requisitionDraft, warehouseId: e.target.value })}
                required
              >
                <option value="">Выберите склад</option>
                {activeWarehouses.map((warehouse) => (
                  <option key={warehouse.id} value={warehouse.id}>{warehouse.name}</option>
                ))}
              </select>
            </label>

            <div className="procurement-editor-lines">
              <div className="procurement-editor-header">
                <strong>Позиции</strong>
                <button
                  type="button"
                  className="text-button"
                  onClick={() => setRequisitionDraft({
                    ...requisitionDraft,
                    lines: [...requisitionDraft.lines, {
                      key: lineKey(),
                      productId: activeItems[0]?.id ?? '',
                      quantity: 1,
                      unitPrice: activeItems[0]?.lastPurchasePrice ?? 0,
                    }],
                  })}
                >
                  + Добавить строку
                </button>
              </div>
              {requisitionDraft.lines.map((line) => (
                <div className="procurement-editor-line" key={line.key}>
                  <select
                    value={line.productId}
                    onChange={(e) => {
                      const itemId = e.target.value;
                      updateRequisitionLine(line.key, {
                        productId: itemId,
                        unitPrice: priceForItem(itemId),
                      });
                    }}
                    required
                  >
                    <option value="">Номенклатура</option>
                    {activeItems.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name}{item.sku ? ' · ' + item.sku : ''}
                      </option>
                    ))}
                  </select>
                  <label>
                    <span>Кол-во</span>
                    <input
                      type="number"
                      min="0.001"
                      step="0.001"
                      value={line.quantity}
                      onChange={(e) => updateRequisitionLine(line.key, { quantity: Number(e.target.value) })}
                      required
                    />
                  </label>
                  <label>
                    <span>Ожид. цена</span>
                    <input
                      type="number"
                      min="0"
                      step="0.0001"
                      value={line.unitPrice}
                      onChange={(e) => updateRequisitionLine(line.key, { unitPrice: Number(e.target.value) })}
                    />
                  </label>
                  <button
                    type="button"
                    className="icon-button"
                    disabled={requisitionDraft.lines.length <= 1}
                    onClick={() => setRequisitionDraft({
                      ...requisitionDraft,
                      lines: requisitionDraft.lines.filter((x) => x.key !== line.key),
                    })}
                  >
                    ×
                  </button>
                </div>
              ))}
            </div>

            <label className="procurement-field">
              <span>Комментарий</span>
              <textarea
                rows={3}
                value={requisitionDraft.comment}
                onChange={(e) => setRequisitionDraft({ ...requisitionDraft, comment: e.target.value })}
                placeholder="Для чего нужна закупка, желаемый срок, примечание…"
              />
            </label>

            <div className="procurement-modal-footer">
              <button type="button" className="secondary-button" onClick={() => setRequisitionDraft(null)}>
                Отмена
              </button>
              <button className="primary-button" disabled={actionId !== null}>
                {actionId === 'create-requisition' ? 'Создаём…' : 'Создать заявку'}
              </button>
            </div>
          </form>
        </div>
      )}

      {orderDraft && (
        <div className="procurement-modal-backdrop" onMouseDown={() => setOrderDraft(null)}>
          <form className="procurement-modal wide" onSubmit={saveOrder} onMouseDown={(e) => e.stopPropagation()}>
            <div className="procurement-modal-head">
              <div>
                <div className="eyebrow">PURCHASE ORDER</div>
                <h2>Заказ поставщику по {orderDraft.requisition.number}</h2>
              </div>
              <button type="button" className="icon-button" onClick={() => setOrderDraft(null)}>×</button>
            </div>

            <label className="procurement-field">
              <span>Поставщик</span>
              <select
                value={orderDraft.supplierId}
                onChange={(e) => setOrderDraft({ ...orderDraft, supplierId: e.target.value })}
                required
              >
                <option value="">Выберите поставщика</option>
                {activeSuppliers.map((supplier) => (
                  <option key={supplier.id} value={supplier.id}>{supplier.name}</option>
                ))}
              </select>
            </label>

            <div className="procurement-editor-lines">
              <div className="procurement-editor-header">
                <strong>Цены и количество заказа</strong>
                <span>Склад: {orderDraft.requisition.warehouseName ?? '—'}</span>
              </div>
              {orderDraft.lines.map((line) => {
                const item = activeItems.find((x) => x.id === line.productId);
                return (
                  <div className="procurement-editor-line order" key={line.key}>
                    <div className="procurement-line-name">
                      <strong>{item?.name ?? line.productId}</strong>
                      <small>{item?.sku || item?.unit}</small>
                    </div>
                    <label>
                      <span>Кол-во</span>
                      <input
                        type="number"
                        min="0.001"
                        step="0.001"
                        value={line.quantity}
                        onChange={(e) => updateOrderLine(line.key, { quantity: Number(e.target.value) })}
                        required
                      />
                    </label>
                    <label>
                      <span>Цена</span>
                      <input
                        type="number"
                        min="0.0001"
                        step="0.0001"
                        value={line.unitPrice}
                        onChange={(e) => updateOrderLine(line.key, { unitPrice: Number(e.target.value) })}
                        required
                      />
                    </label>
                    <strong className="procurement-line-total">
                      {money((Number(line.quantity) || 0) * (Number(line.unitPrice) || 0))}
                    </strong>
                  </div>
                );
              })}
            </div>

            <label className="procurement-field">
              <span>Комментарий поставщику / внутреннее примечание</span>
              <textarea
                rows={3}
                value={orderDraft.comment}
                onChange={(e) => setOrderDraft({ ...orderDraft, comment: e.target.value })}
              />
            </label>

            <div className="procurement-order-total">
              <span>Итого PO</span>
              <strong>
                {money(orderDraft.lines.reduce(
                  (sum, line) => sum + (Number(line.quantity) || 0) * (Number(line.unitPrice) || 0),
                  0,
                ))}
              </strong>
            </div>

            <div className="procurement-modal-footer">
              <button type="button" className="secondary-button" onClick={() => setOrderDraft(null)}>
                Отмена
              </button>
              <button className="primary-button" disabled={actionId !== null}>
                {actionId === 'create-order' ? 'Создаём…' : 'Создать заказ PO'}
              </button>
            </div>
          </form>
        </div>
      )}
    </section>
  );
}
