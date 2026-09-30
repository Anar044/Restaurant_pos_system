import { useEffect, useState } from 'react';
import {
  getInventoryAccountingSettings,
  updateInventoryAccountingSettings,
} from './api';
import './inventory-accounting.css';

export function InventoryAccountingSettingsPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [costMethod, setCostMethod] = useState('WEIGHTED_AVERAGE');
  const [allowNegativeRealization, setAllowNegativeRealization] = useState(true);
  const [methods, setMethods] = useState<Array<{ code: string; name: string; description: string }>>([]);
  const [negativeBalanceCount, setNegativeBalanceCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const data = await getInventoryAccountingSettings(token);
      setCostMethod(data.settings.costMethod);
      setAllowNegativeRealization(data.settings.allowNegativeRealization);
      setMethods(data.costMethods);
      setNegativeBalanceCount(data.negativeBalanceCount);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить настройки складского учёта');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [token]);

  async function save() {
    if (!canManage || saving) return;

    setSaving(true);
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
      setNotice('Настройки складского учёта сохранены.');
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить настройки');
    } finally {
      setSaving(false);
    }
  }

  if (loading && methods.length === 0) {
    return <div className="empty-state">Загружаем настройки складского учёта…</div>;
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НАСТРОЙКИ РЕСТОРАНА</div>
          <h1>Складской учёт</h1>
          <p>
            Метод расчёта себестоимости задаётся для ресторана целиком и применяется
            при формировании актов реализации.
          </p>
        </div>
        {canManage && (
          <div className="heading-actions">
            <button className="primary-button" onClick={() => void save()} disabled={saving || loading}>
              {saving ? 'Сохраняем…' : 'Сохранить'}
            </button>
          </div>
        )}
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}
      {notice && <div className="inventory-accounting-notice">{notice}</div>}

      <div className="inventory-accounting-settings">
        <div className="inventory-accounting-settings-head">
          <div>
            <span className="eyebrow">МЕТОД СЕБЕСТОИМОСТИ</span>
            <h2>Как рассчитывать списание при реализации</h2>
          </div>
        </div>

        <div className="cost-method-grid">
          {methods.map((method) => (
            <button
              type="button"
              key={method.code}
              className={'cost-method-card ' + (costMethod === method.code ? 'active' : '')}
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
            <strong>Разрешать отрицательные остатки при реализации</strong>
            <span>
              Для средневзвешенной себестоимости продажи могут временно уводить склад в минус.
              После прихода себестоимость корректируется. При FIFO отрицательный остаток запрещён.
            </span>
          </div>
        </label>
      </div>

      {negativeBalanceCount > 0 && (
        <div className="negative-stock-panel">
          <div className="negative-stock-panel-head">
            <div>
              <strong>Есть отрицательные остатки: {negativeBalanceCount}</strong>
              <span>
                Перейти на FIFO можно после того, как отрицательные остатки будут закрыты
                приходами или инвентаризацией.
              </span>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
