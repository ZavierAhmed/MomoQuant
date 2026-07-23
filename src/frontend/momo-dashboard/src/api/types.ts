export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export type UserRole = 'Admin' | 'Trader' | 'Viewer';

export interface ApiError {
  field?: string;
  message: string;
}

export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data?: T;
  errors?: ApiError[];
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
}

export interface UserProfile {
  userId: number;
  fullName: string;
  email: string;
  role: UserRole;
  isActive: boolean;
}

export interface AuthSession {
  accessToken: string;
  expiresAtUtc: string;
  user: UserProfile;
}

export interface NavItem {
  path: string;
  label: string;
  roles: UserRole[];
  section: string;
}
