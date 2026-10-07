// Portfolio's data cards (add-activity-cards), moved with the domain into its plugin's web part (extract-portfolio): each
// is drawn by the core's card view through the plugin's `cards` contribution, by its AG-UI activity type.
import { useState, type ReactNode } from 'react';
import type { PluginCardProps } from '@maf/plugin-api';
import { date, langOf, money, percent, toCsv, type Lang } from '@maf/shared/format';
import styles from './Cards.module.css';
import { words } from './words';

/** An account's holdings, drift and rebalance plan (get_household_portfolio). */
export function HoldingsCardView({ content, question, focus = null, onFocus }: PluginCardProps) {
  return (
    <HoldingsCard
      content={content as Holdings}
      lang={langOf(question)}
      focus={focus}
      onFocus={onFocus}
    />
  );
}

/** An account's quarter-end AUM (get_aum_history). */
export function AumHistoryCardView({ content, question, focus = null, onFocus }: PluginCardProps) {
  return (
    <AumHistoryCard
      content={content as AumHistory}
      lang={langOf(question)}
      focus={focus}
      onFocus={onFocus}
    />
  );
}

/** The accounts the user can access (list_my_accounts). */
export function AccountsCardView({ content, question, focus = null, onFocus }: PluginCardProps) {
  return (
    <AccountsCard
      content={content as Accounts}
      lang={langOf(question)}
      focus={focus}
      onFocus={onFocus}
    />
  );
}

interface Focusing {
  focus?: string | null;
  onFocus?: (accountId: string) => void;
}

/** "Focus" on an account, or "In focus" when it already is. */
function FocusButton({
  accountId,
  lang,
  focus,
  onFocus,
}: Focusing & { accountId: string; lang: Lang }) {
  const w = words[lang];
  if (!onFocus) return null;
  const current = focus === accountId;
  return (
    <button
      type="button"
      className={styles.copy}
      aria-pressed={current}
      aria-label={`${w.focus} ${accountId}`}
      disabled={current}
      onClick={(e) => {
        e.stopPropagation();
        onFocus(accountId);
      }}
    >
      {current ? w.inFocus : w.focus}
    </button>
  );
}

interface Holding {
  assetClass: string;
  marketValue: number;
  targetWeightPct: number;
  actualWeightPct: number;
  driftPct: number;
  outsideTolerance?: boolean;
  tradeToTarget?: number;
  tradeSide?: 'buy' | 'sell' | 'none';
  weightAfterPct?: number;
}

interface Holdings {
  accountId: string;
  accountName: string;
  driftTolerancePct: number;
  rebalanceNeeded?: boolean;
  outsideTolerance: boolean;
  holdings: Holding[];
  totalMarketValue: number;
  currency: string;
  asOf: string;
}

interface AumHistory {
  accountId: string;
  currency: string;
  valuations: { quarterEnd: string; aum: number; changePct: number | null }[];
}

interface Accounts {
  count: number;
  accounts: {
    accountId: string;
    name: string;
    householdId: string;
    modelPortfolio: string;
    currency: string;
  }[];
}

function Card({
  caption,
  csv,
  lang,
  badge,
  action,
  children,
}: {
  caption: string;
  csv: string;
  lang: Lang;
  badge?: ReactNode;
  action?: ReactNode;
  children: ReactNode;
}) {
  const [copied, setCopied] = useState(false);
  const w = words[lang];
  return (
    <section className={styles.card} data-testid="data-card">
      <div className={styles.head}>
        {badge}
        {action}
        <button
          type="button"
          className={styles.copy}
          onClick={(e) => {
            e.stopPropagation();
            void navigator.clipboard?.writeText(csv).then(() => setCopied(true));
          }}
        >
          {copied ? w.copied : w.copy}
        </button>
      </div>
      <div className={styles.scroll}>
        <table className={styles.table}>
          <caption>{caption}</caption>
          {children}
        </table>
      </div>
    </section>
  );
}

/** Drift against the model, with the tolerance band drawn around zero. */
function DriftBar({ drift, tolerance }: { drift: number; tolerance: number }) {
  const width = 72;
  const half = width / 2;
  const scale = half / Math.max(tolerance * 1.5, Math.abs(drift) * 1.1, 0.1);
  const band = tolerance * scale;
  const x = half + Math.max(-half, Math.min(half, drift * scale));
  return (
    <svg width={width} height={12} aria-hidden="true" className={styles.bar}>
      <rect x={half - band} y={3} width={band * 2} height={6} rx={2} className={styles.band} />
      <line x1={half} x2={half} y1={1} y2={11} className={styles.zero} />
      <circle
        cx={x}
        cy={6}
        r={3.5}
        className={Math.abs(drift) > tolerance ? styles.markOut : styles.mark}
      />
    </svg>
  );
}

