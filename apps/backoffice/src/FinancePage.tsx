import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeFinance,
  type FinanceShiftReport,
  addShiftCashTransaction,
  closeShiftFromBackOffice,
  getBackOfficeFinance,
} from './api';
import './finance.css';

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
  const [data, setData] = useState<BackOfficeFinance | null>(null);
  const [shiftId, setShiftId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [movement, setMovement] = useState<CashMovementState>(null);
  const [closing, setClosing] = useState(false);

  async function refresh(selectedShift = shiftId) {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeFinance(token, selectedShift || null));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить кассовые данные');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh('');
  }, [token]);

  const totals = useMemo(() => {
    const payments = data?.payments ?? [];
    const refundsList = data?.refunds ?? [];
    const gross = payments.reduce((sum, payment) => sum + payment.amount, 0);
    const refunds = refundsList.reduce((sum, refund) => sum + refund.amount, 0);
    return { gross, refunds, net: gross - refunds, count: payments.length };
  }, [data]);

  function changeShift(value: string) {
    setShiftId(value);
    void refresh(value);
  }

  const selectedShift =
    data?.shifts.find((shift) => shift.id === shiftId) ?? null;
  const report = data?.selectedShiftReport ?? null;

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">КАССА И СМЕНЫ</div>
          <h1>Кассовый контроль</h1>
          <p>
            X/Z-отчёты, ожидаемая наличность, внесения, изъятия,
            оплаты, возвраты и расхождения по каждой кассе.
          </p>
        </div>
        <div className="heading-actions finance-filter-actions">
          <select value={shiftId} onChange={(e) => changeShift(e.target.value)}>
            <option value="">Все последние операции</option>
            {data?.shifts.map((shift) => (
              <option key={shift.id} value={shift.id}>
                {shift.deviceName + ' · ' + formatDate(shift.openedAt) + ' · ' +
                  (shift.status === 'OPEN' ? 'Открыта' : 'Закрыта')}
              </option>
            ))}
          </select>
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>
            Обновить
          </button>
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="stats-grid finance-stats">
        <FinanceStat
          label="Продажи"
          value={money(totals.gross)}
          detail={String(totals.count) + ' оплат'}
        />
        <FinanceStat
          label="Возвраты"
          value={money(totals.refunds)}
          detail="по выбранному журналу"
          warning={totals.refunds > 0}
        />
        <FinanceStat
          label="Нетто"
          value={money(totals.net)}
          detail="продажи минус возвраты"
        />
        <FinanceStat
          label="Открытые смены"
          value={String(data?.openShifts.length ?? 0)}
          detail="сейчас работают"
        />
      </div>

      {(data?.openShifts.length ?? 0) > 0 && (
        <div className="finance-panel finance-open-shifts-panel">
          <div className="finance-panel-head">
            <div>
              <h2>Открытые кассы</h2>
              <p>Текущее состояние наличности по работающим POS.</p>
            </div>
          </div>
          <div className="open-shift-grid">
            {data!.openShifts.map((shift) => (
              <button
                className="open-shift-card"
                key={shift.id}
                onClick={() => changeShift(shift.id)}
              >
                <div className="open-shift-card-head">
                  <strong>{shift.deviceName ?? 'POS'}</strong>
                  <span className="badge success">Открыта</span>
                </div>
                <span>С {formatDate(shift.openedAt)}</span>
                <div className="open-shift-money">
                  <small>Ожидается в кассе</small>
                  <strong>{money(shift.expectedCash)}</strong>
                </div>
                <div className="open-shift-mini">
                  <span>Наличные продажи {money(shift.cashSales)}</span>
                  <span>Внесено {money(shift.deposits)}</span>
                  <span>Изъято {money(shift.withdrawals)}</span>
                </div>
              </button>
            ))}
          </div>
        </div>
      )}

      {report && selectedShift && (
        <ShiftReportPanel
          report={report}
          canManageShifts={canManageShifts}
          onCashMovement={(type) =>
            setMovement({
              shiftId: report.shiftId,
              deviceName: report.deviceName ?? selectedShift.deviceName,
              type,
            })
          }
          onCloseShift={() => setClosing(true)}
        />
      )}

      <div className="finance-panel">
        <div className="finance-panel-head">
          <div>
            <h2>Журнал оплат</h2>
            <p>
              {shiftId
                ? 'Оплаты выбранной смены.'
                : 'Последние операции по заказам.'}
            </p>
          </div>
          {loading && <span className="muted">Обновляем…</span>}
        </div>

        <div className="finance-table-wrap">
          <table className="finance-table">
            <thead>
              <tr>
                <th>Время</th>
                <th>Заказ</th>
                <th>Кассир</th>
                <th>Способ</th>
                <th>Сумма</th>
                <th>Возврат</th>
                <th>Статус</th>
              </tr>
            </thead>
            <tbody>
              {(data?.payments ?? []).map((payment) => (
                <tr key={payment.id}>
                  <td>{formatDate(payment.createdAt)}</td>
                  <td><strong>{'#' + payment.orderNumber}</strong></td>
                  <td>{payment.employeeName}</td>
                  <td>{methodName(payment.method)}</td>
                  <td>
                    <strong>{money(payment.amount, payment.currencyCode)}</strong>
                    {payment.method === 'CASH' && payment.tenderedAmount != null && (
                      <small className="cell-subline">
                        {'получено ' + money(payment.tenderedAmount, payment.currencyCode)}
                        {payment.changeAmount > 0
                          ? ' · сдача ' + money(payment.changeAmount, payment.currencyCode)
                          : ''}
                      </small>
                    )}
                  </td>
                  <td>
                    {payment.refundedAmount > 0
                      ? money(payment.refundedAmount, payment.currencyCode)
                      : '—'}
                  </td>
                  <td>
                    <span
                      className={
                        'badge ' +
                        (payment.status === 'REFUNDED' ? 'neutral' : 'success')
                      }
                    >
                      {payment.status === 'REFUNDED' ? 'Возвращено' : 'Оплачено'}
                    </span>
                  </td>
                </tr>
              ))}
              {!loading && (data?.payments.length ?? 0) === 0 && (
                <tr>
                  <td colSpan={7} className="finance-empty-row">Оплат пока нет.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div className="finance-two-column">
        <div className="finance-panel">
          <div className="finance-panel-head">
            <div>
              <h2>История смен</h2>
              <p>Последние кассовые смены ресторана.</p>
            </div>
          </div>
          <div className="finance-list">
            {(data?.shifts ?? []).map((shift) => (
              <button
                className={'finance-list-row finance-shift-row ' +
                  (shift.id === shiftId ? 'selected' : '')}
                key={shift.id}
                onClick={() => changeShift(shift.id)}
              >
                <div>
                  <strong>{shift.deviceName}</strong>
                  <span>
                    {formatDate(shift.openedAt)}
                    {shift.openedByEmployeeName
                      ? ' · ' + shift.openedByEmployeeName
                      : ''}
                  </span>
                </div>
                <div className="finance-list-right">
                  <span className={'badge ' + (shift.status === 'OPEN' ? 'success' : 'neutral')}>
                    {shift.status === 'OPEN' ? 'Открыта' : 'Закрыта'}
                  </span>
                  <small>
                    {'старт ' + money(shift.openingCash)}
                    {shift.closingCash != null
                      ? ' · факт ' + money(shift.closingCash)
                      : ''}
                  </small>
                  {shift.cashDifference != null && (
                    <small className={differenceClass(shift.cashDifference)}>
                      {'расхождение ' + signedMoney(shift.cashDifference)}
                    </small>
                  )}
                </div>
              </button>
            ))}
          </div>
        </div>

        <div className="finance-panel">
          <div className="finance-panel-head">
            <div>
              <h2>Возвраты</h2>
              <p>Возвраты по показанным операциям.</p>
            </div>
          </div>
          <div className="finance-list">
            {(data?.refunds ?? []).map((refund) => (
              <div className="finance-list-row" key={refund.id}>
                <div>
                  <strong>{'Заказ #' + refund.orderNumber + ' · ' + methodName(refund.method)}</strong>
                  <span>
                    {(refund.reason || 'Без причины') + ' · ' +
                      formatDate(refund.createdAt)}
                  </span>
                </div>
                <div className="finance-list-right">
                  <strong>{'-' + money(refund.amount, refund.currencyCode)}</strong>
                  <small>{refund.employeeName}</small>
                </div>
              </div>
            ))}
            {(data?.refunds.length ?? 0) === 0 && (
              <div className="finance-empty-box">Возвратов нет.</div>
            )}
          </div>
        </div>
      </div>

      {movement && (
        <CashMovementDialog
          state={movement}
          token={token}
          onClose={() => setMovement(null)}
          onSaved={async () => {
            setMovement(null);
            await refresh(movement.shiftId);
          }}
        />
      )}

      {closing && report && report.status === 'OPEN' && (
        <CloseShiftDialog
          report={report}
          token={token}
          onClose={() => setClosing(false)}
          onSaved={async () => {
            setClosing(false);
            await refresh(report.shiftId);
          }}
        />
      )}
    </section>
  );
}

