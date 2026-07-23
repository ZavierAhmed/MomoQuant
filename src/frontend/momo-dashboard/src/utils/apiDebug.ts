const enabled = import.meta.env.DEV;

export function logApiRequest(endpoint: string, payload: unknown): void {
  if (!enabled) {
    return;
  }

  console.debug('[API Request]', endpoint, sanitize(payload));
}

export function logApiResponse(endpoint: string, status: number, body: unknown): void {
  if (!enabled) {
    return;
  }

  console.debug('[API Response]', endpoint, status, sanitize(body));
}

export function logApiError(endpoint: string, status: number, message: string): void {
  if (!enabled) {
    return;
  }

  console.debug('[API Error]', endpoint, status, message);
}

function sanitize(value: unknown): unknown {
  if (value === null || value === undefined) {
    return value;
  }

  if (typeof value === 'string') {
    if (value.startsWith('Bearer ') || value.length > 500) {
      return '[redacted]';
    }
    return value;
  }

  if (Array.isArray(value)) {
    return value.map(sanitize);
  }

  if (typeof value === 'object') {
    const record = value as Record<string, unknown>;
    const result: Record<string, unknown> = {};
    for (const [key, nested] of Object.entries(record)) {
      if (/token|password|secret|authorization/i.test(key)) {
        result[key] = '[redacted]';
      } else {
        result[key] = sanitize(nested);
      }
    }
    return result;
  }

  return value;
}
