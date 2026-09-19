import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { AuthContext } from './AuthContext';
import { loadSession, saveSession, type Session } from './session';

export function AuthProvider({
  children,
  initialSession,
}: {
  children: ReactNode;
  initialSession?: Session | null;
}) {
  const [session, setSessionState] = useState<Session | null>(() =>
    initialSession !== undefined ? initialSession : loadSession(),
  );

  const setSession = useCallback((next: Session | null) => {
    saveSession(next);
    setSessionState(next);
  }, []);

  const value = useMemo(() => ({ session, setSession }), [session, setSession]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
