import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import * as api from '../api/client';
import { ApiError, setUnauthorizedHandler, setToken } from '../api/client';
import type { AuthResponse, UserProfile } from '../api/types';

interface AuthContextValue {
  user: UserProfile | null;
  /** True while the stored token is being checked against GET /api/users/me. */
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string, name: string) => Promise<void>;
  logout: () => void;
  /** Re-reads the profile from the API (the JWT plan is a snapshot from login). */
  refresh: () => Promise<UserProfile | null>;
  setUser: (user: UserProfile | null) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(() => api.getToken() !== null);

  const logout = useCallback(() => {
    setToken(null);
    setUser(null);
  }, []);

  const refresh = useCallback(async () => {
    if (!api.getToken()) {
      setUser(null);
      return null;
    }
    try {
      const profile = await api.getMe();
      setUser(profile);
      return profile;
    } catch (error) {
      // A 401 is already handled globally; any other failure just means "not loaded".
      if (!(error instanceof ApiError)) setUser(null);
      return null;
    }
  }, []);

  // On first load, turn a stored token into a profile; a bad token logs the user out.
  useEffect(() => {
    let active = true;

    async function load() {
      if (!api.getToken()) {
        setLoading(false);
        return;
      }
      const profile = await refresh();
      if (active) setLoading(false);
      if (!profile) setToken(null);
    }

    void load();
    return () => {
      active = false;
    };
  }, [refresh]);

  // Any protected call that returns 401 drops the session.
  useEffect(() => {
    setUnauthorizedHandler(() => setUser(null));
    return () => setUnauthorizedHandler(null);
  }, []);

  const applyAuth = useCallback((auth: AuthResponse) => {
    setToken(auth.token);
    // The token's plan is frozen at login; the profile is re-read from the API below.
    setUser({
      id: '',
      email: auth.email,
      name: auth.name,
      planType: auth.planType,
      createdAt: '',
    });
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      applyAuth(await api.login({ email, password }));
      await refresh();
    },
    [applyAuth, refresh],
  );

  const register = useCallback(
    async (email: string, password: string, name: string) => {
      applyAuth(await api.register({ email, password, name }));
      await refresh();
    },
    [applyAuth, refresh],
  );

  const value = useMemo(
    () => ({ user, loading, login, register, logout, refresh, setUser }),
    [user, loading, login, register, logout, refresh],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>.');
  return context;
}
