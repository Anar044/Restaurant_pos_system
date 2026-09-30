import { Fragment, useEffect, useMemo, useState } from 'react';
import {
  type InventoryAccountingAct,
  type InventoryAccountingData,
  getInventoryAccounting,
  postRealizationAct,
  retryPendingRealizationActs,
  updateInventoryAccountingSettings,
} from './api';
import './inventory-accounting.css';

type ActFilter = 'ALL' | 'DRAFT' | 'POSTED';

export function InventoryAccountingPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<InventoryAccountingData | null>(null);
  const [costMethod, setCostMethod] = useState('WEIGHTED_AVERAGE');
  const [allowNegativeRealization, setAllowNegativeRealization] = useState(true);
  const [filter, setFilter] = useState<ActFilter>('ALL');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [postingId, setPostingId] = useState<string | null>(null);
  const [savingSettings, setSavingSettings] = useState(false);
  const [retrying, setRetrying] = useState(false);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const next = await getInventoryAccounting(token);
      setData(next);
      setCostMethod(next.settings.costMethod);
      setAllowNegativeRealization(next.settings.allowNegativeRealization);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить складской учёт');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [token]);

  async function saveSettings() {
    if (!canManage || savingSettings) return;

    setSavingSettings(true);
    setNotice(null);
    setError(null);
    try {
      const result = await updateInventoryAccountingSettings(token, {
        costMethod,
        allowNegativeRealization:
          costMethod === 'WEIGHTED_AVERAGE' && allowNegativeRealization,
      });
      setCostMethod(result.costMethod);
      setAllowNegativeRealization(result.allowNegativeRealization);
      setNotice('Настройки себестоимости сохранены.');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить настройки');
    } finally {
      setSavingSettings(false);
    }
  }

  async function postAct(act: InventoryAccountingAct) {
    if (!canManage || postingId) return;

    setPostingId(act.id);
    setNotice(null);
    setError(null);
    try {
      await postRealizationAct(token, act.id);
      setNotice('Акт реализации проведён.');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Акт пока нельзя провести');
      await load();
    } finally {
      setPostingId(null);
    }
  }

  async function retryAll() {
    if (!canManage || retrying) return;

    setRetrying(true);
    setNotice(null);
    setError(null);
    try {
      const result = await retryPendingRealizationActs(token);
      setNotice(
        result.checkedActs === 0
          ? 'Ожидающих актов нет.'
          : 'Проверено: ' + result.checkedActs +
            '. Проведено: ' + result.postedActs +
            '. Ожидают: ' + result.pendingActs + '.',
      );
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось повторить проведение актов');
    } finally {
      setRetrying(false);
    }
  }

  const acts = useMemo(() => {
    const source = data?.acts ?? [];
    if (filter === 'ALL') return source;
    return source.filter((x) => x.status === filter);
  }, [data, filter]);

  const counts = useMemo(() => {
    const source = data?.acts ?? [];
    return {
      ALL: source.length,
      DRAFT: source.filter((x) => x.status === 'DRAFT').length,
      POSTED: source.filter((x) => x.status === 'POSTED').length,
    };
  }, [data]);

  if (!data && loading) {
    return <div className="empty-state">Загружаем акты реализации…</div>;
  }

  if (!data) {
    return (
      <div className="empty-state">
        <strong>Складской учёт не загружен</strong>
        <span>{error ?? 'Попробуйте ещё раз.'}</span>
        <button className="secondary-button" onClick={() => void load()}>Повторить</button>
      </div>
    );
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НОМЕНКЛАТУРА И СКЛАД</div>
          <h1>Акты реализации и себестоимость</h1>
          <p>
            Продажи кассовой смены списываются со склада через отдельный акт реализации.
            Здесь выбирается метод расчёта себестоимости и контролируются непроведённые акты.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void load()} disabled={loading}>
            Обновить
          </button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}
      {notice && <div className="inventory-accounting-notice">{notice}</div>}

      <div className="inventory-accounting-settings">
        <div className="inventory-accounting-settings-head">
          <div>
            <span className="eyebrow">МЕТОД СЕБЕСТОИМОСТИ</span>
            <h2>Как списывать товары при продаже</h2>
          </div>
          {canManage && (
            <button
              className="primary-button"
              onClick={() => void saveSettings()}
              disabled={savingSettings}
            >
              {savingSettings ? 'Сохраняем…' : 'Сохранить'}
            </button>
          )}
        </div>

        <div className="cost-method-grid">
          {data.costMethods.map((method) => (
            <button
              type="button"
              key={method.code}
              className={
                'cost-method-card ' +
                (costMethod === method.code ? 'active' : '')
              }
              disabled={!canManage}
              onClick={() => {
                setCostMethod(method.code);
                if (method.code === 'FIFO') setAllowNegativeRealization(false);
              }}
            >
              <strong>{method.name}</strong>
              <span>{method.description}</span>
              {costMethod === method.code && <small>Выбрано</small>}
            </button>
          ))}
        </div>

        <label className="negative-stock-toggle">
          <input
            type="checkbox"
            checked={costMethod === 'WEIGHTED_AVERAGE' && allowNegativeRealization}
            disabled={!canManage || costMethod !== 'WEIGHTED_AVERAGE'}
            onChange={(e) => setAllowNegativeRealization(e.target.checked)}
          />
          <div>
            <strong>Разрешать актам реализации уходить в отрицательный остаток</strong>
            <span>
              Доступно для средневзвешенной себестоимости. При FIFO отрицательные остатки запрещены.
            </span>
          </div>
        </label>
      </div>

      {data.negativeBalances.length > 0 && (
        <div className="negative-stock-panel">
          <div className="negative-stock-panel-head">
            <div>
              <strong>Отрицательные остатки</strong>
              <span>
                {data.negativeBalances.length} позиций требуют внимания.
                Перед переходом на FIFO их нужно закрыть приходом или инвентаризацией.
              </span>
            </div>
          </div>

          <div className="negative-stock-list">
            {data.negativeBalances.slice(0, 12).map((row) => (
              <div key={row.warehouseId + ':' + row.productId}>
                <span>{row.productName}</span>
                <small>{row.warehouseName ?? 'Склад'}</small>
                <strong>{quantity(row.quantity)} {row.unit}</strong>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="realization-journal">
        <div className="realization-journal-head">
          <div>
            <h2>Акты реализации</h2>
            <p>
              Один акт формируется по складу из продаж закрытой кассовой смены.
            </p>
          </div>
          {canManage && (
            <button
              className="secondary-button"
              onClick={() => void retryAll()}
              disabled={retrying || counts.DRAFT === 0}
            >
              {retrying ? 'Проверяем…' : 'Провести все доступные'}
            </button>
          )}
        </div>

        <div className="receipt-status-tabs realization-tabs">
          {([
            ['ALL', 'Все'],
            ['DRAFT', 'Ожидают'],
            ['POSTED', 'Проведённые'],
          ] as Array<[ActFilter, string]>).map(([key, label]) => (
            <button
              key={key}
              type="button"
              className={filter === key ? 'receipt-status-tab active' : 'receipt-status-tab'}
              onClick={() => setFilter(key)}
            >
              {label}<span>{counts[key]}</span>
            </button>
          ))}
        </div>

        {acts.length === 0 ? (
          <div className="empty-state compact">
            <strong>Актов в этом разделе пока нет</strong>
            <span>Они появятся после закрытия кассовой смены с продажами.</span>
          </div>
        ) : (
          <div className="realization-table-wrap">
            <table className="data-table realization-table">
              <thead>
                <tr>
                  <th>Документ</th>
                  <th>Дата</th>
                  <th>Касса / смена</th>
                  <th>Склад</th>
                  <th>Метод</th>
                  <th>Себестоимость</th>
                  <th>Статус</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {acts.map((act) => {
                  const expanded = expandedId === act.id;
                  return (
                    <Fragment key={act.id}>
                      <tr>
                        <td>
                          <button
                            type="button"
                            className="realization-number"
                            onClick={() => setExpandedId(expanded ? null : act.id)}
                          >
                            {act.number}
                          </button>
                        </td>
                        <td>{formatDateTime(act.documentDate)}</td>
                        <td>
                          <strong>{act.deviceName ?? 'Касса'}</strong>
                          <small className="table-secondary">
                            {act.shiftClosedAt
                              ? 'закрыта ' + formatDateTime(act.shiftClosedAt)
                              : act.shiftId
                                ? 'смена ' + shortId(act.shiftId)
                                : '—'}
                          </small>
                        </td>
                        <td>{act.warehouseName ?? '—'}</td>
                        <td>{costMethodLabel(act.costMethod)}</td>
                        <td><strong>{money(act.totalAmount)}</strong></td>
                        <td>
                          <span className={'badge ' + (act.status === 'POSTED' ? 'success' : 'neutral')}>
                            {act.status === 'POSTED' ? 'Проведён' : 'Ожидает'}
                          </span>
                          {act.postingError && (
                            <small className="realization-error">{act.postingError}</small>
                          )}
                        </td>
                        <td>
                          <div className="warehouse-doc-actions">
                            <button
                              type="button"
                              className="text-button"
                              onClick={() => setExpandedId(expanded ? null : act.id)}
                            >
                              {expanded ? 'Скрыть' : 'Состав'}
                            </button>
                            {canManage && act.status === 'DRAFT' && (
                              <button
                                type="button"
                                className="primary-button compact"
                                disabled={postingId !== null}
                                onClick={() => void postAct(act)}
                              >
                                {postingId === act.id ? 'Проводим…' : 'Провести'}
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>

                      {expanded && (
                        <tr className="realization-details-row">
                          <td colSpan={8}>
                            <div className="realization-details">
                              <div className="realization-details-title">
                                <strong>Состав акта</strong>
                                <span>{act.lines.length} позиций</span>
                              </div>
                              <div className="realization-lines">
                                {act.lines.map((line) => (
                                  <div key={line.id}>
                                    <span>{line.productName}</span>
                                    <span>{quantity(line.quantity)} {line.unit}</span>
                                    <span>{money(line.unitCost)} / {line.unit}</span>
                                    <strong>{money(line.cost)}</strong>
                                  </div>
                                ))}
                              </div>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}

function costMethodLabel(value: string) {
  return value === 'FIFO' ? 'FIFO' : 'Средневзвешенная';
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value) + ' ₼';
}

function quantity(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 3,
  }).format(value);
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('ru-RU', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value));
}

function shortId(value: string) {
  return value.slice(0, 8).toUpperCase();
}
