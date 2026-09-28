import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeFinance,
  type FinanceShift,
  type FinanceShiftReport,
  addShiftCashTransaction,
  closeShiftFromBackOffice,
  getBackOfficeFinance,
} from './api';
import './finance.css';

type PeriodPreset = 'today' | 'yesterday' | '7d' | '30d' | 'custom';
type ShiftStatusFilter = 'ALL' | 'OPEN' | 'CLOSED';

type FinanceFilters = {
  fromDate: string;
  toDate: string;
  deviceIds: string[];
};

type CashMovementState = {
  shiftId: string;
  deviceName: string;
  type: 'DEPOSIT' | 'WITHDRAWAL';
} | null;

export function FinancePage({
  token,
  canManageShifts,
}: {
  token: string;
  canManageShifts: boolean;
}) {
  const today = dateInputValue(new Date());
  const initialFilters = useMemo<FinanceFilters>(
    () => ({ fromDate: today, toDate: today, deviceIds: [] }),
    [today],
  );

  const [draftFilters, setDraftFilters] = useState<FinanceFilters>(initialFilters);
  const [filters, setFilters] = useState<FinanceFilters>(initialFilters);
  const [periodPreset, setPeriodPreset] = useState<PeriodPreset>('today');
  const [statusFilter, setStatusFilter] = useState<ShiftStatusFilter>('ALL');
  const [selectedShiftId, setSelectedShiftId] = useState<string | null>(null);
  const [data, setData] = useState<BackOfficeFinance | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [movement, setMovement] = useState<CashMovementState>(null);
  const [closing, setClosing] = useState(false);

  async function load(
    nextShiftId: string | null,
    nextFilters: FinanceFilters = filters,
  ) {
    setLoading(true);
    setError(null);

    try {
      const bounds = periodBounds(nextFilters.fromDate, nextFilters.toDate);
      const next = await getBackOfficeFinance(token, {
        shiftId: nextShiftId,
        from: bounds.from,
        to: bounds.to,
        deviceIds: nextFilters.deviceIds,
      });

      setData(next);
      setSelectedShiftId(nextShiftId);
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : 'Не удалось загрузить кассовые данные',
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(null, initialFilters);
  }, [token]);

  const selectedShift =
    data?.shifts.find((shift) => shift.id === selectedShiftId) ?? null;

  const visibleShifts = useMemo(() => {
    const shifts = data?.shifts ?? [];
    if (statusFilter === 'ALL') return shifts;
    return shifts.filter((shift) => shift.status === statusFilter);
  }, [data, statusFilter]);

  function applyFilters() {
    const next = {
      ...draftFilters,
      deviceIds: [...draftFilters.deviceIds],
    };
    setFilters(next);
    setSelectedShiftId(null);
    void load(null, next);
  }

  function applyPreset(preset: Exclude<PeriodPreset, 'custom'>) {
    const range = presetRange(preset);
    const next = {
      ...draftFilters,
      fromDate: range.fromDate,
      toDate: range.toDate,
    };

    setPeriodPreset(preset);
    setDraftFilters(next);
    setFilters(next);
    setSelectedShiftId(null);
    void load(null, next);
  }

  function toggleRegister(id: string) {
    setDraftFilters((current) => {
      const selected = current.deviceIds.includes(id);
      return {
        ...current,
        deviceIds: selected
          ? current.deviceIds.filter((value) => value !== id)
          : [...current.deviceIds, id],
      };
    });
  }

  function openShift(id: string) {
    void load(id, filters);
  }

  function backToDashboard() {
    setSelectedShiftId(null);
    void load(null, filters);
  }

  if (selectedShiftId && selectedShift && data?.selectedShiftReport) {
    return (
      <ShiftDetailView
        data={data}
        shift={selectedShift}
        report={data.selectedShiftReport}
        loading={loading}
        canManageShifts={canManageShifts}
        onBack={backToDashboard}
        onRefresh={() => void load(selectedShift.id, filters)}
        onCashMovement={(type) =>
          setMovement({
            shiftId: selectedShift.id,
            deviceName: selectedShift.deviceName,
            type,
          })
        }
        onCloseShift={() => setClosing(true)}
        movement={movement}
        setMovement={setMovement}
        closing={closing}
        setClosing={setClosing}
        token={token}
        onMutationSaved={async () => {
          await load(selectedShift.id, filters);
        }}
      />
    );
  }

  const summary = data?.summary;

  return (
    <section className="finance-dashboard">
      <div className="page-heading finance-page-heading">
        <div>
          <div className="eyebrow">КАССА И СМЕНЫ</div>
          <h1>Кассовый контроль</h1>
          <p>
            Выручка, открытые заказы и кассовые смены за выбранный период.
            Детали операций находятся внутри конкретной смены.
          </p>
        </div>
        <button
          className="secondary-button"
          onClick={() => void load(null, filters)}
          disabled={loading}
        >
          {loading ? 'Обновляем…' : 'Обновить'}
        </button>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void load(null, filters)}>Повторить</button>
        </div>
      )}

      <div className="finance-filter-bar">
        <div className="finance-period-presets">
          <button
            className={periodPreset === 'today' ? 'active' : ''}
            onClick={() => applyPreset('today')}
          >
            Сегодня
          </button>
          <button
            className={periodPreset === 'yesterday' ? 'active' : ''}
            onClick={() => applyPreset('yesterday')}
          >
            Вчера
          </button>
          <button
            className={periodPreset === '7d' ? 'active' : ''}
            onClick={() => applyPreset('7d')}
          >
            7 дней
          </button>
          <button
            className={periodPreset === '30d' ? 'active' : ''}
            onClick={() => applyPreset('30d')}
          >
            30 дней
          </button>
        </div>

        <div className="finance-custom-period">
          <label>
            <span>С</span>
            <input
              type="date"
              value={draftFilters.fromDate}
              onChange={(e) => {
                setPeriodPreset('custom');
                setDraftFilters((current) => ({
                  ...current,
                  fromDate: e.target.value,
                }));
              }}
            />
          </label>
          <span className="date-separator">—</span>
          <label>
            <span>По</span>
            <input
              type="date"
              value={draftFilters.toDate}
              min={draftFilters.fromDate}
              onChange={(e) => {
                setPeriodPreset('custom');
                setDraftFilters((current) => ({
                  ...current,
                  toDate: e.target.value,
                }));
              }}
            />
          </label>
        </div>

        <details className="finance-register-picker">
          <summary>
            <span>Кассы</span>
            <strong>
              {draftFilters.deviceIds.length === 0
                ? 'Все кассы'
                : draftFilters.deviceIds.length === 1
                  ? registerName(
                      data,
                      draftFilters.deviceIds[0],
                    )
                  : draftFilters.deviceIds.length + ' кассы'}
            </strong>
            <span className="picker-chevron">⌄</span>
          </summary>

          <div className="finance-register-popover">
            <label className="register-option register-all">
              <input
                type="checkbox"
                checked={draftFilters.deviceIds.length === 0}
                onChange={() =>
                  setDraftFilters((current) => ({
                    ...current,
                    deviceIds: [],
                  }))
                }
              />
              <span>
                <strong>Все кассы</strong>
                <small>Показать ресторан целиком</small>
              </span>
            </label>

            {(data?.cashRegisters ?? []).map((register) => (
              <label className="register-option" key={register.id}>
                <input
                  type="checkbox"
                  checked={draftFilters.deviceIds.includes(register.id)}
                  onChange={() => toggleRegister(register.id)}
                />
                <span>
                  <strong>{register.name}</strong>
                  <small>{register.isActive ? 'Активна' : 'Отключена'}</small>
                </span>
              </label>
            ))}
          </div>
        </details>

        <button
          className="primary-button finance-apply-filter"
          onClick={applyFilters}
          disabled={
            !draftFilters.fromDate ||
            !draftFilters.toDate ||
            draftFilters.toDate < draftFilters.fromDate
          }
        >
          Показать
        </button>
      </div>

      <div className="finance-filter-summary">
        <span>{humanPeriod(filters.fromDate, filters.toDate)}</span>
        <span className="filter-dot">•</span>
        <span>
          {filters.deviceIds.length === 0
            ? 'Все кассы'
            : filters.deviceIds
                .map((id) => registerName(data, id))
                .join(', ')}
        </span>
      </div>

      <div className="revenue-equation">
        <RevenueCard
          label="Закрытые заказы"
          value={summary?.completedOrdersAmount ?? 0}
          detail={(summary?.completedOrdersCount ?? 0) + ' заказов'}
          tone="closed"
        />
        <div className="equation-sign">+</div>
        <RevenueCard
          label="Открытые заказы"
          value={summary?.openOrdersAmount ?? 0}
          detail={(summary?.openOrdersCount ?? 0) + ' сейчас в работе'}
          tone="open"
        />
        <div className="equation-sign">=</div>
        <RevenueCard
          label="Ожидаемая выручка"
          value={summary?.expectedRevenue ?? 0}
          detail="закрытые + открытые заказы"
          tone="expected"
          emphasized
        />
      </div>

      <div className="finance-secondary-kpis">
        <CompactKpi
          label="Возвраты"
          value={money(summary?.refundAmount ?? 0)}
          detail="за выбранный период"
          warning={(summary?.refundAmount ?? 0) > 0}
        />
        <CompactKpi
          label="Выручка после возвратов"
          value={money(summary?.netExpectedRevenue ?? 0)}
          detail="ожидаемая минус возвраты"
        />
        <CompactKpi
          label="Открытые смены"
          value={String(summary?.openShiftsCount ?? 0)}
          detail={(summary?.closedShiftsCount ?? 0) + ' закрыто в периоде'}
        />
        <CompactKpi
          label="Расхождение кассы"
          value={signedMoney(summary?.cashDifference ?? 0)}
          detail="сумма по закрытым сменам"
          warning={Math.abs(summary?.cashDifference ?? 0) >= 0.01}
        />
      </div>

      <div className="finance-panel finance-shifts-panel">
        <div className="finance-panel-head finance-shifts-head">
          <div>
            <h2>Кассовые смены</h2>
            <p>
              Выберите смену, чтобы открыть её заказы, оплаты,
              возвраты и кассовые операции.
            </p>
          </div>

          <div className="shift-status-tabs">
            <button
              className={statusFilter === 'ALL' ? 'active' : ''}
              onClick={() => setStatusFilter('ALL')}
            >
              Все
              <span>{data?.shifts.length ?? 0}</span>
            </button>
            <button
              className={statusFilter === 'OPEN' ? 'active' : ''}
              onClick={() => setStatusFilter('OPEN')}
            >
              Открытые
              <span>
                {(data?.shifts ?? []).filter((x) => x.status === 'OPEN').length}
              </span>
            </button>
            <button
              className={statusFilter === 'CLOSED' ? 'active' : ''}
              onClick={() => setStatusFilter('CLOSED')}
            >
              Закрытые
              <span>
                {(data?.shifts ?? []).filter((x) => x.status === 'CLOSED').length}
              </span>
            </button>
          </div>
        </div>

        <div className="finance-table-wrap">
          <table className="finance-table shifts-table">
            <thead>
              <tr>
                <th>Смена</th>
                <th>Касса</th>
                <th>Открыта</th>
                <th>Закрыта</th>
                <th>Сотрудник</th>
                <th>Заказы</th>
                <th>Оборот</th>
                <th>Ожидается в кассе</th>
                <th>Расхождение</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {visibleShifts.map((shift) => (
                <tr
                  key={shift.id}
                  className="shift-table-row"
                  onClick={() => openShift(shift.id)}
                >
                  <td>
                    <span
                      className={
                        'shift-kind-badge ' +
                        (shift.status === 'OPEN' ? 'x' : 'z')
                      }
                    >
                      {shift.status === 'OPEN' ? 'X' : 'Z'}
                    </span>
                    <span
                      className={
                        'badge ' +
                        (shift.status === 'OPEN' ? 'success' : 'neutral')
                      }
                    >
                      {shift.status === 'OPEN' ? 'Открыта' : 'Закрыта'}
                    </span>
                  </td>
                  <td>
                    <strong>{shift.deviceName}</strong>
                  </td>
                  <td>
                    <strong>{formatShortDate(shift.openedAt)}</strong>
                    <small className="cell-subline">
                      {formatTime(shift.openedAt)}
                    </small>
                  </td>
                  <td>
                    {shift.closedAt ? (
                      <>
                        <strong>{formatShortDate(shift.closedAt)}</strong>
                        <small className="cell-subline">
                          {formatTime(shift.closedAt)}
                        </small>
                      </>
                    ) : (
                      <span className="muted">—</span>
                    )}
                  </td>
                  <td>
                    {shift.openedByEmployeeName ?? '—'}
                  </td>
                  <td>
                    <strong>{shift.ordersCount}</strong>
                  </td>
                  <td>
                    <strong>{money(shift.netSales)}</strong>
                    {shift.refunds > 0 && (
                      <small className="cell-subline">
                        {'возвраты ' + money(shift.refunds)}
                      </small>
                    )}
                  </td>
                  <td>
                    <strong>{money(shift.expectedCashAtClose)}</strong>
                  </td>
                  <td>
                    {shift.cashDifference == null ? (
                      <span className="muted">—</span>
                    ) : (
                      <strong
                        className={differenceClass(shift.cashDifference)}
                      >
                        {signedMoney(shift.cashDifference)}
                      </strong>
                    )}
                  </td>
                  <td className="shift-open-cell">
                    <span>Открыть ›</span>
                  </td>
                </tr>
              ))}

              {!loading && visibleShifts.length === 0 && (
                <tr>
                  <td colSpan={10} className="finance-empty-row">
                    За выбранный период смен не найдено.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </section>
  );
}

