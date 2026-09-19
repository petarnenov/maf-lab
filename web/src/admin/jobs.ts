import type { AdminJob } from '../api/types';

/** A job is active (queued or running) until it reaches succeeded or failed; polling stops then. */
export const isJobActive = (job: AdminJob | null | undefined) =>
  !!job && job.state !== 'succeeded' && job.state !== 'failed';
