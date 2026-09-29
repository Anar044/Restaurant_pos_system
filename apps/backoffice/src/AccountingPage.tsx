import { useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeAccounting,
  type LedgerAccount,
  type LedgerAccountDetails,
  getBackOfficeAccounting,
  getBackOfficeAccountMovements,
} from './api';
import './accounting.css';

const SECTION_DEFS = [
  { code: '1', title: 'Долгосрочные активы', az: 'Uzunmüddətli aktivlər' },
  { code: '2', title: 'Краткосрочные активы', az: 'Qısamüddətli aktivlər' },
  { code: '3', title: 'Капитал', az: 'Kapital' },
  { code: '4', title: 'Долгосрочные обязательства', az: 'Uzunmüddətli öhdəliklər' },
  { code: '5', title: 'Краткосрочные обязательства', az: 'Qısamüddətli öhdəliklər' },
  { code: '6', title: 'Доходы', az: 'Gəlirlər' },
  { code: '7', title: 'Расходы', az: 'Xərclər' },
  { code: '8', title: 'Прибыль / убыток', az: 'Mənfəətlər (zərərlər)' },
  { code: '9', title: 'Налог на прибыль', az: 'Mənfəət vergisi' },
] as const;

export function AccountingPage({ token }: { token: string }) {
  const today = dateValue(new Date());
  const monthAgo = dateValue(new Date(Date.now() - 29 * 86400000));
  const [fromDate, setFromDate] = useState(monthAgo);
  const [toDate, setToDate] = useState(today);
  const [data, setData] = useState<BackOfficeAccounting | null>(null);
  const [selectedAccount, setSelectedAccount] = useState<LedgerAccount | null>(null);
  const [accountDetails, setAccountDetails] = useState<LedgerAccountDetails | null>(null);
  const [expandedSections, setExpandedSections] = useState<Set<string>>(
    () => new Set(['2', '5', '6', '7']),
  );
  const [loading, setLoading] = useState(true);
  const [detailsLoading, setDetailsLoading] = useState(false);
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

  async function loadAccount(account: LedgerAccount, from = fromDate, to = toDate) {
    setSelectedAccount(account);
    setDetailsLoading(true);
    setError(null);
    try {
      setAccountDetails(
        await getBackOfficeAccountMovements(
          token,
          account.id,
          dayStartIso(from),
          dayAfterIso(to),
        ),
      );
    } catch (e) {
      setAccountDetails(null);
      setError(e instanceof Error ? e.message : 'Не удалось загрузить движения по счёту');
    } finally {
      setDetailsLoading(false);
    }
  }

  async function applyPeriod() {
    await load(fromDate, toDate);
    if (selectedAccount) {
      await loadAccount(selectedAccount, fromDate, toDate);
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
      equity: accounts.filter((x) => x.type === 'EQUITY').reduce((sum, x) => sum + x.balance, 0),
      income: accounts.filter((x) => x.type === 'INCOME').reduce((sum, x) => sum + x.balance, 0),
      expense: accounts.filter((x) => x.type === 'EXPENSE').reduce((sum, x) => sum + x.balance, 0),
    };
  }, [data]);

  const sections = useMemo(() => {
    const accounts = data?.accounts ?? [];
    return SECTION_DEFS.map((section) => {
      const sectionAccounts = accounts.filter((account) => account.code.trim().startsWith(section.code));
      return {
        ...section,
        accounts: sectionAccounts,
        balance: sectionAccounts.reduce((sum, account) => sum + account.balance, 0),
        periodDebit: sectionAccounts.reduce((sum, account) => sum + account.periodDebit, 0),
        periodCredit: sectionAccounts.reduce((sum, account) => sum + account.periodCredit, 0),
      };
    });
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем план счетов…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">ФИНАНСЫ</div>
          <h1>{selectedAccount ? 'Карточка счёта' : 'План счетов'}</h1>
          <p>
            {selectedAccount
              ? 'Движения по счёту, дебет, кредит, остаток и корреспондирующие счета.'
              : 'План счетов Азербайджана сгруппирован по официальным разделам 1–9.'}
          </p>
        </div>
        <div className="heading-actions">
          {selectedAccount && (
            <button
              className="secondary-button"
              onClick={() => {
                setSelectedAccount(null);
                setAccountDetails(null);
              }}
            >
              ← К плану счетов
            </button>
          )}
          <button
            className="secondary-button"
            onClick={() => selectedAccount ? void loadAccount(selectedAccount) : void load()}
            disabled={loading || detailsLoading}
          >
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
          disabled={!fromDate || !toDate || toDate < fromDate || loading || detailsLoading}
          onClick={() => void applyPeriod()}
        >
          Показать
        </button>
      </div>

      {selectedAccount ? (
        <AccountDetails
          account={selectedAccount}
          details={accountDetails}
          loading={detailsLoading}
        />
      ) : (
        <>
          <div className="stats-grid accounting-stats accounting-stats-five">
            <Stat label="Активы" value={money(summary.assets)} />
            <Stat label="Обязательства" value={money(summary.liabilities)} />
            <Stat label="Капитал" value={money(summary.equity)} />
            <Stat label="Доходы" value={money(summary.income)} />
            <Stat label="Расходы" value={money(summary.expense)} />
          </div>

          <div className="chart-sections">
            {sections.map((section) => {
              const expanded = expandedSections.has(section.code);
              return (
                <div className="chart-section" key={section.code}>
                  <button
                    type="button"
                    className="chart-section-head"
                    onClick={() => {
                      setExpandedSections((current) => {
                        const next = new Set(current);
                        if (next.has(section.code)) next.delete(section.code);
                        else next.add(section.code);
                        return next;
                      });
                    }}
                  >
                    <div className="chart-section-title">
                      <span className="chart-section-number">{section.code}</span>
                      <div>
                        <strong>{section.title}</strong>
                        <small>{section.az}</small>
                      </div>
                    </div>
                    <div className="chart-section-summary">
                      <span>{section.accounts.length} счетов</span>
                      <span>Дт {money(section.periodDebit)}</span>
                      <span>Кт {money(section.periodCredit)}</span>
                      <strong>{money(section.balance)}</strong>
                      <b>{expanded ? '−' : '+'}</b>
                    </div>
                  </button>

                  {expanded && (
                    <div className="chart-section-body">
                      {section.accounts.length === 0 ? (
                        <div className="chart-section-empty">В этом разделе пока нет используемых счетов.</div>
                      ) : (
                        <div className="accounting-table-wrap">
                          <table className="accounting-table chart-account-table">
                            <thead>
                              <tr>
                                <th>Код</th>
                                <th>Счёт</th>
                                <th>Оборот Дт</th>
                                <th>Оборот Кт</th>
                                <th>Баланс</th>
                                <th></th>
                              </tr>
                            </thead>
                            <tbody>
                              {section.accounts.map((account) => (
                                <tr
                                  key={account.id}
                                  className="account-drill-row"
                                  onClick={() => void loadAccount(account)}
                                >
                                  <td><span className="account-code">{account.code}</span></td>
                                  <td>
                                    <strong>{account.name}</strong>
                                    {account.isSystem && <small className="account-system">системный</small>}
                                  </td>
                                  <td>{money(account.periodDebit)}</td>
                                  <td>{money(account.periodCredit)}</td>
                                  <td><strong>{money(account.balance)}</strong></td>
                                  <td><span className="account-open-link">Открыть →</span></td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </>
      )}
    </section>
  );
}

function AccountDetails({
  account,
  details,
  loading,
}: {
  account: LedgerAccount;
  details: LedgerAccountDetails | null;
  loading: boolean;
}) {
  if (loading && !details) {
    return <div className="empty-state">Загружаем движения по счёту…</div>;
  }

  if (!details) {
    return <div className="empty-state">Движения по счёту не загружены.</div>;
  }

  return (
    <div className="account-detail">
      <div className="account-detail-title">
        <span className="account-detail-code">{account.code}</span>
        <div>
          <h2>{account.name}</h2>
          <small>{sectionName(account.code)} · {typeLabel(account.type)}</small>
        </div>
      </div>

      <div className="stats-grid accounting-stats account-detail-stats">
        <Stat label="Остаток на начало" value={money(details.openingBalance)} />
        <Stat label="Оборот по дебету" value={money(details.periodDebit)} />
        <Stat label="Оборот по кредиту" value={money(details.periodCredit)} />
        <Stat label="Остаток на конец" value={money(details.closingBalance)} />
      </div>

      <div className="account-movements-panel">
        <div className="account-movements-head">
          <div>
            <strong>Движения по счёту</strong>
            <span>{details.movements.length} операций за выбранный период</span>
          </div>
          <div className="account-movement-legend">
            <span>Дт — дебет</span>
            <span>Кт — кредит</span>
          </div>
        </div>

        {details.movements.length === 0 ? (
          <div className="chart-section-empty">За выбранный период движений нет.</div>
        ) : (
          <div className="accounting-table-wrap">
            <table className="accounting-table movement-table">
              <thead>
                <tr>
                  <th>Дата</th>
                  <th>Операция</th>
                  <th>Корреспондирующий счёт</th>
                  <th>Дебет</th>
                  <th>Кредит</th>
                  <th>Остаток</th>
                </tr>
              </thead>
              <tbody>
                {details.movements.map((movement) => (
                  <tr key={movement.id}>
                    <td>
                      <span className="movement-date">{dateTime(movement.occurredAt)}</span>
                    </td>
                    <td>
                      <strong>{movement.description}</strong>
                      <small className="account-system">{referenceLabel(movement.referenceType)}</small>
                      {movement.analytics && (
                        <small className="movement-analytics">{movement.analytics}</small>
                      )}
                    </td>
                    <td>
                      <div className="correspondents">
                        {movement.correspondents.length === 0 ? (
                          <span>—</span>
                        ) : (
                          movement.correspondents.map((row) => (
                            <div className="correspondent-row" key={row.id}>
                              <span className="account-code">{row.accountCode}</span>
                              <div>
                                <strong>{row.accountName}</strong>
                                <small>
                                  {row.debit > 0 ? 'Дт ' + money(row.debit) : ''}
                                  {row.credit > 0 ? 'Кт ' + money(row.credit) : ''}
                                  {row.analytics ? ' · ' + row.analytics : ''}
                                </small>
                              </div>
                            </div>
                          ))
                        )}
                      </div>
                    </td>
                    <td className="amount-cell debit-cell">
                      {movement.debit > 0 ? money(movement.debit) : '—'}
                    </td>
                    <td className="amount-cell credit-cell">
                      {movement.credit > 0 ? money(movement.credit) : '—'}
                    </td>
                    <td className="amount-cell"><strong>{money(movement.balanceAfter)}</strong></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return <div className="stat-card"><span>{label}</span><div><strong>{value}</strong></div></div>;
}

function typeLabel(value: string) {
  return ({
    ASSET: 'Актив',
    LIABILITY: 'Обязательство',
    EQUITY: 'Капитал',
    INCOME: 'Доход',
    EXPENSE: 'Расход',
  } as Record<string, string>)[value] ?? value;
}

function sectionName(code: string) {
  const section = SECTION_DEFS.find((item) => code.trim().startsWith(item.code));
  return section ? section.code + '. ' + section.title : 'Раздел не определён';
}

function referenceLabel(value: string) {
  return ({
    STOCK_RECEIPT: 'Приходная накладная',
    STOCK_WRITE_OFF: 'Списание',
    STOCK_TRANSFER: 'Перемещение',
    STOCK_INVENTORY: 'Инвентаризация',
    POS_PAYMENT: 'Оплата POS',
    POS_REFUND: 'Возврат POS',
    ORDER_COGS: 'Себестоимость продажи',
    SUPPLIER_PAYMENT: 'Оплата поставщику',
    MONEY_TRANSACTION: 'Денежная операция',
    SHIFT_CASH_TRANSACTION: 'Операция кассовой смены',
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