function ShiftDetailView({
  data,
  shift,
  report,
  loading,
  canManageShifts,
  onBack,
  onRefresh,
  onCashMovement,
  onCloseShift,
  movement,
  setMovement,
  closing,
  setClosing,
  token,
  onMutationSaved,
}: {
  data: BackOfficeFinance;
  shift: FinanceShift;
  report: FinanceShiftReport;
  loading: boolean;
  canManageShifts: boolean;
  onBack: () => void;
  onRefresh: () => void;
  onCashMovement: (type: 'DEPOSIT' | 'WITHDRAWAL') => void;
  onCloseShift: () => void;
  movement: CashMovementState;
  setMovement: (value: CashMovementState) => void;
  closing: boolean;
  setClosing: (value: boolean) => void;
  token: string;
  onMutationSaved: () => Promise<void>;
}) {
  const orders = data.selectedShiftOrders;

  return (
    <section className="finance-shift-detail">
      <div className="shift-detail-top">
        <button className="back-link" onClick={onBack}>
          ‹ К списку смен
        </button>

        <div className="shift-detail-actions">
          <button
            className="secondary-button"
            onClick={onRefresh}
            disabled={loading}
          >
            {loading ? 'Обновляем…' : 'Обновить'}
          </button>
          {shift.status === 'OPEN' && canManageShifts && (
            <>
              <button
                className="secondary-button"
                onClick={() => onCashMovement('DEPOSIT')}
              >
                + Внесение
              </button>
              <button
                className="secondary-button"
                onClick={() => onCashMovement('WITHDRAWAL')}
              >
                − Изъятие
              </button>
              <button className="danger-button" onClick={onCloseShift}>
                Закрыть смену
              </button>
            </>
          )}
        </div>
      </div>

      <div className="shift-detail-heading">
        <div>
          <div className="shift-title-line">
            <span
              className={
                'report-type-badge ' + report.reportType.toLowerCase()
              }
            >
              {report.reportType}-ОТЧЁТ
            </span>
            <h1>{shift.deviceName}</h1>
            <span
              className={
                'badge ' +
                (shift.status === 'OPEN' ? 'success' : 'neutral')
              }
            >
              {shift.status === 'OPEN' ? 'Смена открыта' : 'Смена закрыта'}
            </span>
          </div>
          <p>
            {'Открыта ' + formatDate(report.openedAt)}
            {report.openedByEmployeeName
              ? ' · ' + report.openedByEmployeeName
              : ''}
          </p>
        </div>
      </div>

      <div className="shift-overview-strip">
        <DetailMeta
          label="Открытие"
          value={formatDate(report.openedAt)}
        />
        <DetailMeta
          label="Закрытие"
          value={
            report.closedAt
              ? formatDate(report.closedAt)
              : 'Смена ещё работает'
          }
        />
        <DetailMeta
          label="Заказы"
          value={String(orders.length)}
        />
        <DetailMeta
          label="Оплаты"
          value={String(report.paymentsCount)}
        />
      </div>

      <div className="shift-cash-grid shift-detail-cash-grid">
        <CashMetric label="Начальный остаток" value={report.openingCash} />
        <CashMetric label="Наличные продажи" value={report.cashSales} />
        <CashMetric label="Возвраты наличными" value={-report.cashRefunds} />
        <CashMetric label="Внесения" value={report.deposits} />
        <CashMetric label="Изъятия" value={-report.withdrawals} />
        <CashMetric
          label="Ожидается в кассе"
          value={report.expectedCash}
          emphasized
        />
        {report.closingCash != null && (
          <CashMetric
            label="Фактически посчитано"
            value={report.closingCash}
          />
        )}
        {report.cashDifference != null && (
          <CashMetric
            label="Расхождение"
            value={report.cashDifference}
            warning={Math.abs(report.cashDifference) >= 0.01}
            emphasized
          />
        )}
      </div>

      {report.closingNote && (
        <div className="shift-closing-note detail-closing-note">
          <strong>Комментарий при закрытии</strong>
          <span>{report.closingNote}</span>
        </div>
      )}

      <div className="shift-detail-columns">
        <div className="finance-panel detail-panel">
          <div className="finance-panel-head">
            <div>
              <h2>Оплаты по типам</h2>
              <p>Итог этой кассовой смены.</p>
            </div>
            <strong>{money(report.netSales)}</strong>
          </div>

          <div className="payment-method-list detail-list">
            {report.payments.map((row) => (
              <div className="payment-method-row" key={row.method}>
                <div>
                  <strong>{methodName(row.method)}</strong>
                  <small>
                    {'Продажи ' + money(row.gross)}
                    {row.refunds > 0
                      ? ' · возвраты ' + money(row.refunds)
                      : ''}
                  </small>
                </div>
                <strong>{money(row.net)}</strong>
              </div>
            ))}
            {report.payments.length === 0 && (
              <div className="finance-empty-box">
                Оплат в смене пока нет.
              </div>
            )}
          </div>
        </div>

        <div className="finance-panel detail-panel">
          <div className="finance-panel-head">
            <div>
              <h2>Движение наличности</h2>
              <p>Внесения и изъятия по этой смене.</p>
            </div>
          </div>

          <div className="cash-movement-list detail-list">
            {report.cashTransactions.map((entry) => (
              <div className="cash-movement-row" key={entry.id}>
                <div>
                  <strong>
                    {entry.type === 'DEPOSIT' ? 'Внесение' : 'Изъятие'}
                  </strong>
                  <span>
                    {(entry.reason || 'Без комментария') +
                      ' · ' +
                      formatDate(entry.createdAt)}
                  </span>
                  <small>{entry.employeeName ?? 'Сотрудник'}</small>
                </div>
                <strong
                  className={
                    entry.type === 'DEPOSIT'
                      ? 'cash-positive'
                      : 'cash-negative'
                  }
                >
                  {(entry.type === 'DEPOSIT' ? '+' : '−') +
                    money(entry.amount)}
                </strong>
              </div>
            ))}
            {report.cashTransactions.length === 0 && (
              <div className="finance-empty-box">
                Внесений и изъятий не было.
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="finance-panel shift-orders-panel">
        <div className="finance-panel-head">
          <div>
            <h2>Заказы смены</h2>
            <p>
              Здесь показываются только заказы, открытые или оплаченные
              в выбранной смене.
            </p>
          </div>
          <span className="orders-count-badge">{orders.length}</span>
        </div>

        <div className="finance-table-wrap">
          <table className="finance-table shift-orders-table">
            <thead>
              <tr>
                <th>Заказ</th>
                <th>Статус</th>
                <th>Стол</th>
                <th>Сотрудник</th>
                <th>Открыт</th>
                <th>Сумма</th>
                <th>Оплачено в смене</th>
                <th>Способ</th>
                <th>Остаток</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.id}>
                  <td>
                    <strong>{'#' + order.orderNumber}</strong>
                    {!order.openedInThisShift && (
                      <small className="cell-subline">
                        открыт в другой смене
                      </small>
                    )}
                  </td>
                  <td>
                    <span
                      className={'badge ' + orderStatusTone(order.status)}
                    >
                      {orderStatusName(order.status)}
                    </span>
                  </td>
                  <td>
                    {order.tableName
                      ? [order.hallName, order.tableName]
                          .filter(Boolean)
                          .join(' · ')
                      : '—'}
                  </td>
                  <td>{order.employeeName ?? '—'}</td>
                  <td>{formatDate(order.createdAt)}</td>
                  <td><strong>{money(order.total)}</strong></td>
                  <td>
                    <strong>{money(order.netPaidInShift)}</strong>
                    {order.refundedInShift > 0 && (
                      <small className="cell-subline">
                        {'возврат ' + money(order.refundedInShift)}
                      </small>
                    )}
                  </td>
                  <td>
                    {order.paymentMethods.length === 0
                      ? '—'
                      : order.paymentMethods
                          .map(
                            (item) =>
                              methodName(item.method) +
                              ' ' +
                              money(item.amount),
                          )
                          .join(' + ')}
                  </td>
                  <td>
                    {order.remaining > 0
                      ? money(order.remaining)
                      : '—'}
                  </td>
                </tr>
              ))}

              {orders.length === 0 && (
                <tr>
                  <td colSpan={9} className="finance-empty-row">
                    В этой смене заказов пока нет.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {data.refunds.length > 0 && (
        <div className="finance-panel shift-refunds-panel">
          <div className="finance-panel-head">
            <div>
              <h2>Возвраты смены</h2>
              <p>Все возвраты, проведённые в этой смене.</p>
            </div>
          </div>

          <div className="refund-grid">
            {data.refunds.map((refund) => (
              <div className="refund-card" key={refund.id}>
                <div>
                  <strong>
                    {'Заказ #' +
                      refund.orderNumber +
                      ' · ' +
                      methodName(refund.method)}
                  </strong>
                  <span>
                    {(refund.reason || 'Без причины') +
                      ' · ' +
                      formatDate(refund.createdAt)}
                  </span>
                  <small>{refund.employeeName}</small>
                </div>
                <strong>{'−' + money(refund.amount)}</strong>
              </div>
            ))}
          </div>
        </div>
      )}

      {movement && (
        <CashMovementDialog
          state={movement}
          token={token}
          onClose={() => setMovement(null)}
          onSaved={async () => {
            setMovement(null);
            await onMutationSaved();
          }}
        />
      )}

      {closing && shift.status === 'OPEN' && (
        <CloseShiftDialog
          report={report}
          token={token}
          onClose={() => setClosing(false)}
          onSaved={async () => {
            setClosing(false);
            await onMutationSaved();
          }}
        />
      )}
    </section>
  );
}

