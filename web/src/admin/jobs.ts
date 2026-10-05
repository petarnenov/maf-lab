import type { AdminJob } from '../api/types';

/** A job is active (queued or running) until it ends — succeeded, failed or canceled; polling stops then. */
export const isJobActive = (job: AdminJob | null | undefined) =>
  !!job && job.state !== 'succeeded' && job.state !== 'failed' && job.state !== 'canceled';
