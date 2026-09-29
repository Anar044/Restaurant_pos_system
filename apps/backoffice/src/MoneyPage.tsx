import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeMoney,
  type MoneyAccount,
  type MoneyCategory,
  createMoneyAccount,
  createMoneyCategory,
  createMoneyTransaction,
  getBackOfficeMoney,
  updateMoneyAccount,
  updateMoneyCategory,
} from './api';
import './money.css';

type Editor =
  | { kind: 'account-create' }
  | { kind: 'account-edit'; account: MoneyAccount }
  | { kind: 'category-create'; direction: 'INCOME' | 'EXPENSE' }
  | { kind: 'category-edit'; category: MoneyCategory }
  | { kind: 'transaction'; direction: 'INCOME' | 'EXPENSE' }
  | null;

export function MoneyPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const today = dateValue(new Date());
  const monthAgo = dateValue(new Date(Date.now() - 29 * 86400000));
  const [fromDate, setFromDate] = useState(monthAgo);
  const [toDate, setToDate] = useState(today);
  const [data, setData] = useState<BackOfficeMoney | null>(null);
  const [editor, setEditor] = useState<Editor>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load(from = fromDate, to = toDate) {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeMoney(token, dayStartIso(from), dayAfterIso(to)));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить денежный учёт');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load(monthAgo, today);
  }, [token]);

  const incomeCategories = useMemo(
    () => data?.categories.filter((x) => x.direction === 'INCOME') ?? [],
    [data],
  );
  const expenseCategories = useMemo(
    () => data?.categories.filter((x) => x.direction === 'EXPENSE') ?? [],
    [data],
  );

  if (!data && loading) {
    return <div className="empty-state">Загружаем денежный учёт…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">ФИНАНСЫ</div>
          <h1>Денежный учёт</h1>
          <p>Счета денег, доходы и расходы ресторана. Кассовые смены и продажи POS остаются в разделе «Кассы и смены».</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void load()} disabled={loading}>Обновить</button>
          {canManage && <button className="primary-button" onClick={() => setEditor({ kind: 'transaction', direction: 'EXPENSE' })}>+ Операция</button>}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="money-filter-bar">
        <label><span>С</span><input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} /></label>
        <label><span>По</span><input type="date" value={toDate} min={fromDate} onChange={(e) => setToDate(e.target.value)} /></label>
        <button className="primary-button compact" disabled={!fromDate || !toDate || toDate < fromDate} onClick={() => void load()}>Показать</button>
      </div>

      <div className="stats-grid money-stats">
        <MoneyStat label="Доходы" value={money(data?.summary.income ?? 0)} />
        <MoneyStat label="Расходы" value={money(data?.summary.expense ?? 0)} warning={(data?.summary.expense ?? 0) > 0} />
        <MoneyStat label="Результат периода" value={signedMoney(data?.summary.net ?? 0)} warning={(data?.summary.net ?? 0) < 0} />
        <MoneyStat label="Баланс счетов" value={money(data?.summary.totalBalance ?? 0)} />
      </div>

      <div className="money-layout">
        <div className="money-panel">
          <div className="money-panel-head">
            <div><h2>Счета денег</h2><p>Наличные, банк, карта или другой денежный счёт.</p></div>
            {canManage && <button className="secondary-button compact" onClick={() => setEditor({ kind: 'account-create' })}>+ Счёт</button>}
          </div>
          <div className="money-account-grid">
            {(data?.accounts ?? []).map((account) => (
              <button
                key={account.id}
                className={'money-account-card ' + (!account.isActive ? 'inactive' : '')}
                onClick={() => canManage && setEditor({ kind: 'account-edit', account })}
              >
                <span>{accountTypeLabel(account.type)}</span>
                <strong>{account.name}</strong>
                <b>{money(account.balance)}</b>
                <small>{account.isActive ? 'Активен' : 'Отключён'}</small>
              </button>
            ))}
            {(data?.accounts.length ?? 0) === 0 && <div className="money-empty">Создайте первый денежный счёт.</div>}
          </div>
        </div>

        <div className="money-panel">
          <div className="money-panel-head">
            <div><h2>Категории</h2><p>Статьи доходов и расходов.</p></div>
          </div>
          <CategoryBlock
            title="Доходы"
            categories={incomeCategories}
            canManage={canManage}
            onCreate={() => setEditor({ kind: 'category-create', direction: 'INCOME' })}
            onEdit={(category) => setEditor({ kind: 'category-edit', category })}
          />
          <CategoryBlock
            title="Расходы"
            categories={expenseCategories}
            canManage={canManage}
            onCreate={() => setEditor({ kind: 'category-create', direction: 'EXPENSE' })}
            onEdit={(category) => setEditor({ kind: 'category-edit', category })}
          />
        </div>
      </div>

      <div className="money-panel money-transactions-panel">
        <div className="money-panel-head">
          <div><h2>Журнал операций</h2><p>До 500 операций за выбранный период. Проведённые операции не удаляются.</p></div>
          {canManage && (
            <div className="heading-actions">
              <button className="secondary-button compact" onClick={() => setEditor({ kind: 'transaction', direction: 'INCOME' })}>+ Доход</button>
              <button className="primary-button compact" onClick={() => setEditor({ kind: 'transaction', direction: 'EXPENSE' })}>+ Расход</button>
            </div>
          )}
        </div>

        {(data?.transactions.length ?? 0) === 0 ? (
          <div className="money-empty">За выбранный период операций нет.</div>
        ) : (
          <div className="money-table-wrap">
            <table className="money-table">
              <thead><tr><th>Дата</th><th>Счёт</th><th>Категория</th><th>Сотрудник</th><th>Комментарий</th><th>Сумма</th></tr></thead>
              <tbody>
                {data!.transactions.map((transaction) => (
                  <tr key={transaction.id}>
                    <td>{dateTime(transaction.occurredAt)}</td>
                    <td>{transaction.accountName}</td>
                    <td>{transaction.categoryName}</td>
                    <td>{transaction.employeeName}</td>
                    <td>{transaction.note ?? '—'}</td>
                    <td className={transaction.direction === 'INCOME' ? 'money-income' : 'money-expense'}>
                      {transaction.direction === 'INCOME' ? '+' : '−'}{money(transaction.amount)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {editor && data && canManage && (
        <MoneyEditor
          editor={editor}
          data={data}
          token={token}
          onClose={() => setEditor(null)}
          onSaved={async () => {
            setEditor(null);
            await load();
          }}
        />
      )}
    </section>
  );
}

function MoneyStat({ label, value, warning = false }: { label: string; value: string; warning?: boolean }) {
  return <div className={'stat-card ' + (warning ? 'money-warning' : '')}><span>{label}</span><div><strong>{value}</strong></div></div>;
}

function CategoryBlock({
  title,
  categories,
  canManage,
  onCreate,
  onEdit,
}: {
  title: string;
  categories: MoneyCategory[];
  canManage: boolean;
  onCreate: () => void;
  onEdit: (category: MoneyCategory) => void;
}) {
  return (
    <div className="money-category-block">
      <div><strong>{title}</strong>{canManage && <button className="text-button" onClick={onCreate}>+ Добавить</button>}</div>
      <div className="money-category-list">
        {categories.map((category) => (
          <button key={category.id} disabled={!canManage} className={!category.isActive ? 'inactive' : ''} onClick={() => onEdit(category)}>
            {category.name}{!category.isActive ? ' · выкл.' : ''}
          </button>
        ))}
        {categories.length === 0 && <span>Категорий пока нет.</span>}
      </div>
    </div>
  );
}

function MoneyEditor({
  editor,
  data,
  token,
  onClose,
  onSaved,
}: {
  editor: Exclude<Editor, null>;
  data: BackOfficeMoney;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  if (editor.kind === 'account-create' || editor.kind === 'account-edit') {
    return <AccountEditor editor={editor} token={token} onClose={onClose} onSaved={onSaved} />;
  }
  if (editor.kind === 'category-create' || editor.kind === 'category-edit') {
    return <CategoryEditor editor={editor} token={token} onClose={onClose} onSaved={onSaved} />;
  }
  return <TransactionEditor direction={editor.direction} data={data} token={token} onClose={onClose} onSaved={onSaved} />;
}

function AccountEditor({
  editor,
  token,
  onClose,
  onSaved,
}: {
  editor: { kind: 'account-create' } | { kind: 'account-edit'; account: MoneyAccount };
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const account = editor.kind === 'account-edit' ? editor.account : null;
  const [name, setName] = useState(account?.name ?? '');
  const [type, setType] = useState(account?.type ?? 'CASH');
  const [isActive, setIsActive] = useState(account?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'account-create') {
        await createMoneyAccount(token, { name: name.trim(), type });
      } else {
        await updateMoneyAccount(token, editor.account.id, { name: name.trim(), type, isActive });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить счёт');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header"><div><div className="eyebrow">ДЕНЬГИ</div><h2>{account ? 'Настройки счёта' : 'Новый счёт'}</h2></div><button type="button" className="close-button" onClick={onClose}>×</button></div>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>
        <label><span>Тип</span><select value={type} onChange={(e) => setType(e.target.value)}><option value="CASH">Наличные</option><option value="BANK">Банк</option><option value="CARD">Карта / эквайринг</option><option value="OTHER">Другой</option></select></label>
        {account && <label className="toggle-row"><span><strong>Активно</strong></span><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /></label>}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button></div>
      </form>
    </div>
  );
}

function CategoryEditor({
  editor,
  token,
  onClose,
  onSaved,
}: {
  editor: { kind: 'category-create'; direction: 'INCOME' | 'EXPENSE' } | { kind: 'category-edit'; category: MoneyCategory };
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const category = editor.kind === 'category-edit' ? editor.category : null;
  const [name, setName] = useState(category?.name ?? '');
  const [direction, setDirection] = useState<'INCOME' | 'EXPENSE'>(category?.direction ?? (editor.kind === 'category-create' ? editor.direction : 'EXPENSE'));
  const [isActive, setIsActive] = useState(category?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;
    setSaving(true);
    setError(null);
    try {
      if (editor.kind === 'category-create') {
        await createMoneyCategory(token, { name: name.trim(), direction });
      } else {
        await updateMoneyCategory(token, editor.category.id, { name: name.trim(), direction, isActive });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить категорию');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header"><div><div className="eyebrow">ДЕНЬГИ</div><h2>{category ? 'Настройки категории' : 'Новая категория'}</h2></div><button type="button" className="close-button" onClick={onClose}>×</button></div>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>
        <label><span>Тип</span><select value={direction} onChange={(e) => setDirection(e.target.value as 'INCOME' | 'EXPENSE')}><option value="INCOME">Доход</option><option value="EXPENSE">Расход</option></select></label>
        {category && <label className="toggle-row"><span><strong>Активно</strong></span><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /></label>}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button></div>
      </form>
    </div>
  );
}

function TransactionEditor({
  direction,
  data,
  token,
  onClose,
  onSaved,
}: {
  direction: 'INCOME' | 'EXPENSE';
  data: BackOfficeMoney;
  token: string;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const accounts = data.accounts.filter((x) => x.isActive);
  const categories = data.categories.filter((x) => x.isActive && x.direction === direction);
  const [accountId, setAccountId] = useState(accounts[0]?.id ?? '');
  const [categoryId, setCategoryId] = useState(categories[0]?.id ?? '');
  const [amount, setAmount] = useState('');
  const [occurredDate, setOccurredDate] = useState(dateValue(new Date()));
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const numeric = Number(amount.replace(',', '.'));
    if (!accountId || !categoryId || !Number.isFinite(numeric) || numeric <= 0) {
      setError('Выберите счёт, категорию и укажите сумму больше нуля.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await createMoneyTransaction(token, {
        accountId,
        categoryId,
        direction,
        amount: numeric,
        note: note.trim() || null,
        occurredAt: occurredDate ? dayNoonIso(occurredDate) : null,
      });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести операцию');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header"><div><div className="eyebrow">{direction === 'INCOME' ? 'ДОХОД' : 'РАСХОД'}</div><h2>{direction === 'INCOME' ? 'Новый доход' : 'Новый расход'}</h2></div><button type="button" className="close-button" onClick={onClose}>×</button></div>
        {accounts.length === 0 || categories.length === 0 ? (
          <div className="error-box">Сначала создайте активный денежный счёт и категорию {direction === 'INCOME' ? 'дохода' : 'расхода'}.</div>
        ) : (
          <>
            <label><span>Счёт</span><select value={accountId} onChange={(e) => setAccountId(e.target.value)}>{accounts.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <label><span>Категория</span><select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>{categories.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <div className="form-grid">
              <label><span>Сумма</span><input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" autoFocus /></label>
              <label><span>Дата</span><input type="date" value={occurredDate} onChange={(e) => setOccurredDate(e.target.value)} /></label>
            </div>
            <label><span>Комментарий</span><input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="Например: аренда за сентябрь" /></label>
          </>
        )}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions"><button type="button" className="secondary-button" onClick={onClose}>Отмена</button><button className="primary-button" disabled={saving || !accountId || !categoryId}>{saving ? 'Проводим…' : 'Провести'}</button></div>
      </form>
    </div>
  );
}

function accountTypeLabel(type: string) {
  return ({ CASH: 'Наличные', BANK: 'Банк', CARD: 'Карта / эквайринг', OTHER: 'Другой' } as Record<string, string>)[type] ?? type;
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼';
}

function signedMoney(value: number) {
  return (value > 0 ? '+' : '') + money(value);
}

function dateTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value));
}

function dateValue(date: Date) {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
}

function dayStartIso(value: string) {
  return new Date(value + 'T00:00:00').toISOString();
}

function dayAfterIso(value: string) {
  const date = new Date(value + 'T00:00:00');
  date.setDate(date.getDate() + 1);
  return date.toISOString();
}

function dayNoonIso(value: string) {
  return new Date(value + 'T12:00:00').toISOString();
}
