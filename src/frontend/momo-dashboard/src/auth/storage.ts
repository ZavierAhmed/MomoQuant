const TOKEN_KEY = 'momo.accessToken';
const SESSION_KEY = 'momo.session';

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function getStoredSession(): string | null {
  return localStorage.getItem(SESSION_KEY);
}

export function persistAuth(accessToken: string, sessionJson: string): void {
  localStorage.setItem(TOKEN_KEY, accessToken);
  localStorage.setItem(SESSION_KEY, sessionJson);
}

export function clearStoredAuth(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(SESSION_KEY);
}

export { TOKEN_KEY, SESSION_KEY };
