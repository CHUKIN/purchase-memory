import { createContext, useContext } from "react";
import type { CurrentUser } from "../api/client";

export interface AuthContextValue {
  user: CurrentUser;
  /** Null in the local development bypass, where there is no session to end. */
  signOut: (() => Promise<void>) | null;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

/** Null outside the auth gate (tests and stories render the app directly). */
export function useAuth(): AuthContextValue | null {
  return useContext(AuthContext);
}
