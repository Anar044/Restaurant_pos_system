import { useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeAccounting,
  type LedgerAccount,
  type LedgerAccountDetails,
  type LedgerAccountMovement,
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
          key={selectedAccount.id}
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
  const [analyticFilter, setAnalyticFilter] = useState<string | null>(null);

  if (loading && !details) {
    return <div className="empty-state">Загружаем движения по счёту…</div>;
  }

  if (!details) {
    return <div className="empty-state">Движения по счёту не загружены.</div>;
  }

  const visibleMovements = analyticFilter
    ? details.movements.filter((movement) =>
        movement.analyticsKind && movement.analyticsId &&
        `${movement.analyticsKind}:${movement.analyticsId}` === analyticFilter)
    : details.movements;

  const selectedAnalytic = analyticFilter
    ? details.analytics.find((row) => `${row.kind}:${row.id}` === analyticFilter) ?? null
    : null;
  const displayMovements = analyticFilter
    ? visibleMovements
    : collapseInternalTransfers(visibleMovements, account);
  const analyticBalances = new Map<string, number>();
  if (selectedAnalytic) {
    let running = selectedAnalytic.openingBalance;
    [...visibleMovements].reverse().forEach((movement) => {
      running = applyNaturalMovement(account.type, running, movement.debit, movement.credit);
      analyticBalances.set(movement.id, running);
    });
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

      {details.analytics.length > 0 && (
        <div className="account-analytics-panel">
          <div className="account-movements-head">
            <div>
              <strong>Аналитика счёта</strong>
              <span>Склад, касса или поставщик внутри синтетического счёта.</span>
            </div>
            {analyticFilter && (
              <button
                type="button"
                className="secondary-button compact"
                onClick={() => setAnalyticFilter(null)}
              >
                Все движения
              </button>
            )}
          </div>
          <div className="accounting-table-wrap">
            <table className="accounting-table analytics-table">
              <thead>
                <tr>
                  <th>Аналитика</th>
                  <th>На начало</th>
                  <th>Оборот Дт</th>
                  <th>Оборот Кт</th>
                  <th>На конец</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {details.analytics.map((row) => {
                  const key = `${row.kind}:${row.id}`;
                  const active = analyticFilter === key;
                  return (
                    <tr
                      key={key}
                      className={active ? 'analytic-row active' : 'analytic-row'}
                      onClick={() => setAnalyticFilter(active ? null : key)}
                    >
                      <td>
                        <strong>{row.name}</strong>
                        <small className="account-system">{analyticKindLabel(row.kind)}</small>
                      </td>
                      <td>{money(row.openingBalance)}</td>
                      <td>{money(row.periodDebit)}</td>
                      <td>{money(row.periodCredit)}</td>
                      <td><strong>{money(row.closingBalance)}</strong></td>
                      <td><span className="account-open-link">{active ? 'Показаны движения' : 'Движения →'}</span></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <div className="account-movements-panel">
        <div className="account-movements-head">
          <div>
            <strong>Движения по счёту</strong>
            <span>
              {displayMovements.length} операций
              {analyticFilter ? ' по выбранной аналитике' : ' за выбранный период'}
            </span>
          </div>
          <div className="account-movement-legend">
            <span>Дт — дебет</span>
            <span>Кт — кредит</span>
          </div>
        </div>

        {displayMovements.length === 0 ? (
          <div className="chart-section-empty">По выбранным условиям движений нет.</div>
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
                {displayMovements.map((movement) => (
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
                      {movement.internalTransfer ? (
                        <div className="correspondents">
                          <div className="correspondent-row">
                            <span className="account-code">{account.code}</span>
                            <div>
                              <strong>{account.name}</strong>
                              <small>
                                Внутри счёта
                                {movement.transferLabel ? ' · ' + movement.transferLabel : ''}
                              </small>
                            </div>
                          </div>
                        </div>
                      ) : (
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
                      )}
                    </td>
                    <td className="amount-cell debit-cell">
                      {movement.debit > 0 ? money(movement.debit) : '—'}
                    </td>
                    <td className="amount-cell credit-cell">
                      {movement.credit > 0 ? money(movement.credit) : '—'}
                    </td>
                    <td className="amount-cell">
                      <strong>
                        {money(selectedAnalytic
                          ? analyticBalances.get(movement.id) ?? movement.balanceAfter
                          : movement.balanceAfter)}
                      </strong>
                    </td>
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

type DisplayMovement = LedgerAccountMovement & {
  internalTransfer?: boolean;
  transferLabel?: string;
};

function collapseInternalTransfers(
  movements: LedgerAccountMovement[],
  account: LedgerAccount,
): DisplayMovement[] {
  const transferGroups = new Map<string, LedgerAccountMovement[]>();

  for (const movement of movements) {
    if (movement.referenceType !== 'STOCK_TRANSFER') continue;
    const rows = transferGroups.get(movement.entryId) ?? [];
    rows.push(movement);
    transferGroups.set(movement.entryId, rows);
  }

  const consumed = new Set<string>();
  const result: DisplayMovement[] = [];

  for (const movement of movements) {
    if (consumed.has(movement.id)) continue;

    const group = transferGroups.get(movement.entryId);
    if (!group || group.length < 2) {
      result.push(movement);
      continue;
    }

    const debitRows = group.filter((row) => row.debit > 0);
    const creditRows = group.filter((row) => row.credit > 0);
    const totalDebit = debitRows.reduce((sum, row) => sum + row.debit, 0);
    const totalCredit = creditRows.reduce((sum, row) => sum + row.credit, 0);

    if (
      debitRows.length === 0 ||
      creditRows.length === 0 ||
      Math.abs(totalDebit - totalCredit) > 0.0001
    ) {
      result.push(movement);
      continue;
    }

    group.forEach((row) => consumed.add(row.id));

    const sources = uniqueAnalytics(creditRows);
    const destinations = uniqueAnalytics(debitRows);
    const transferLabel = sources.length || destinations.length
      ? `${sources.join(', ') || 'Склад'} → ${destinations.join(', ') || 'Склад'}`
      : null;

    const firstDisplayedRow = group[0];
    result.push({
      ...firstDisplayedRow,
      id: `internal-transfer:${movement.entryId}:${account.id}`,
      description: 'Внутреннее перемещение',
      debit: totalDebit,
      credit: totalCredit,
      balanceAfter: firstDisplayedRow.balanceAfter,
      analytics: transferLabel,
      analyticsKind: null,
      analyticsId: null,
      internalTransfer: true,
      transferLabel: transferLabel ?? undefined,
    });
  }

  return result;
}

function uniqueAnalytics(rows: LedgerAccountMovement[]) {
  return Array.from(new Set(
    rows
      .map((row) => cleanAnalyticsLabel(row.analytics))
      .filter((value): value is string => Boolean(value)),
  ));
}

function cleanAnalyticsLabel(value: string | null) {
  if (!value) return null;
  return value
    .replace(/^Склад:\s*/i, '')
    .replace(/^Поставщик:\s*/i, '')
    .replace(/^Деньги:\s*/i, '')
    .trim();
}

function Stat({ label, value }: { label: string; value: string }) {
  return <div className="stat-card"><span>{label}</span><div><strong>{value}</strong></div></div>;
}

function analyticKindLabel(value: string) {
  return ({
    WAREHOUSE: 'Склад',
    SUPPLIER: 'Поставщик',
    MONEY_ACCOUNT: 'Денежный счёт / касса',
  } as Record<string, string>)[value] ?? value;
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
    INVENTORY_ACCOUNT_RECLASS: 'Переклассификация складского счёта',
  } as Record<string, string>)[value] ?? value;
}

function applyNaturalMovement(
  type: string,
  current: number,
  debit: number,
  credit: number,
) {
  return type === 'ASSET' || type === 'EXPENSE'
    ? current + debit - credit
    : current + credit - debit;
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
