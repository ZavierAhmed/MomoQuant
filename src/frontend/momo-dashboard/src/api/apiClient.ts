import type { ApiResponse } from '@/api/types';
import { buildQuery } from '@/api/query';
import { getStoredToken } from '@/auth/storage';
import { logApiError, logApiRequest, logApiResponse } from '@/utils/apiDebug';
import { errorsFromApiList } from '@/utils/apiError';

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? 'https://localhost:7295/api/v1';
const DEFAULT_TIMEOUT_MS = Number(import.meta.env.VITE_API_TIMEOUT_MS ?? 30_000);

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export class ApiClientError extends Error {
  status: number;
  errors?: ApiResponse<unknown>['errors'];
  traceId?: string;
  code?: string;

  constructor(
    message: string,
    status: number,
    errors?: ApiResponse<unknown>['errors'],
    traceId?: string,
    code?: string,
  ) {
    super(message);
    this.name = 'ApiClientError';
    this.status = status;
    this.errors = errors;
    this.traceId = traceId;
    this.code = code;
  }
}

let unauthorizedHandler: (() => void) | null = null;
let forbiddenHandler: ((error: ApiClientError) => void) | null = null;

export function setUnauthorizedHandler(handler: () => void): void {
  unauthorizedHandler = handler;
}

export function setForbiddenHandler(handler: (error: ApiClientError) => void): void {
  forbiddenHandler = handler;
}

export function unwrapApiResponse<T>(payload: unknown): T {
  if (payload && typeof payload === 'object' && 'data' in payload) {
    const wrapped = payload as ApiResponse<T>;
    if (wrapped.data !== undefined) {
      return wrapped.data;
    }
  }

  return payload as T;
}

async function parseResponse<T>(response: Response): Promise<ApiResponse<T>> {
  const contentType = response.headers.get('content-type');
  if (contentType?.includes('application/json')) {
    return (await response.json()) as ApiResponse<T>;
  }

  return {
    success: response.ok,
    message: response.statusText || 'Unexpected response.',
  };
}

function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
    return crypto.randomUUID();
  }
  return `corr-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

export async function apiRequest<T>(
  path: string,
  options: {
    method?: HttpMethod;
    body?: unknown;
    auth?: boolean;
    query?: object;
    signal?: AbortSignal;
    timeoutMs?: number;
    correlationId?: string;
  } = {},
): Promise<T> {
  const {
    method = 'GET',
    body,
    auth = true,
    query,
    signal,
    timeoutMs = DEFAULT_TIMEOUT_MS,
    correlationId = createCorrelationId(),
  } = options;
  const endpoint = `${baseUrl}${path}`;
  logApiRequest(`${method} ${path}`, body ?? query ?? null);

  const headers: Record<string, string> = {
    Accept: 'application/json',
    'X-Correlation-Id': correlationId,
  };

  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  if (auth) {
    const token = getStoredToken();
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
  }

  const controller = new AbortController();
  const onAbort = () => controller.abort(signal?.reason);
  if (signal) {
    if (signal.aborted) {
      controller.abort(signal.reason);
    } else {
      signal.addEventListener('abort', onAbort, { once: true });
    }
  }

  const timeoutId =
    timeoutMs > 0
      ? setTimeout(() => controller.abort(new DOMException('API request timed out.', 'TimeoutError')), timeoutMs)
      : null;

  let response: Response;
  try {
    response = await fetch(`${endpoint}${buildQuery(query)}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });
  } catch (error) {
    if (timeoutId) clearTimeout(timeoutId);
    signal?.removeEventListener('abort', onAbort);
    if (error instanceof DOMException && (error.name === 'AbortError' || error.name === 'TimeoutError')) {
      const timedOut = error.name === 'TimeoutError' || error.message.includes('timed out');
      throw new ApiClientError(
        timedOut ? 'Request timed out.' : 'Request cancelled.',
        timedOut ? 408 : 499,
        undefined,
        correlationId,
        timedOut ? 'Timeout' : 'Cancelled',
      );
    }
    throw new ApiClientError(
      'Network unavailable.',
      0,
      undefined,
      correlationId,
      'NetworkUnavailable',
    );
  } finally {
    if (timeoutId) clearTimeout(timeoutId);
    signal?.removeEventListener('abort', onAbort);
  }

  const payload = await parseResponse<T>(response);
  logApiResponse(`${method} ${path}`, response.status, payload);

  const traceId = response.headers.get('x-correlation-id') ?? correlationId;

  if (response.status === 401 && auth) {
    unauthorizedHandler?.();
    throw new ApiClientError(payload.message || 'Unauthorized.', 401, payload.errors, traceId, 'Unauthorized');
  }

  if (response.status === 403) {
    const err = new ApiClientError(
      payload.message || 'Forbidden.',
      403,
      payload.errors,
      traceId,
      'Forbidden',
    );
    forbiddenHandler?.(err);
    throw err;
  }

  if (!response.ok || !payload.success) {
    const fieldErrors = errorsFromApiList(payload.errors);
    let message = payload.message || 'Request failed.';
    if (response.status === 400 && Object.keys(fieldErrors).length > 0) {
      message = Object.values(fieldErrors).join(' ');
    }
    logApiError(`${method} ${path}`, response.status, message);
    throw new ApiClientError(message, response.status, payload.errors, traceId);
  }

  if (payload.data === undefined) {
    throw new ApiClientError('Response did not include data.', response.status, payload.errors, traceId);
  }

  return payload.data;
}

export function getApiBaseUrl(): string {
  return baseUrl;
}
