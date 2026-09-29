import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  type BackOfficeTaxSettings,
  getBackOfficeTaxSettings,
  updateBackOfficeTaxSettings,
} from './api';
import './tax-settings.css';

export function TaxSettingsPage({
  token,
  canManage,
}: {
  token: string;
  canManage: boolean;
}) {
  const [data, setData] = useState<BackOfficeTaxSettings | null>(null);
  const [taxRegime, setTaxRegime] = useState('UNCONFIGURED');
  const [vatPriceMode, setVatPriceMode] = useState('INCLUDED');
  const [integratedPosTaxReliefEnabled, setIntegratedPosTaxReliefEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const next = await getBackOfficeTaxSettings(token);
      setData(next);
      setTaxRegime(next.profile.taxRegime);
      setVatPriceMode(next.profile.vatPriceMode);
      setIntegratedPosTaxReliefEnabled(next.profile.integratedPosTaxReliefEnabled);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось загрузить налоговые настройки');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, [token]);

  const selectedRegime = useMemo(
    () => data?.taxRegimes.find((item) => item.code === taxRegime) ?? null,
    [data, taxRegime],
  );

  const posReliefAvailable = taxRegime === 'VAT_18' || taxRegime === 'SIMPLIFIED_8';

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!canManage || saving || !data) return;

    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await updateBackOfficeTaxSettings(token, {
        taxRegime,
        vatPriceMode,
        integratedPosTaxReliefEnabled: posReliefAvailable
          ? integratedPosTaxReliefEnabled
          : false,
      });
      setSaved(true);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не удалось сохранить налоговые настройки');
    } finally {
      setSaving(false);
    }
  }

  if (!data && loading) {
    return <div className="empty-state">Загружаем налоговые настройки…</div>;
  }

  if (!data) {
    return (
      <div className="empty-state">
        <strong>Налоговые настройки не загружены</strong>
        <span>{error ?? 'Попробуйте обновить страницу.'}</span>
        <button className="secondary-button" onClick={() => void load()}>Повторить</button>
      </div>
    );
  }

  return (
    <section>
      <div className="page-heading">
        <div>
          <div className="eyebrow">НАСТРОЙКИ РЕСТОРАНА</div>
          <h1>Налоги</h1>
          <p>
            Единый налоговый профиль для POS, приходных накладных, импорта и бухгалтерских проводок.
          </p>
        </div>
        <div className="heading-actions">
          <button className="secondary-button" onClick={() => void load()} disabled={loading || saving}>
            Обновить
          </button>
        </div>
      </div>

      {error && <div className="global-error"><span>{error}</span></div>}
      {saved && <div className="tax-saved-note">Налоговый профиль сохранён.</div>}

      <form className="tax-settings-layout" onSubmit={submit}>
        <div className="tax-settings-main">
          <div className="tax-card">
            <div className="tax-card-heading">
              <div>
                <strong>Налоговый режим ресторана</strong>
                <span>Выбирается на уровне ресторана и применяется единым Tax Engine.</span>
              </div>
            </div>

            <div className="tax-regime-options">
              {data.taxRegimes.map((option) => (
                <label
                  key={option.code}
                  className={taxRegime === option.code ? 'tax-regime-option active' : 'tax-regime-option'}
                >
                  <input
                    type="radio"
                    name="taxRegime"
                    value={option.code}
                    checked={taxRegime === option.code}
                    onChange={(e) => setTaxRegime(e.target.value)}
                    disabled={!canManage}
                  />
                  <span>
                    <strong>{option.name}</strong>
                    <small>{option.description}</small>
                  </span>
                </label>
              ))}
            </div>
          </div>

          {taxRegime === 'VAT_18' && (
            <div className="tax-card">
              <div className="tax-card-heading">
                <div>
                  <strong>Как указаны цены продажи</strong>
                  <span>Эта настройка определяет формулу выделения или начисления ƏDV.</span>
                </div>
              </div>

              <div className="tax-price-options">
                {data.vatPriceModes.map((option) => (
                  <label
                    key={option.code}
                    className={vatPriceMode === option.code ? 'tax-price-option active' : 'tax-price-option'}
                  >
                    <input
                      type="radio"
                      name="vatPriceMode"
                      value={option.code}
                      checked={vatPriceMode === option.code}
                      onChange={(e) => setVatPriceMode(e.target.value)}
                      disabled={!canManage}
                    />
                    <span>
                      <strong>{option.name}</strong>
                      <small>{option.description}</small>
                    </span>
                  </label>
                ))}
              </div>

              <div className="tax-formula-box">
                {vatPriceMode === 'INCLUDED' ? (
                  <>
                    <strong>ƏDV включён в конечную цену</strong>
                    <span>ƏDV = итог × 18 / 118</span>
                    <span>База без ƏDV = итог × 100 / 118</span>
                  </>
                ) : (
                  <>
                    <strong>Цена указана без ƏDV</strong>
                    <span>ƏDV = цена × 18 / 100</span>
                    <span>Итого к оплате = цена + ƏDV</span>
                  </>
                )}
              </div>
            </div>
          )}

          {posReliefAvailable && (
            <div className="tax-card">
              <label className="tax-relief-toggle">
                <span>
                  <strong>Льгота для интегрированного POS в 2026–2028</strong>
                  <small>
                    Включайте только если безналичная оплата проходит через POS-терминал,
                    интегрированный с контрольно-кассовым аппаратом в единой операционной системе.
                  </small>
                </span>
                <input
                  type="checkbox"
                  checked={integratedPosTaxReliefEnabled}
                  onChange={(e) => setIntegratedPosTaxReliefEnabled(e.target.checked)}
                  disabled={!canManage}
                />
              </label>

              {integratedPosTaxReliefEnabled && (
                <div className="tax-relief-details">
                  <div>
                    <span>Период</span>
                    <strong>
                      {formatDate(data.restaurantPosRelief.from)} — {formatDate(data.restaurantPosRelief.to)}
                    </strong>
                  </div>
                  {taxRegime === 'VAT_18' ? (
                    <div>
                      <span>ƏDV</span>
                      <strong>50% подходящего POS-оборота исключается из облагаемого оборота</strong>
                    </div>
                  ) : (
                    <div>
                      <span>Sadələşdirilmiş vergi</span>
                      <strong>
                        Подходящий POS-оборот: {number(data.rates.simplified8IntegratedPos)}%;
                        остальной оборот: {number(data.rates.simplified8)}%
                      </strong>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {taxRegime === 'SIMPLIFIED_2' && (
            <div className="tax-card">
              <div className="tax-formula-box">
                <strong>Sadələşdirilmiş vergi — 2%</strong>
                <span>Налог = облагаемый оборот × {number(data.rates.simplified2)} / 100</span>
                <span>Этот налог не выделяется из цены блюда как ƏDV.</span>
              </div>
            </div>
          )}

          {taxRegime === 'SIMPLIFIED_8' && (
            <div className="tax-card">
              <div className="tax-formula-box">
                <strong>Sadələşdirilmiş vergi — 8%</strong>
                <span>Обычный облагаемый оборот: × {number(data.rates.simplified8)} / 100</span>
                <span>
                  Подходящий интегрированный POS в период льготы: × {number(data.rates.simplified8IntegratedPos)} / 100
                </span>
                <span>Налог считается с оборота отдельно и не вычитается из цены позиции как ƏDV.</span>
              </div>
            </div>
          )}

          <div className="tax-settings-actions">
            {!canManage && (
              <span>Для изменения требуется право «Управление рестораном».</span>
            )}
            <button
              className="primary-button"
              disabled={!canManage || saving || taxRegime === 'UNCONFIGURED'}
            >
              {saving ? 'Сохраняем…' : 'Сохранить налоговый профиль'}
            </button>
          </div>
        </div>

        <aside className="tax-settings-side">
          <div className="tax-summary-card">
            <span>Текущий выбор</span>
            <strong>{selectedRegime?.name ?? taxRegime}</strong>
            <p>{selectedRegime?.description}</p>

            {taxRegime === 'VAT_18' && (
              <div className="tax-summary-row">
                <span>Цены POS</span>
                <strong>{vatPriceMode === 'INCLUDED' ? 'ƏDV включён' : 'Без ƏDV'}</strong>
              </div>
            )}

            {posReliefAvailable && (
              <div className="tax-summary-row">
                <span>POS-льгота</span>
                <strong>{integratedPosTaxReliefEnabled ? 'Включена' : 'Выключена'}</strong>
              </div>
            )}
          </div>

          <div className="tax-info-card">
            <strong>Как будет работать дальше</strong>
            <span>POS использует профиль при расчёте продажи.</span>
            <span>Приходная накладная определяет входной ƏDV.</span>
            <span>Импорт рассчитывает таможенную базу и импортный ƏDV.</span>
            <span>Бухгалтерия получает проводки из того же Tax Engine.</span>
          </div>
        </aside>
      </form>
    </section>
  );
}

function formatDate(value: string) {
  const [year, month, day] = value.slice(0, 10).split('-');
  return `${day}.${month}.${year}`;
}

function number(value: number) {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value);
}
