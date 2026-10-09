import { useCallback, useContext } from 'react';
import {
  apiRequest,
  ApiError,
  authHeaders,
  errorMessage,
  type RequestOptions,
} from '../api/client';
import { AuthContext, type AuthState } from './AuthContext';

export function useAuth(): AuthState {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth must be used inside <AuthProvider>.');
  return auth;
}

/** Returns a request function bound to the current session's token. */
export function useApi() {
  const { session } = useAuth();
  const token = session?.token ?? null;
  return useCallback(
    <T>(path: string, options?: RequestOptions) => apiRequest<T>(token, path, options),
    [token],
  );
}

/** A text response bound to the current session, using the same errors and cancellation as JSON reads. */
export function useApiText() {
  const { session } = useAuth();
  const token = session?.token ?? null;
  return useCallback(
    async (path: string, signal?: AbortSignal) => {
      const response = await fetch(path, { headers: authHeaders(token), signal });
      if (!response.ok) throw new ApiError(response.status, await errorMessage(response));
      return response.text();
    },
    [token],
  );
}
