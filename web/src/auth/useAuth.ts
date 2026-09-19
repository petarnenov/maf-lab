import { useCallback, useContext } from 'react';
import { apiRequest, type RequestOptions } from '../api/client';
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
