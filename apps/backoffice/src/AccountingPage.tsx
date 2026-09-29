import { useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeAccounting,
  getBackOfficeAccounting,
} from './api';
import './accounting.css';

type Tab = 'accounts' | 'journal';

export function AccountingPage({ token }: { token: string }) {
  const today = dateValue(new Date());
  const monthAgo = dateValue(new Date(Date.now() - 29 * 86400000));
  const [fromDate, setFromDate] = useState(monthAgo);
  const [toDate, setToDate] = useState(today);
  const [tab, setTab] = useState<Tab>('accounts');
  const [data, setData] = useState<BackOfficeAccounting | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load(from = fromDate, to = toDate) {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeAccounting(token, dayStartIso(from), dayAfterIso(to)));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить план счетов');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(monthAgo, today);
  }, [token]);

  const summary = useMemo(() => {
    const accounts = data?.accounts ?? [];
    return {
      assets: accounts.filter((x) => x.type === 'ASSET').reduce((sum, x) => sum + x.balance, 0),
      liabilities: accounts.filter((x) => x.type === 'LIABILITY').reduce((sum, x) => sum + x.balance, 0),
      income: accounts.filter((x) => x.type === 'INCOME').reduce((sum, x) => sum + x.balance, 0),
      expense: accounts.filter((x) => x.type === 'EXPENSE').reduce((sum, x) => sum + x.balance, 0),
    };
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем план счетов…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">ФИНАНСЫ</div>
          <h1>План счетов</h1>
          <p>
            Единый журнал проводок: склад, поставщики, продажи, возвраты,
            касса и ручные денежные операции.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void load()} disabled={loading}>
            Обновить
          </button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="accounting-filter-bar">
        <label>
          <span>С</span>
          <input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
        </label>
        <label>
          <span>По</span>
          <input type="date" value={toDate} min={fromDate} onChange={(e) => setToDate(e.target.value)} />
        </label>
        <button
          className="primary-button compact"
          disabled={!fromDate || !toDate || toDate < fromDate}
          onClick={() => void load()}
        >
          Показать
        </button>
      </div>

      <div className="stats-grid accounting-stats">
        <Stat label="Активы" value={money(summary.assets)} />
        <Stat label="Обязательства" value={money(summary.liabilities)} />
        <Stat label="Доходы" value={money(summary.income)} />
        <Stat label="Расходы" value={money(summary.expense)} />
      </div>

      <div className="supplier-tabs accounting-tabs">
        <button className={tab === 'accounts' ? 'active' : ''} onClick={() => setTab('accounts')}>
          Счета
        </button>
        <button className={tab === 'journal' ? 'active' : ''} onClick={() => setTab('journal')}>
          Журнал проводок
        </button>
      </div>

      {tab === 'accounts' ? (
        <div className="accounting-panel">
          <div className="accounting-table-wrap">
            <table className="accounting-table">
              <thead>
                <tr>
                  <th>Код</th>
                  <th>Счёт</th>
                  <th>Тип</th>
                  <th>Дебет</th>
                  <th>Кредит</th>
                  <th>Баланс</th>
                  <th>Оборот Дт</th>
                  <th>Оборот Кт</th>
                </tr>
              </thead>
              <tbody>
                {(data?.accounts ?? []).map((account) => (
                  <tr key={account.id}>
                    <td><span className="account-code">{account.code}</span></td>
                    <td>
                      <strong>{account.name}</strong>
                      {account.isSystem && <small className="account-system">системный</small>}
                    </td>
                    <td>{typeLabel(account.type)}</td>
                    <td>{money(account.debit)}</td>
                    <td>{money(account.credit)}</td>
                    <td><strong>{money(account.balance)}</strong></td>
                    <td>{money(account.periodDebit)}</td>
                    <td>{money(account.periodCredit)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : (
        <div className="accounting-journal">
          {(data?.entries ?? []).length === 0 ? (
            <div className="stock-op-empty">Проводок за выбранный период пока нет.</div>
          ) : (
            (data?.entries ?? []).map((entry) => (
              <div className="journal-entry" key={entry.id}>
                <div className="journal-entry-head">
                  <div>
                    <strong>{entry.description}</strong>
                    <small>{dateTime(entry.occurredAt)} · {entry.referenceType}</small>
                  </div>
                  <span>{entry.lines.length} строк</span>
                </div>
                <div className="accounting-table-wrap">
                  <table className="accounting-table compact-table">
                    <thead>
                      <tr>
                        <th>Счёт</th>
                        <th>Дебет</th>
                        <th>Кредит</th>
                      </tr>
                    </thead>
                    <tbody>
                      {entry.lines.map((line) => (
                        <tr key={line.id}>
                          <td>
                            <span className="account-code">{line.accountCode}</span>
                            <strong>{line.accountName}</strong>
                          </td>
                          <td>{line.debit ? money(line.debit) : '—'}</td>
                          <td>{line.credit ? money(line.credit) : '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ))
          )}
        </div>
      )}
    </section>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return <div className="stat-card"><span>{label}</span><div><strong>{value}</strong></div></div>;
}

function typeLabel(value: string) {
  return ({
    ASSET: 'Актив',
    LIABILITY: 'Обязательство',
    EQUITY: 'Капитал / служебный',
    INCOME: 'Доход',
    EXPENSE: 'Расход',
  } as Record<string, string>)[value] ?? value;
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value) + ' ₼';
}

function dateTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', {
    dateStyle: 'short',
    timeStyle: 'short',
  }).format(new Date(value));
}

function dateValue(value: Date) {
  const y = value.getFullYear();
  const m = String(value.getMonth() + 1).padStart(2, '0');
  const d = String(value.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

function dayStartIso(value: string) {
  return new Date(value + 'T00:00:00').toISOString();
}

function dayAfterIso(value: string) {
  const date = new Date(value + 'T00:00:00');
  date.setDate(date.getDate() + 1);
  return date.toISOString();
}
