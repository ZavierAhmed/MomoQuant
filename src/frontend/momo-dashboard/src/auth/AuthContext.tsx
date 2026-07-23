import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { setUnauthorizedHandler } from '@/api/apiClient';
import { getCurrentUser, login as loginRequest, logout as logoutRequest } from '@/api/authApi';
import type { AuthSession, LoginRequest, UserProfile } from '@/api/types';
import { clearStoredAuth, getStoredSession, getStoredToken, persistAuth } from '@/auth/storage';

interface AuthContextValue {
  user: UserProfile | null;
  accessToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (request: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function readStoredSession(): AuthSession | null {
  const raw = getStoredSession();
  if (!raw) {
    return null;
  }

  try {
    return JSON.parse(raw) as AuthSession;
  } catch {
    clearStoredAuth();
    return null;
  }
}

function isExpired(expiresAtUtc: string): boolean {
  return new Date(expiresAtUtc).getTime() <= Date.now();
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const clearSession = useCallback(() => {
    clearStoredAuth();
    setUser(null);
    setAccessToken(null);
  }, []);

  const logout = useCallback(async () => {
    try {
      if (getStoredToken()) {
        await logoutRequest();
      }
    } catch {
      // Local session should still be cleared even if API logout fails.
    } finally {
      clearSession();
    }
  }, [clearSession]);

  const restoreSession = useCallback(async () => {
    const stored = readStoredSession();
    const token = getStoredToken();

    if (!stored || !token || isExpired(stored.expiresAtUtc)) {
      clearSession();
      setIsLoading(false);
      return;
    }

    setAccessToken(token);
    setUser(stored.user);

    try {
      const profile = await getCurrentUser();
      setUser(profile);
      persistAuth(token, JSON.stringify({ ...stored, user: profile }));
    } catch {
      clearSession();
    } finally {
      setIsLoading(false);
    }
  }, [clearSession]);

  useEffect(() => {
    setUnauthorizedHandler(() => {
      clearSession();
      window.location.assign('/signin');
    });

    void restoreSession();
  }, [clearSession, restoreSession]);

  const login = useCallback(async (request: LoginRequest) => {
    const response = await loginRequest(request);
    const profile: UserProfile = {
      userId: response.userId,
      fullName: response.fullName,
      email: response.email,
      role: response.role,
      isActive: true,
    };

    const session: AuthSession = {
      accessToken: response.accessToken,
      expiresAtUtc: response.expiresAtUtc,
      user: profile,
    };

    persistAuth(response.accessToken, JSON.stringify(session));
    setAccessToken(response.accessToken);
    setUser(profile);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      accessToken,
      isAuthenticated: Boolean(user && accessToken),
      isLoading,
      login,
      logout,
    }),
    [accessToken, isLoading, login, logout, user],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuthContext(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuthContext must be used within AuthProvider.');
  }

  return context;
}
