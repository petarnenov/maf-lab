export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
}

export function authHeaders(token: string | null): Record<string, string> {
  return token ? { Authorization: `Bearer ${token}` } : {};
}

/** JSON request against the API. Throws ApiError on non-2xx; returns undefined for 204/empty bodies. */
export async function apiRequest<T>(
  token: string | null,
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...authHeaders(token) };
  let body: string | undefined;
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json';
    body = JSON.stringify(options.body);
  }

  const response = await fetch(path, {
    method: options.method ?? (body === undefined ? 'GET' : 'POST'),
    headers,
    body,
    signal: options.signal,
  });

  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response));
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

/** Plain-text request: the topology diagram is XML, not JSON. Same auth and error handling. */
export async function apiText(
  token: string | null,
  path: string,
  signal?: AbortSignal,
): Promise<string> {
  const response = await fetch(path, { headers: authHeaders(token), signal });
  if (!response.ok) {
    throw new ApiError(response.status, await errorMessage(response));
  }
  return response.text();
}

async function errorMessage(response: Response): Promise<string> {
  if (response.status === 401) return 'Not signed in — pick a dev persona.';
  if (response.status === 403) return 'Access denied.';
  if (response.status === 404) return 'Not found.';
  try {
    const text = await response.text();
    if (text) {
      const parsed = JSON.parse(text) as { title?: string; detail?: string; error?: string };
      return (
        parsed.detail ?? parsed.error ?? parsed.title ?? `Request failed (${response.status}).`
      );
    }
  } catch {
    // Non-JSON body: fall through to the generic message.
  }
  return `Request failed (${response.status}).`;
}