function RevenueCard({
  label,
  value,
  detail,
  tone,
  emphasized = false,
}: {
  label: string;
  value: number;
  detail: string;
  tone: 'closed' | 'open' | 'expected';
  emphasized?: boolean;
}) {
  return (
    <div
      className={
        'revenue-card ' +
        tone +
        (emphasized ? ' emphasized' : '')
      }
    >
      <span>{label}</span>
      <strong>{money(value)}</strong>
      <small>{detail}</small>
    </div>
  );
}

function CompactKpi({
  label,
  value,
  detail,
  warning = false,
}: {
  label: string;
  value: string;
  detail: string;
  warning?: boolean;
}) {
  return (
    <div className={'compact-kpi ' + (warning ? 'warning' : '')}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </div>
  );
}

function DetailMeta({ label, value }: { label: string; value: string }) {
  return (
    <div className="detail-meta">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function CashMovementDialog({
  state,
  token,
  onClose,
  onSaved,
}: {
  state: Exclude<CashMovementState, null>;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [amount, setAmount] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const numericAmount = Number(amount.replace(',', '.'));
  const valid =
    Number.isFinite(numericAmount) &&
    numericAmount > 0 &&
    reason.trim().length > 0;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!valid || saving) return;

    setSaving(true);
    setError(null);
    try {
      await addShiftCashTransaction(token, state.shiftId, {
        type: state.type,
        amount: numericAmount,
        reason: reason.trim(),
      });
      await onSaved();
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : 'Не удалось выполнить операцию',
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <form className="modal-card finance-cash-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">КАССОВАЯ ОПЕРАЦИЯ</div>
            <h2>
              {state.type === 'DEPOSIT'
                ? 'Внесение наличных'
                : 'Изъятие наличных'}
            </h2>
            <p>{state.deviceName}</p>
          </div>
          <button
            type="button"
            className="close-button"
            onClick={onClose}
          >
            ×
          </button>
        </div>

        <label>
          <span>Сумма</span>
          <input
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            inputMode="decimal"
            placeholder="0.00"
            autoFocus
          />
        </label>

        <label>
          <span>Причина *</span>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={500}
            rows={3}
            placeholder={
              state.type === 'DEPOSIT'
                ? 'Например: разменные деньги'
                : 'Например: инкассация'
            }
          />
        </label>

        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button
            type="button"
            className="secondary-button"
            onClick={onClose}
          >
            Отмена
          </button>
          <button
            className="primary-button"
            disabled={!valid || saving}
          >
            {saving ? 'Сохраняем…' : 'Провести'}
          </button>
        </div>
      </form>
    </div>
  );
}

function CloseShiftDialog({
  report,
  token,
  onClose,
  onSaved,
}: {
  report: FinanceShiftReport;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [closingCash, setClosingCash] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const actual = Number(closingCash.replace(',', '.'));
  const validActual = Number.isFinite(actual) && actual >= 0;
  const difference = validActual ? actual - report.expectedCash : 0;
  const requiresReason =
    validActual && Math.abs(difference) >= 0.01;
  const valid =
    validActual &&
    (!requiresReason || reason.trim().length > 0);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!valid || saving) return;

    setSaving(true);
    setError(null);
    try {
      await closeShiftFromBackOffice(token, report.shiftId, {
        closingCash: actual,
        reason: reason.trim() || null,
      });
      await onSaved();
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : 'Не удалось закрыть смену',
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      className="modal-backdrop"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <form className="modal-card finance-close-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">Z-ОТЧЁТ</div>
            <h2>Закрытие смены</h2>
            <p>{report.deviceName ?? 'POS'}</p>
          </div>
          <button
            type="button"
            className="close-button"
            onClick={onClose}
          >
            ×
          </button>
        </div>

        <div className="close-shift-summary">
          <span>Ожидаемая наличность</span>
          <strong>{money(report.expectedCash)}</strong>
        </div>

        <label>
          <span>Фактически посчитано в кассе</span>
          <input
            value={closingCash}
            onChange={(e) => setClosingCash(e.target.value)}
            inputMode="decimal"
            placeholder="0.00"
            autoFocus
          />
        </label>

        {validActual && (
          <div
            className={
              'close-difference ' +
              (requiresReason ? 'has-difference' : 'balanced')
            }
          >
            <span>Расхождение</span>
            <strong>{signedMoney(difference)}</strong>
          </div>
        )}

        <label>
          <span>
            Комментарий {requiresReason ? '*' : '(необязательно)'}
          </span>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={500}
            rows={3}
            placeholder={
              requiresReason
                ? 'Обязательно укажите причину расхождения'
                : 'Комментарий к закрытию смены'
            }
          />
        </label>

        {requiresReason && !reason.trim() && (
          <div className="employee-inline-warning">
            При расхождении комментарий обязателен.
          </div>
        )}
        {error && <div className="error-box">{error}</div>}

        <div className="modal-actions">
          <button
            type="button"
            className="secondary-button"
            onClick={onClose}
          >
            Отмена
          </button>
          <button
            className="danger-button"
            disabled={!valid || saving}
          >
            {saving ? 'Закрываем…' : 'Закрыть смену'}
          </button>
        </div>
      </form>
    </div>
  );
}

function CashMetric({
  label,
  value,
  warning = false,
  emphasized = false,
}: {
  label: string;
  value: number;
  warning?: boolean;
  emphasized?: boolean;
}) {
  return (
    <div
      className={
        'cash-metric ' +
        (warning ? 'warning ' : '') +
        (emphasized ? 'emphasized' : '')
      }
    >
      <span>{label}</span>
      <strong>
        {value < 0 ? '−' + money(Math.abs(value)) : money(value)}
      </strong>
    </div>
  );
}

function registerName(
  data: BackOfficeFinance | null,
  id: string,
) {
  return (
    data?.cashRegisters.find((register) => register.id === id)?.name ??
    'Касса'
  );
}

function presetRange(
  preset: Exclude<PeriodPreset, 'custom'>,
): { fromDate: string; toDate: string } {
  const today = startOfLocalDay(new Date());

  if (preset === 'today') {
    const value = dateInputValue(today);
    return { fromDate: value, toDate: value };
  }

  if (preset === 'yesterday') {
    const yesterday = addDays(today, -1);
    const value = dateInputValue(yesterday);
    return { fromDate: value, toDate: value };
  }

  const days = preset === '7d' ? 6 : 29;
  return {
    fromDate: dateInputValue(addDays(today, -days)),
    toDate: dateInputValue(today),
  };
}

function periodBounds(
  fromDate: string,
  toDate: string,
): { from: string; to: string } {
  const from = new Date(fromDate + 'T00:00:00');
  const to = new Date(toDate + 'T00:00:00');
  to.setDate(to.getDate() + 1);

  return {
    from: from.toISOString(),
    to: to.toISOString(),
  };
}

function humanPeriod(fromDate: string, toDate: string) {
  if (fromDate === toDate) {
    return formatInputDateHuman(fromDate);
  }
  return (
    formatInputDateHuman(fromDate) +
    ' — ' +
    formatInputDateHuman(toDate)
  );
}

function formatInputDateHuman(value: string) {
  const date = new Date(value + 'T00:00:00');
  return date.toLocaleDateString('ru-RU', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

function startOfLocalDay(value: Date) {
  return new Date(
    value.getFullYear(),
    value.getMonth(),
    value.getDate(),
  );
}

function addDays(value: Date, days: number) {
  const copy = new Date(value);
  copy.setDate(copy.getDate() + days);
  return copy;
}

function dateInputValue(value: Date) {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  const day = String(value.getDate()).padStart(2, '0');
  return year + '-' + month + '-' + day;
}

function methodName(method: string) {
  if (method === 'CASH') return 'Наличные';
  if (method === 'CARD') return 'Карта';
  return 'Другое';
}

function orderStatusName(status: string) {
  if (status === 'DRAFT') return 'Черновик';
  if (status === 'OPEN') return 'Открыт';
  if (status === 'PARTIALLY_SENT') return 'Частично отправлен';
  if (status === 'SENT') return 'На кухне';
  if (status === 'PARTIALLY_PAID') return 'Частично оплачен';
  if (status === 'PAID') return 'Оплачен';
  if (status === 'CLOSED') return 'Закрыт';
  if (status === 'CANCELLED') return 'Отменён';
  return status;
}

function orderStatusTone(status: string) {
  if (status === 'CLOSED' || status === 'PAID') return 'success';
  if (status === 'CANCELLED') return 'neutral';
  return 'warning';
}

function money(value: number, currency = 'AZN') {
  return value.toFixed(2) + ' ' + currency;
}

function signedMoney(value: number) {
  if (Math.abs(value) < 0.005) return money(0);
  return (value > 0 ? '+' : '−') + money(Math.abs(value));
}

function differenceClass(value: number) {
  return Math.abs(value) >= 0.01
    ? 'finance-difference-warning'
    : 'finance-difference-ok';
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('ru-RU', {
        day: '2-digit',
        month: '2-digit',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
}

function formatShortDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleDateString('ru-RU', {
        day: '2-digit',
        month: '2-digit',
      });
}

function formatTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? ''
    : date.toLocaleTimeString('ru-RU', {
        hour: '2-digit',
        minute: '2-digit',
      });
}
