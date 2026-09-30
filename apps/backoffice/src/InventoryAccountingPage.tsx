import { Fragment, useEffect, useMemo, useState } from 'react';
import {
  type InventoryAccountingAct,
  type InventoryAccountingData,
  getInventoryAccounting,
  postRealizationAct,
  retryPendingRealizationActs,
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
  const [filter, setFilter] = useState<ActFilter>('ALL');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [postingId, setPostingId] = useState<string | null>(null);
  const [retrying, setRetrying] = useState(false);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setData(await getInventoryAccounting(token));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить акты реализации');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [token]);

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
        <strong>Акты реализации не загружены</strong>
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
          <h1>Акты реализации</h1>
          <p>
            Здесь отображаются акты, автоматически сформированные по продажам закрытых
            кассовых смен.
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

      <div className="realization-journal">
        <div className="realization-journal-head">
          <div>
            <h2>Журнал актов</h2>
            <p>Один акт формируется по складу из продаж закрытой кассовой смены.</p>
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
            <strong>Актов реализации пока нет</strong>
            <span>Они появятся автоматически после закрытия кассовой смены с продажами.</span>
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
