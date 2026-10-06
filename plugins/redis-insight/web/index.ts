import { definePlugin } from '@maf/plugin-api';

// A developer tool beside the lab, on the same host as this page (its port is published on loopback only), opened in a
// new tab. No token travels in the link.
const href = `${window.location.protocol}//${window.location.hostname}:7174`;

export default definePlugin({
  name: 'redis-insight',
  nav: [{ label: 'Redis Insight', href }],
});
