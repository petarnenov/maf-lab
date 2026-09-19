import { createContext } from 'react';
import type { Session } from './session';

export interface AuthState {
  session: Session | null;
  setSession: (session: Session | null) => void;
}

export const AuthContext = createContext<AuthState | null>(null);
