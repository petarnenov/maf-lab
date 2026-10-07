import { definePlugin } from '@maf/plugin-api';
import { FeeAdjustmentSummary } from './FeeAdjustmentSummary';

// The billing plugin's web part (generalize-write-confirmation): how a fee adjustment waiting for the advisor reads.
export default definePlugin({
  name: 'billing',
  confirmations: {
    propose_fee_adjustment: FeeAdjustmentSummary,
  },
});
