import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type AccountingMoneyAccount,
  type BackOfficeAccounting,
  type BackOfficeStockDocuments,
  type StockSupplier,
  createStockSupplier,
  getBackOfficeAccounting,
  getBackOfficeStockDocuments,
  paySupplier,
  updateStockSupplier,
} from './api';
import './suppliers.css';

type Tab = 'list' | 'balance' | 'documents';

export function SuppliersPage({
  token,
  canManage,
  canPay,
}: {
  token: string;
  canManage: boolean;
  canPay: boolean;
}) {
  const [data, setData] = useState<BackOfficeStockDocuments | null>(null);
  const [accounting, setAccounting] = useState<BackOfficeAccounting | null>(null);
  const [tab, setTab] = useState<Tab>('list');
  const [editor, setEditor] = useState<StockSupplier | 'new' | null>(null);
  const [paymentSupplier, setPaymentSupplier] = useState<StockSupplier | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const [stockData, accountingData] = await Promise.all([
        getBackOfficeStockDocuments(token),
        getBackOfficeAccounting(token),
      ]);
      setData(stockData);
      setAccounting(accountingData);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить поставщиков');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void refresh(); }, [token]);

  const supplierRows = useMemo(() => {
    const suppliers = data?.suppliers ?? [];
    const documents = data?.documents ?? [];
    const balances = accounting?.suppliers ?? [];

    return suppliers.map((supplier) => {
      const posted = documents.filter(
        (document) => document.supplierId === supplier.id && document.status === 'POSTED',
      );
      const drafts = documents.filter(
        (document) => document.supplierId === supplier.id && document.status === 'DRAFT',
      );
      const lastDocument = posted
        .slice()
        .sort((a, b) => new Date(b.documentDate).getTime() - new Date(a.documentDate).getTime())[0];
      const settlement = balances.find((x) => x.id === supplier.id);

      return {
        supplier,
        postedAmount: posted.reduce((sum, document) => sum + document.totalAmount, 0),
        postedCount: posted.length,
        draftCount: drafts.length,
        lastDocument,
        payable: settlement?.payable ?? 0,
        advance: settlement?.advance ?? 0,
        balance: settlement?.balance ?? 0,
      };
    });
  }, [data, accounting]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем поставщиков…</div>;
  }

  const activeMoneyAccounts = (accounting?.moneyAccounts ?? []).filter((x) => x.isActive);

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">КОНТРАГЕНТЫ</div>
          <h1>Поставщики</h1>
          <p>Карточки поставщиков, поставки, задолженность, авансы и оплаты в одном разделе.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>Обновить</button>
          {canManage && tab === 'list' && (
            <button className="primary-button" onClick={() => setEditor('new')}>+ Поставщик</button>
          )}
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}

      <div className="supplier-tabs">
        <button className={tab === 'list' ? 'active' : ''} onClick={() => setTab('list')}>Список поставщиков</button>
        <button className={tab === 'balance' ? 'active' : ''} onClick={() => setTab('balance')}>Баланс по поставщикам</button>
        <button className={tab === 'documents' ? 'active' : ''} onClick={() => setTab('documents')}>Документы поставщиков</button>
      </div>

      {tab === 'list' && (
        <div className="supplier-panel">
          <div className="supplier-list-grid">
            {supplierRows.map(({ supplier, postedAmount, postedCount, draftCount, lastDocument, balance }) => (
              <button
                key={supplier.id}
                className={'supplier-main-card ' + (!supplier.isActive ? 'inactive' : '')}
                onClick={() => canManage && setEditor(supplier)}
                disabled={!canManage}
              >
                <div className="supplier-main-card-head">
                  <span>{supplier.type === 'INTERNAL' ? 'Внутренний' : 'Внешний'}</span>
                  <span className={'badge ' + (supplier.isActive ? 'success' : 'neutral')}>
                    {supplier.isActive ? 'Активен' : 'Отключён'}
                  </span>
                </div>
                <strong>{supplier.name}</strong>
                <small>{supplier.taxId ? 'VÖEN: ' + supplier.taxId : 'VÖEN не указан'}</small>
                <small>{supplier.phone || 'Телефон не указан'}</small>
                <div className="supplier-main-stats">
                  <span><b>{postedCount}</b> проведено</span>
                  <span><b>{draftCount}</b> черновиков</span>
                  <span><b>{money(postedAmount)}</b> поставки</span>
                </div>
                <small className="supplier-last">
                  {balance > 0
                    ? 'К оплате: ' + money(balance)
                    : balance < 0
                      ? 'Аванс: ' + money(-balance)
                      : lastDocument
                        ? 'Баланс закрыт · ' + date(lastDocument.documentDate)
                        : 'Поставок пока нет'}
                </small>
              </button>
            ))}
            {supplierRows.length === 0 && <div className="supplier-empty">Поставщиков пока нет.</div>}
          </div>
        </div>
      )}

      {tab === 'balance' && (
        <div className="supplier-panel">
          <div className="supplier-balance-note">
            <strong>Баланс по поставщикам</strong>
            <span>
              Баланс берётся из плана счетов. Проведённая приходная накладная создаёт долг,
              оплата уменьшает долг, а переплата учитывается как аванс поставщику.
            </span>
          </div>

          {canPay && activeMoneyAccounts.length === 0 && (
            <div className="global-error">
              <span>Для оплаты поставщика создайте активный денежный счёт в разделе «Денежный учёт».</span>
            </div>
          )}

          <div className="supplier-table-wrap">
            <table className="supplier-table">
              <thead>
                <tr>
                  <th>Поставщик</th>
                  <th>Накладных</th>
                  <th>Сумма поставок</th>
                  <th>Задолженность</th>
                  <th>Аванс</th>
                  <th>Итоговый баланс</th>
                  <th>Последняя поставка</th>
                  {canPay && <th />}
                </tr>
              </thead>
              <tbody>
                {supplierRows.map(({ supplier, postedAmount, postedCount, payable, advance, balance, lastDocument }) => (
                  <tr key={supplier.id}>
                    <td><strong>{supplier.name}</strong></td>
                    <td>{postedCount}</td>
                    <td>{money(postedAmount)}</td>
                    <td><strong>{money(payable)}</strong></td>
                    <td>{money(advance)}</td>
                    <td>
                      <strong>
                        {balance > 0
                          ? money(balance) + ' к оплате'
                          : balance < 0
                            ? money(-balance) + ' аванс'
                            : money(0)}
                      </strong>
                    </td>
                    <td>{lastDocument ? date(lastDocument.documentDate) : '—'}</td>
                    {canPay && (
                      <td>
                        <button
                          className="secondary-button compact"
                          disabled={!supplier.isActive || activeMoneyAccounts.length === 0}
                          onClick={() => setPaymentSupplier(supplier)}
                        >
                          Оплата / аванс
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {tab === 'documents' && (
        <div className="supplier-panel">
          <div className="supplier-table-wrap">
            <table className="supplier-table">
              <thead>
                <tr>
                  <th>Дата</th>
                  <th>№</th>
                  <th>Поставщик</th>
                  <th>Склад</th>
                  <th>Сумма</th>
                  <th>Статус</th>
                </tr>
              </thead>
              <tbody>
                {(data?.documents ?? []).map((document) => (
                  <tr key={document.id}>
                    <td>{date(document.documentDate)}</td>
                    <td><strong>{document.number}</strong></td>
                    <td>{document.supplierName ?? '—'}</td>
                    <td>{document.warehouseName ?? '—'}</td>
                    <td>{money(document.totalAmount)}</td>
                    <td>
                      <span className={'badge ' + (document.status === 'POSTED' ? 'success' : 'neutral')}>
                        {document.status === 'POSTED' ? 'Проведён' : document.status === 'DRAFT' ? 'Черновик' : 'Отменён'}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {editor && canManage && (
        <SupplierEditor
          token={token}
          supplier={editor === 'new' ? null : editor}
          onClose={() => setEditor(null)}
          onSaved={async () => {
            setEditor(null);
            await refresh();
          }}
        />
      )}

      {paymentSupplier && canPay && (
        <SupplierPaymentEditor
          token={token}
          supplier={paymentSupplier}
          moneyAccounts={activeMoneyAccounts}
          currentBalance={supplierRows.find((x) => x.supplier.id === paymentSupplier.id)?.balance ?? 0}
          onClose={() => setPaymentSupplier(null)}
          onSaved={async () => {
            setPaymentSupplier(null);
            await refresh();
          }}
        />
      )}
    </section>
  );
}

function SupplierEditor({
  token,
  supplier,
  onClose,
  onSaved,
}: {
  token: string;
  supplier: StockSupplier | null;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const [name, setName] = useState(supplier?.name ?? '');
  const [type, setType] = useState<'EXTERNAL' | 'INTERNAL'>(supplier?.type ?? 'EXTERNAL');
  const [taxId, setTaxId] = useState(supplier?.taxId ?? '');
  const [phone, setPhone] = useState(supplier?.phone ?? '');
  const [isActive, setIsActive] = useState(supplier?.isActive ?? true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return;

    setSaving(true);
    setError(null);
    try {
      if (supplier) {
        await updateStockSupplier(token, supplier.id, {
          name: name.trim(),
          type,
          taxId: taxId.trim() || null,
          phone: phone.trim() || null,
          isActive,
        });
      } else {
        await createStockSupplier(token, {
          name: name.trim(),
          type,
          taxId: taxId.trim() || null,
          phone: phone.trim() || null,
        });
      }
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить поставщика');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div><div className="eyebrow">ПОСТАВЩИК</div><h2>{supplier ? 'Карточка поставщика' : 'Новый поставщик'}</h2></div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>
        <label><span>Название</span><input value={name} onChange={(e) => setName(e.target.value)} autoFocus /></label>
        <label>
          <span>Тип</span>
          <select value={type} onChange={(e) => setType(e.target.value as 'EXTERNAL' | 'INTERNAL')}>
            <option value="EXTERNAL">Внешний поставщик</option>
            <option value="INTERNAL">Внутренний поставщик</option>
          </select>
        </label>
        <div className="form-grid">
          <label><span>VÖEN / ИНН</span><input value={taxId} onChange={(e) => setTaxId(e.target.value)} /></label>
          <label><span>Телефон</span><input value={phone} onChange={(e) => setPhone(e.target.value)} /></label>
        </div>
        {supplier && (
          <label className="toggle-row">
            <span><strong>Активен</strong><small>Отключённого поставщика нельзя выбрать в новой накладной.</small></span>
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          </label>
        )}
        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !name.trim()}>{saving ? 'Сохраняем…' : 'Сохранить'}</button>
        </div>
      </form>
    </div>
  );
}

function SupplierPaymentEditor({
  token,
  supplier,
  moneyAccounts,
  currentBalance,
  onClose,
  onSaved,
}: {
  token: string;
  supplier: StockSupplier;
  moneyAccounts: AccountingMoneyAccount[];
  currentBalance: number;
  onClose: () => void;
  onSaved: () => Promise<void>;
}) {
  const today = dateValue(new Date());
  const [moneyAccountId, setMoneyAccountId] = useState(moneyAccounts[0]?.id ?? '');
  const [amount, setAmount] = useState(currentBalance > 0 ? currentBalance.toFixed(2) : '');
  const [operationDate, setOperationDate] = useState(today);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const numericAmount = Number(amount.replace(',', '.'));
    if (!moneyAccountId || !Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError('Выберите денежный счёт и укажите сумму больше нуля.');
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await paySupplier(token, supplier.id, {
        moneyAccountId,
        amount: numericAmount,
        occurredAt: operationDate ? new Date(operationDate + 'T12:00:00').toISOString() : null,
        note: note.trim() || null,
      });
      await onSaved();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось провести оплату поставщику');
    } finally {
      setSaving(false);
    }
  }

  const numericAmount = Number(amount.replace(',', '.'));
  const futureAdvance = Number.isFinite(numericAmount)
    ? Math.max(0, numericAmount - Math.max(0, currentBalance))
    : 0;

  return (
    <div className="modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <form className="modal-card" onSubmit={submit}>
        <div className="modal-header">
          <div>
            <div className="eyebrow">ВЗАИМОРАСЧЁТЫ</div>
            <h2>Оплата поставщику</h2>
            <p>{supplier.name}</p>
          </div>
          <button type="button" className="close-button" onClick={onClose}>×</button>
        </div>

        <div className="supplier-balance-note">
          <strong>
            {currentBalance > 0
              ? 'Текущий долг: ' + money(currentBalance)
              : currentBalance < 0
                ? 'Текущий аванс: ' + money(-currentBalance)
                : 'Баланс закрыт'}
          </strong>
          <span>Сумма сверх текущего долга автоматически будет учтена как аванс поставщику.</span>
        </div>

        <label>
          <span>С какого счёта оплатить</span>
          <select value={moneyAccountId} onChange={(e) => setMoneyAccountId(e.target.value)}>
            {moneyAccounts.map((account) => (
              <option key={account.id} value={account.id}>{account.name} · {moneyAccountType(account.type)}</option>
            ))}
          </select>
        </label>

        <div className="form-grid">
          <label>
            <span>Сумма</span>
            <input
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              inputMode="decimal"
              placeholder="0,00"
              autoFocus
            />
          </label>
          <label>
            <span>Дата</span>
            <input type="date" value={operationDate} onChange={(e) => setOperationDate(e.target.value)} />
          </label>
        </div>

        <label>
          <span>Комментарий</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} placeholder="Назначение платежа, № документа…" />
        </label>

        {futureAdvance > 0 && (
          <div className="supplier-balance-note">
            <strong>Будет создан аванс: {money(futureAdvance)}</strong>
            <span>Он автоматически зачтётся при следующей приходной накладной этого поставщика.</span>
          </div>
        )}

        {error && <div className="error-box">{error}</div>}
        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>Отмена</button>
          <button className="primary-button" disabled={saving || !moneyAccountId}>
            {saving ? 'Проводим…' : 'Провести оплату'}
          </button>
        </div>
      </form>
    </div>
  );
}

function moneyAccountType(value: string) {
  return ({
    CASH: 'наличные',
    BANK: 'банк',
    CARD: 'карта / эквайринг',
    OTHER: 'прочее',
  } as Record<string, string>)[value] ?? value;
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value) + ' ₼';
}

function date(value: string) {
  return new Intl.DateTimeFormat('ru-RU').format(new Date(value));
}

function dateValue(value: Date) {
  const y = value.getFullYear();
  const m = String(value.getMonth() + 1).padStart(2, '0');
  const d = String(value.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}