function HoldingsCard({
  content: c,
  lang,
  ...focusing
}: { content: Holdings; lang: Lang } & Focusing) {
  const w = words[lang];
  const needed = c.rebalanceNeeded ?? c.outsideTolerance;
  const side = (h: Holding) =>
    h.tradeSide === 'buy' ? w.buy : h.tradeSide === 'sell' ? w.sell : w.none;
  const csv = toCsv(
    [
      'assetClass',
      'marketValue',
      'targetWeightPct',
      'actualWeightPct',
      'driftPct',
      'outsideTolerance',
      'tradeToTarget',
      'tradeSide',
      'weightAfterPct',
    ],
    c.holdings.map((h) => [
      h.assetClass,
      h.marketValue,
      h.targetWeightPct,
      h.actualWeightPct,
      h.driftPct,
      h.outsideTolerance ?? false,
      h.tradeToTarget ?? 0,
      h.tradeSide ?? 'none',
      h.weightAfterPct ?? h.targetWeightPct,
    ]),
  );
  return (
    <Card
      caption={`${c.accountId} · ${c.accountName} · ${w.asOf} ${date(c.asOf, lang)}`}
      csv={csv}
      lang={lang}
      action={<FocusButton accountId={c.accountId} lang={lang} {...focusing} />}
      badge={
        <span className={needed ? styles.badgeWarn : styles.badgeOk} data-testid="rebalance-badge">
          {needed ? `⚠ ${w.rebalanceNeeded}` : `✓ ${w.rebalanceNotNeeded}`} ({w.tolerance} ±
          {percent(c.driftTolerancePct, lang)})
        </span>
      }
    >
      <thead>
        <tr>
          <th scope="col">{w.assetClass}</th>
          <th scope="col" className={styles.num}>
            {w.value}
          </th>
          <th scope="col" className={styles.num}>
            {w.target}
          </th>
          <th scope="col" className={styles.num}>
            {w.actual}
          </th>
          <th scope="col">{w.drift}</th>
          <th scope="col" className={styles.num}>
            {w.trade}
          </th>
          <th scope="col" className={styles.num}>
            {w.after}
          </th>
        </tr>
      </thead>
      <tbody>
        {c.holdings.map((h) => (
          <tr key={h.assetClass} data-outside={h.outsideTolerance ? 'true' : undefined}>
            <th scope="row">{h.assetClass}</th>
            <td className={styles.num}>{money(h.marketValue, c.currency, lang)}</td>
            <td className={styles.num}>{percent(h.targetWeightPct, lang)}</td>
            <td className={styles.num}>{percent(h.actualWeightPct, lang)}</td>
            <td>
              <div className={styles.drift}>
                <DriftBar drift={h.driftPct} tolerance={c.driftTolerancePct} />
                <span className={styles.num}>{percent(h.driftPct, lang, true)}</span>
                {h.outsideTolerance && <span className={styles.outside}>⚠ {w.outside}</span>}
              </div>
            </td>
            <td className={styles.num}>
              {h.tradeToTarget ? (
                <>
                  <span className={h.tradeSide === 'buy' ? styles.buy : styles.sell}>
                    {side(h)}
                  </span>{' '}
                  {money(Math.abs(h.tradeToTarget), c.currency, lang)}
                </>
              ) : (
                w.none
              )}
            </td>
            <td className={styles.num}>{percent(h.weightAfterPct ?? h.targetWeightPct, lang)}</td>
          </tr>
        ))}
      </tbody>
      <tfoot>
        <tr>
          <th scope="row">{w.total}</th>
          <td className={styles.num}>{money(c.totalMarketValue, c.currency, lang)}</td>
          <td colSpan={5} />
        </tr>
      </tfoot>
    </Card>
  );
}

function AumHistoryCard({
  content: c,
  lang,
  ...focusing
}: { content: AumHistory; lang: Lang } & Focusing) {
  const w = words[lang];
  return (
    <Card
      caption={`${c.accountId} · ${w.aumOf}`}
      csv={toCsv(
        ['quarterEnd', 'aum', 'changePct'],
        c.valuations.map((v) => [v.quarterEnd, v.aum, v.changePct]),
      )}
      lang={lang}
      action={<FocusButton accountId={c.accountId} lang={lang} {...focusing} />}
    >
      <thead>
        <tr>
          <th scope="col">{w.quarterEnd}</th>
          <th scope="col" className={styles.num}>
            {w.aum}
          </th>
          <th scope="col" className={styles.num}>
            {w.change}
          </th>
        </tr>
      </thead>
      <tbody>
        {c.valuations.map((v) => (
          <tr key={v.quarterEnd}>
            <th scope="row">{date(v.quarterEnd, lang)}</th>
            <td className={styles.num}>{money(v.aum, c.currency, lang)}</td>
            <td className={styles.num}>
              {v.changePct == null ? w.none : percent(v.changePct, lang, true)}
            </td>
          </tr>
        ))}
      </tbody>
    </Card>
  );
}

function AccountsCard({
  content: c,
  lang,
  ...focusing
}: { content: Accounts; lang: Lang } & Focusing) {
  const w = words[lang];
  return (
    <Card
      caption={`${w.myAccounts} (${c.count})`}
      csv={toCsv(
        ['accountId', 'name', 'householdId', 'modelPortfolio', 'currency'],
        c.accounts.map((a) => [a.accountId, a.name, a.householdId, a.modelPortfolio, a.currency]),
      )}
      lang={lang}
    >
      <thead>
        <tr>
          <th scope="col">{w.account}</th>
          <th scope="col">{w.name}</th>
          <th scope="col">{w.household}</th>
          <th scope="col">{w.model}</th>
          <th scope="col">{w.currency}</th>
          {focusing.onFocus && (
            <th scope="col">
              <span className={styles.visuallyHidden}>{w.focus}</span>
            </th>
          )}
        </tr>
      </thead>
      <tbody>
        {c.accounts.map((a) => (
          <tr key={a.accountId}>
            <th scope="row">{a.accountId}</th>
            <td>{a.name}</td>
            <td>{a.householdId}</td>
            <td>{a.modelPortfolio}</td>
            <td>{a.currency}</td>
            {focusing.onFocus && (
              <td>
                <FocusButton accountId={a.accountId} lang={lang} {...focusing} />
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </Card>
  );
}
