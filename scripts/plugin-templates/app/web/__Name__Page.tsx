import { useQuery } from '@tanstack/react-query';
import { useApi, useUserKey } from '@maf/plugin-api';

interface ${Name}Summary {
  conversations: number;
  moreThanAPage: boolean;
}

/** The $title page: what the plugin's one route says about the signed-in user. */
export function ${Name}Page() {
  const api = useApi();
  const userKey = useUserKey();
  const summary = useQuery({
    queryKey: ['$name', userKey],
    queryFn: ({ signal }) => api<${Name}Summary>('/api/$name/summary', { signal }),
  });
  return (
    <section aria-label="$title">
      <h1>$title</h1>
      {summary.isLoading && <p role="status">Loading…</p>}
      {summary.isError && <p role="alert">Could not load the summary.</p>}
      {summary.data && (
        <p>
          Conversations: {summary.data.conversations}
          {summary.data.moreThanAPage ? '+' : ''}
        </p>
      )}
    </section>
  );
}
