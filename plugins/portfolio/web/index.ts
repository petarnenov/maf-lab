import { definePlugin } from '@maf/plugin-api';
import { AccountsCardView, AumHistoryCardView, HoldingsCardView } from './PortfolioCards';

// The portfolio plugin's web part (extract-portfolio): its three data cards, by AG-UI activity type, and how its tools
// read in the chat.
export default definePlugin({
  name: 'portfolio',
  cards: {
    'maf-lab/holdings': HoldingsCardView,
    'maf-lab/aum-history': AumHistoryCardView,
    'maf-lab/accounts': AccountsCardView,
  },
  toolLabels: {
    search_portfolio_documents: ({ running }) =>
      running ? 'Searching portfolio documentation…' : 'Searched portfolio documentation',
    get_household_portfolio: ({ running }) =>
      running ? 'Reading the portfolio…' : 'Read the portfolio',
    get_aum_history: ({ running }) =>
      running ? 'Reading quarter-end AUM…' : 'Read quarter-end AUM',
    list_my_accounts: ({ running }) =>
      running ? 'Listing your accounts…' : 'Listed your accounts',
  },
});
