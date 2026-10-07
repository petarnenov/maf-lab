import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import type { PluginCardProps } from '@maf/plugin-api';
import { money, percent, toCsv } from '@maf/shared/format';
import portfolio from './index';

/** A data card as the chat holds it. */
interface DataCard {
  messageId: string;
  activityType: string;
  content: unknown;
}

/** A card drawn by this plugin's renderer for its activity type, as the core's card view does it. */
function CardView({ card, ...rest }: Omit<PluginCardProps, 'content'> & { card: DataCard }) {
  const Renderer = portfolio.cards?.[card.activityType];
  return Renderer ? <Renderer content={card.content} {...rest} /> : null;
}

const holdings = (overrides: Record<string, unknown> = {}): DataCard => ({
  messageId: 'card-c1',
  activityType: 'maf-lab/holdings',
  content: {
    accountId: 'A-1043',
    accountName: 'Calder Retirement Plan',
    householdId: 'HH-CALDER',
    modelPortfolio: 'ACME-BALANCED-60-40',
    driftTolerancePct: 5,
    outsideTolerance: false,
    rebalanceNeeded: false,
    holdings: [
      {
        assetClass: 'US equity',
        marketValue: 268000,
        targetWeightPct: 20,
        actualWeightPct: 20.6,
        driftPct: 0.6,
        outsideTolerance: false,
        tradeToTarget: -8000,
        tradeSide: 'sell',
        weightAfterPct: 20,
      },
      {
        assetClass: 'Core bonds',
        marketValue: 780000,
        targetWeightPct: 60,
        actualWeightPct: 60,
        driftPct: 0,
        outsideTolerance: false,
        tradeToTarget: 0,
        tradeSide: 'none',
        weightAfterPct: 60,
      },
      {
        assetClass: 'Cash',
        marketValue: 121000,
        targetWeightPct: 10,
        actualWeightPct: 9.3,
        driftPct: -0.7,
        outsideTolerance: false,
        tradeToTarget: 9000,
        tradeSide: 'buy',
        weightAfterPct: 10,
      },
    ],
    totalMarketValue: 1300000,
    currency: 'USD',
    asOf: '2026-09-30',
    ...overrides,
  },
});

/** Intl writes non-breaking spaces; the reader sees spaces. */
const plain = (s: string | null | undefined) => (s ?? '').replace(/[\u00a0\u202f]/g, ' ');

describe('card formatting', () => {
  it('writes money and weights the Bulgarian way for a Cyrillic question', () => {
    expect(plain(money(268000, 'USD', 'bg'))).toBe('268 000 $');
    expect(plain(percent(20.6, 'bg'))).toBe('20,6 %');
  });

  it('writes them the English way otherwise', () => {
    expect(money(268000, 'USD', 'en')).toBe('$268,000');
    expect(percent(20.6, 'en')).toBe('20.6%');
  });

  it('writes CSV with a header, plain numbers and quoted text', () => {
    expect(
      toCsv(
        ['a', 'b'],
        [
          ['x, y', 1200],
          ['z', -8000],
        ],
      ),
    ).toBe('a,b\n"x, y",1200\nz,-8000');
  });
});

describe('portfolio cards', () => {
  it('draws a holdings card as a table with a caption, row headers and the plan in words', () => {
    render(<CardView card={holdings()} question="Препоръчай ребалансиране за A-1043" />);

    const table = screen.getByRole('table');
    expect(within(table).getByText(/A-1043 · Calder Retirement Plan/)).toBeInTheDocument();
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((h) => h.textContent),
    ).toContain('Сделка');
    const us = within(table).getByRole('rowheader', { name: 'US equity' }).closest('tr')!;
    expect(plain(us.textContent)).toContain('268 000 $');
    expect(plain(us.textContent)).toContain('20,6 %');
    expect(within(us).getByText('Продажба')).toBeInTheDocument();
    const cash = within(table).getByRole('rowheader', { name: 'Cash' }).closest('tr')!;
    expect(within(cash).getByText('Покупка')).toBeInTheDocument();
    expect(
      plain(within(table).getByRole('rowheader', { name: 'Общо' }).closest('tr')!.textContent),
    ).toContain('1 300 000 $');
    expect(screen.getByTestId('rebalance-badge')).toHaveTextContent('Не е нужно ребалансиране');
  });

  it('says so in words when a class is outside its tolerance', () => {
    const card = holdings({
      rebalanceNeeded: true,
      outsideTolerance: true,
      holdings: [
        {
          assetClass: 'US equity',
          marketValue: 1519560,
          targetWeightPct: 40,
          actualWeightPct: 46.9,
          driftPct: 6.9,
          outsideTolerance: true,
          tradeToTarget: -223560,
          tradeSide: 'sell',
          weightAfterPct: 40,
        },
      ],
    });
    render(<CardView card={card} question="Does A-1042 need rebalancing?" />);

    const row = screen.getByRole('rowheader', { name: 'US equity' }).closest('tr')!;
    expect(within(row).getByText(/outside tolerance/)).toBeInTheDocument();
    expect(within(row).getByText('Sell')).toBeInTheDocument();
    expect(screen.getByTestId('rebalance-badge')).toHaveTextContent('Rebalance needed');
  });

  it('copies its rows as CSV', async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    render(<CardView card={holdings()} question="Rebalance A-1043" />);

    await user.click(screen.getByRole('button', { name: 'Copy as CSV' }));

    const csv = writeText.mock.calls[0][0];
    expect(csv.split('\n')[0]).toBe(
      'assetClass,marketValue,targetWeightPct,actualWeightPct,driftPct,outsideTolerance,tradeToTarget,tradeSide,weightAfterPct',
    );
    expect(csv).toContain('US equity,268000,20,20.6,0.6,false,-8000,sell,20');
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
  });

  it('draws the AUM history and the account list', () => {
    render(
      <>
        <CardView
          card={{
            messageId: 'card-2',
            activityType: 'maf-lab/aum-history',
            content: {
              accountId: 'A-1043',
              householdId: 'HH',
              currency: 'USD',
              valuations: [
                { quarterEnd: '2026-06-30', aum: 1250000, changePct: null },
                { quarterEnd: '2026-09-30', aum: 1300000, changePct: 4 },
              ],
            },
          }}
          question="AUM of A-1043"
        />
        <CardView
          card={{
            messageId: 'card-3',
            activityType: 'maf-lab/accounts',
            content: {
              count: 1,
              accounts: [
                {
                  accountId: 'A-1042',
                  name: 'Ridgeline Family Trust',
                  householdId: 'HH-RIDGELINE',
                  modelPortfolio: 'M',
                  currency: 'USD',
                },
              ],
            },
          }}
          question="my accounts"
        />
      </>,
    );

    const [aum, accounts] = screen.getAllByRole('table');
    expect(within(aum).getByText('+4.0%')).toBeInTheDocument();
    expect(within(accounts).getByRole('rowheader', { name: 'A-1042' })).toBeInTheDocument();
  });

  it('draws nothing for a card type it does not know', () => {
    const { container } = render(
      <CardView
        card={{ messageId: 'x', activityType: 'maf-lab/something-new', content: {} }}
        question="q"
      />,
    );
    expect(container).toBeEmptyDOMElement();
  });
});
