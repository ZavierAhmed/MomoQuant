import { apiRequest } from '@/api/apiClient';
import type { LoginRequest, LoginResponse, UserProfile } from '@/api/types';

export async function login(request: LoginRequest): Promise<LoginResponse> {
  return apiRequest<LoginResponse>('/auth/login', {
    method: 'POST',
    body: request,
    auth: false,
  });
}

export async function getCurrentUser(): Promise<UserProfile> {
  return apiRequest<UserProfile>('/auth/me');
}

export async function logout(): Promise<void> {
  await apiRequest<{ loggedOut: boolean }>('/auth/logout', { method: 'POST' });
}
