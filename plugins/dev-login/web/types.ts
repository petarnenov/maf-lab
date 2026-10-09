import type { SessionUser, Role } from '@maf/plugin-api';
export type DevUser = SessionUser;
export interface DevTokenRequest {
  userId: string;
  tenantId: string;
  role: Role;
  audience: string;
}
export interface DevTokenResponse {
  token: string;
  expiresAt: string;
}
