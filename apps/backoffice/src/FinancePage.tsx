import { useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeFinance,
  getBackOfficeFinance,
} from './api';
import './finance.css';

export function FinancePage({ token }: { token: string }) {
  const [data, setData] = useState<BackOfficeFinance | null>(null);
  const [shiftId, setShiftId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh(selectedShift = shiftId) {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeFinance(token, selectedShift || null));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить оплаты');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh('');
  }, [token]);

  const totals = useMemo(() => {
    const payments = data?.payments ?? [];
    const gross = payments.reduce((sum, payment) => sum + payment.amount, 0);
    const refunds = payments.reduce((sum, payment) => sum + payment.refundedAmount, 0);
    return { gross, refunds, net: gross - refunds, count: payments.length };
  }, [data]);

  function changeShift(value: string) {
    setShiftId(value);
    void refresh(value);
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">КАССА И ОПЛАТЫ</div>
          <h1>Оплаты и смены</h1>
          <p>Журнал оплат, возвратов и кассовых смен ресторана.</p>
          <p className="muted">Возвраты выполняются на POS сотрудником с правом «Возврат оплат».</p>
        </div>
        <div className="heading-actions finance-filter-actions">
          <select value={shiftId} onChange={(e) => changeShift(e.target.value)}>
            <option value="">Все последние оплаты</option>
            {data?.shifts.map((shift) => (
              <option key={shift.id} value={shift.id}>
                {shift.deviceName + ' · ' + formatDate(shift.openedAt) + ' · ' + shift.status}
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
        <FinanceStat label="Продажи" value={money(totals.gross)} detail={String(totals.count) + ' оплат'} />
        <FinanceStat label="Возвраты" value={money(totals.refunds)} detail="по выбранному журналу" warning={totals.refunds > 0} />
        <FinanceStat label="Нетто" value={money(totals.net)} detail="продажи минус возвраты" />
        <FinanceStat label="Открытые смены" value={String(data?.openShifts.length ?? 0)} detail="сейчас работают" />
      </div>

      <div className="finance-panel">
        <div className="finance-panel-head">
          <div>
            <h2>Журнал оплат</h2>
            <p>Последние операции по заказам.</p>
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
                        {payment.changeAmount > 0 ? ' · сдача ' + money(payment.changeAmount, payment.currencyCode) : ''}
                      </small>
                    )}
                  </td>
                  <td>
                    {payment.refundedAmount > 0
                      ? money(payment.refundedAmount, payment.currencyCode)
                      : '—'}
                  </td>
                  <td>
                    <span className={'badge ' + (payment.status === 'REFUNDED' ? 'neutral' : 'success')}>
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
              <h2>Смены</h2>
              <p>Последние кассовые смены.</p>
            </div>
          </div>
          <div className="finance-list">
            {(data?.shifts ?? []).map((shift) => (
              <div className="finance-list-row" key={shift.id}>
                <div>
                  <strong>{shift.deviceName}</strong>
                  <span>{formatDate(shift.openedAt)}</span>
                </div>
                <div className="finance-list-right">
                  <span className={'badge ' + (shift.status === 'OPEN' ? 'success' : 'neutral')}>
                    {shift.status === 'OPEN' ? 'Открыта' : 'Закрыта'}
                  </span>
                  <small>
                    {'старт ' + money(shift.openingCash)}
                    {shift.closingCash != null ? ' · закрытие ' + money(shift.closingCash) : ''}
                  </small>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="finance-panel">
          <div className="finance-panel-head">
            <div>
              <h2>Возвраты</h2>
              <p>Последние возвраты по показанным оплатам.</p>
            </div>
          </div>
          <div className="finance-list">
            {(data?.refunds ?? []).map((refund) => (
              <div className="finance-list-row" key={refund.id}>
                <div>
                  <strong>{'Заказ #' + refund.orderNumber + ' · ' + methodName(refund.method)}</strong>
                  <span>{(refund.reason || 'Без причины') + ' · ' + formatDate(refund.createdAt)}</span>
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

    </section>
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
