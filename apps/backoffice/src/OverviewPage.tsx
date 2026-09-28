import { useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeFinance,
  getBackOfficeFinance,
} from './api';
import './overview.css';

export function OverviewPage({
  token,
  restaurantName,
  onOpenFinance,
}: {
  token: string;
  restaurantName: string;
  onOpenFinance: () => void;
}) {
  const [data, setData] = useState<BackOfficeFinance | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const period = useMemo(() => {
    const now = new Date();
    const from = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const to = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
    return { from: from.toISOString(), to: to.toISOString() };
  }, []);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      setData(await getBackOfficeFinance(token, {
        from: period.from,
        to: period.to,
      }));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить обзор');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [token, period.from, period.to]);

  const summary = data?.summary;
  const recentShifts = (data?.shifts ?? []).slice(0, 6);

  if (!data && loading) {
    return <div className="empty-state">Загружаем обзор ресторана…</div>;
  }

  return (
    <section>
      <div className="page-heading overview-heading">
        <div>
          <div className="eyebrow">СЕГОДНЯ</div>
          <h1>Обзор</h1>
          <p>{restaurantName}. Ключевые показатели за текущий день.</p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void refresh()} disabled={loading}>
            Обновить
          </button>
          <button className="primary-button" onClick={onOpenFinance}>
            Кассы и смены
          </button>
        </div>
      </div>

      {error && (
        <div className="global-error">
          <span>{error}</span>
          <button onClick={() => void refresh()}>Повторить</button>
        </div>
      )}

      <div className="overview-kpi-grid">
        <OverviewKpi
          label="Ожидаемая выручка"
          value={money(summary?.expectedRevenue ?? 0)}
          detail="закрытые + открытые заказы"
          emphasis
        />
        <OverviewKpi
          label="Закрытые заказы"
          value={money(summary?.completedOrdersAmount ?? 0)}
          detail={String(summary?.completedOrdersCount ?? 0) + ' заказов'}
        />
        <OverviewKpi
          label="Открытые заказы"
          value={money(summary?.openOrdersAmount ?? 0)}
          detail={String(summary?.openOrdersCount ?? 0) + ' заказов'}
        />
        <OverviewKpi
          label="Возвраты"
          value={money(summary?.refundAmount ?? 0)}
          detail="за текущий день"
          warning={(summary?.refundAmount ?? 0) > 0}
        />
        <OverviewKpi
          label="Открытые смены"
          value={String(summary?.openShiftsCount ?? 0)}
          detail={String(summary?.closedShiftsCount ?? 0) + ' закрыто'}
        />
        <OverviewKpi
          label="Расхождение кассы"
          value={money(summary?.cashDifference ?? 0)}
          detail="по закрытым сменам"
          warning={Math.abs(summary?.cashDifference ?? 0) > 0.001}
        />
      </div>

      <div className="overview-section-card">
        <div className="overview-section-head">
          <div>
            <strong>Последние кассовые смены</strong>
            <span>Состояние смен за текущий день</span>
          </div>
          <button className="text-button" onClick={onOpenFinance}>Открыть все</button>
        </div>

        {recentShifts.length === 0 ? (
          <div className="overview-empty">
            <strong>Смен за сегодня пока нет</strong>
            <span>После открытия кассовой смены она появится здесь.</span>
          </div>
        ) : (
          <div className="overview-shift-list">
            {recentShifts.map((shift) => (
              <button
                className="overview-shift-row"
                key={shift.id}
                onClick={onOpenFinance}
              >
                <div className="overview-shift-main">
                  <div className="overview-shift-icon">₼</div>
                  <div>
                    <strong>{shift.deviceName}</strong>
                    <span>
                      {(shift.openedByEmployeeName ?? 'Сотрудник') + ' · ' + formatTime(shift.openedAt)}
                    </span>
                  </div>
                </div>
                <div className="overview-shift-stats">
                  <span>{shift.ordersCount} заказов</span>
                  <strong>{money(shift.netSales)}</strong>
                </div>
                <span className={'badge ' + (shift.status === 'OPEN' ? 'success' : 'neutral')}>
                  {shift.status === 'OPEN' ? 'Открыта' : 'Закрыта'}
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </section>
  );
}

function OverviewKpi({
  label,
  value,
  detail,
  emphasis = false,
  warning = false,
}: {
  label: string;
  value: string;
  detail: string;
  emphasis?: boolean;
  warning?: boolean;
}) {
  const className =
    'overview-kpi' +
    (emphasis ? ' emphasis' : '') +
    (warning ? ' warning' : '');

  return (
    <div className={className}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{detail}</small>
    </div>
  );
}

function money(value: number) {
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value) + ' AZN';
}

function formatTime(value: string) {
  return new Date(value).toLocaleTimeString('ru-RU', {
    hour: '2-digit',
    minute: '2-digit',
  });
}
