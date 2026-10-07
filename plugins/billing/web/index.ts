import { definePlugin } from '@maf/plugin-api';
import { FeeAdjustmentSummary } from './FeeAdjustmentSummary';
import { billingToolLabels } from './toolLabels';

// The billing plugin's web part (generalize-write-confirmation): how a fee adjustment waiting for the advisor reads, and
// how its tools read in the chat (extract-portfolio).
export default definePlugin({
  name: 'billing',
  confirmations: {
    propose_fee_adjustment: FeeAdjustmentSummary,
  },
  toolLabels: billingToolLabels,
});