function ShiftReportPanel({
  report,
  canManageShifts,
  onCashMovement,
  onCloseShift,
}: {
  report: FinanceShiftReport;
  canManageShifts: boolean;
  onCashMovement: (type: 'DEPOSIT' | 'WITHDRAWAL') => void;
  onCloseShift: () => void;
}) {
  return (
    <div className="finance-panel shift-report-panel">
      <div className="finance-panel-head shift-report-head">
        <div>
          <div className="shift-report-title-row">
            <span className={'report-type-badge ' + report.reportType.toLowerCase()}>
              {report.reportType}-ОТЧЁТ
            </span>
            <h2>{report.deviceName ?? 'POS'}</h2>
          </div>
          <p>
            Открыта {formatDate(report.openedAt)}
            {report.openedByEmployeeName
              ? ' · ' + report.openedByEmployeeName
              : ''}
            {report.closedAt
              ? ' · закрыта ' + formatDate(report.closedAt)
              : ''}
          </p>
        </div>
        {report.status === 'OPEN' && canManageShifts && (
          <div className="shift-report-actions">
            <button className="secondary-button" onClick={() => onCashMovement('DEPOSIT')}>
              + Внесение
            </button>
            <button className="secondary-button" onClick={() => onCashMovement('WITHDRAWAL')}>
              − Изъятие
            </button>
            <button className="danger-button" onClick={onCloseShift}>
              Закрыть смену
            </button>
          </div>
        )}
      </div>

      <div className="shift-cash-grid">
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
          <CashMetric label="Фактически посчитано" value={report.closingCash} />
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
        <div className="shift-closing-note">
          <strong>Комментарий при закрытии</strong>
          <span>{report.closingNote}</span>
        </div>
      )}

      <div className="shift-report-columns">
        <div>
          <h3>Продажи по способам оплаты</h3>
          <div className="payment-method-list">
            {report.payments.map((row) => (
              <div className="payment-method-row" key={row.method}>
                <div>
                  <strong>{methodName(row.method)}</strong>
                  <small>
                    {'продажи ' + money(row.gross) +
                      (row.refunds > 0 ? ' · возвраты ' + money(row.refunds) : '')}
                  </small>
                </div>
                <strong>{money(row.net)}</strong>
              </div>
            ))}
            {report.payments.length === 0 && (
              <div className="finance-empty-box">Оплат в смене пока нет.</div>
            )}
          </div>
          <div className="shift-sales-total">
            <span>Нетто продаж</span>
            <strong>{money(report.netSales)}</strong>
            <small>
              {report.ordersCount + ' заказов · ' +
                report.paymentsCount + ' оплат'}
            </small>
          </div>
        </div>

        <div>
          <h3>Движение наличности</h3>
          <div className="cash-movement-list">
            {report.cashTransactions.map((entry) => (
              <div className="cash-movement-row" key={entry.id}>
                <div>
                  <strong>
                    {entry.type === 'DEPOSIT' ? 'Внесение' : 'Изъятие'}
                  </strong>
                  <span>
                    {(entry.reason || 'Без комментария') + ' · ' +
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
      setError(e instanceof Error ? e.message : 'Не удалось выполнить операцию');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card finance-cash-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">КАССОВАЯ ОПЕРАЦИЯ</div>
            <h2>{state.type === 'DEPOSIT' ? 'Внесение наличных' : 'Изъятие наличных'}</h2>
            <p>{state.deviceName}</p>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
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
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={!valid || saving}>
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
  const requiresReason = validActual && Math.abs(difference) >= 0.01;
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
      setError(e instanceof Error ? e.message : 'Не удалось закрыть смену');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card finance-close-modal" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">Z-ОТЧЁТ</div>
            <h2>Закрытие смены</h2>
            <p>{report.deviceName ?? 'POS'}</p>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
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
          <div className={'close-difference ' +
            (requiresReason ? 'has-difference' : 'balanced')}
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
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="danger-button" disabled={!valid || saving}>
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
    <div className={
      'cash-metric ' +
      (warning ? 'warning ' : '') +
      (emphasized ? 'emphasized' : '')
    }>
      <span>{label}</span>
      <strong>{value < 0 ? '−' + money(Math.abs(value)) : money(value)}</strong>
    </div>
  );
}

function FinanceStat({
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
    <div className={'stat-card ' + (warning ? 'stat-warning' : '')}>
      <span>{label}</span>
      <div>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </div>
  );
}

function methodName(method: string) {
  if (method === 'CASH') return 'Наличные';
  if (method === 'CARD') return 'Карта';
  return 'Другое';
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
