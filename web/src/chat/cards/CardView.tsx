import { useState, type ReactNode } from 'react';
import type { DataCard } from '../../api/types';
import styles from './Cards.module.css';
import { date, langOf, money, percent, toCsv, words, type Lang } from './format';

/**
 * A data card (add-activity-cards): a typed tool result drawn as a table, in the language of the question it answers.
 * A type this screen does not know draws nothing, as the protocol asks of an open activity type.
 */
export function CardView({ card, question }: { card: DataCard; question: string }) {
  const lang = langOf(question);
  switch (card.activityType) {
    case 'maf-lab/holdings':
      return <HoldingsCard content={card.content as unknown as Holdings} lang={lang} />;
    case 'maf-lab/aum-history':
      return <AumHistoryCard content={card.content as unknown as AumHistory} lang={lang} />;
    case 'maf-lab/accounts':
      return <AccountsCard content={card.content as unknown as Accounts} lang={lang} />;
    default:
      return null;
  }
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
  children,
}: {
  caption: string;
  csv: string;
  lang: Lang;
  badge?: ReactNode;
  children: ReactNode;
}) {
  const [copied, setCopied] = useState(false);
  const w = words[lang];
  return (
    <section className={styles.card} data-testid="data-card">
      <div className={styles.head}>
        {badge}
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

function HoldingsCard({ content: c, lang }: { content: Holdings; lang: Lang }) {
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

function AumHistoryCard({ content: c, lang }: { content: AumHistory; lang: Lang }) {
  const w = words[lang];
  return (
    <Card
      caption={`${c.accountId} · ${w.aumOf}`}
      csv={toCsv(
        ['quarterEnd', 'aum', 'changePct'],
        c.valuations.map((v) => [v.quarterEnd, v.aum, v.changePct]),
      )}
      lang={lang}
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

function AccountsCard({ content: c, lang }: { content: Accounts; lang: Lang }) {
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
          </tr>
        ))}
      </tbody>
    </Card>
  );
}
