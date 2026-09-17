export const API_BASE_URL =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ??
  'http://127.0.0.1:8080';

export const DEVELOPMENT_RESTAURANT_ID =
  (import.meta.env.VITE_RESTAURANT_ID as string | undefined) ??
  '11111111-1111-1111-1111-111111111111';

export type AuthSession = {
  token: string;
  expiresAt: string;
  employeeId: string;
  employeeName: string;
  roleName: string;
  organizationId: string;
  restaurantId: string;
};

export type BackOfficeContext = {
  organization: {
    id: string;
    name: string;
    isActive: boolean;
  };
  restaurant: {
    id: string;
    organizationId: string;
    name: string;
    currencyCode: string;
    timeZone: string;
    isActive: boolean;
  };
};

export type DiningTable = {
  id: string;
  hallId: string;
  name: string;
  seats: number;
  sortOrder: number;
  isActive: boolean;
};

export type Hall = {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  tables: DiningTable[];
};

export type CreateHallInput = {
  name: string;
  sortOrder: number;
};

export type UpdateHallInput = CreateHallInput & {
  isActive: boolean;
};

export type CreateTableInput = {
  name: string;
  seats: number;
  sortOrder: number;
};

export type UpdateTableInput = CreateTableInput & {
  hallId: string;
  isActive: boolean;
};

async function readError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { message?: string; title?: string };
    return body.message ?? body.title ?? `HTTP ${response.status}`;
  } catch {
    return `HTTP ${response.status}`;
  }
}

async function request<T>(
  path: string,
  options: RequestInit = {},
  token?: string,
): Promise<T> {
  const headers = new Headers(options.headers);
  if (options.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) headers.set('Authorization', `Bearer ${token}`);

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers,
  });

  if (!response.ok) throw new Error(await readError(response));
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export async function loginWithPin(
  restaurantId: string,
  pin: string,
): Promise<AuthSession> {
  return request<AuthSession>('/api/v1/auth/pin', {
    method: 'POST',
    body: JSON.stringify({ restaurantId, pin }),
  });
}

export async function getBackOfficeContext(
  token: string,
): Promise<BackOfficeContext> {
  return request<BackOfficeContext>('/api/v1/backoffice/context', {}, token);
}

export async function getHalls(token: string): Promise<Hall[]> {
  return request<Hall[]>('/api/v1/backoffice/halls', {}, token);
}

export async function createHall(
  token: string,
  input: CreateHallInput,
): Promise<Omit<Hall, 'tables'>> {
  return request('/api/v1/backoffice/halls', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateHall(
  token: string,
  hallId: string,
  input: UpdateHallInput,
): Promise<Omit<Hall, 'tables'>> {
  return request(`/api/v1/backoffice/halls/${hallId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createTable(
  token: string,
  hallId: string,
  input: CreateTableInput,
): Promise<DiningTable> {
  return request(`/api/v1/backoffice/halls/${hallId}/tables`, {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateTable(
  token: string,
  tableId: string,
  input: UpdateTableInput,
): Promise<DiningTable> {
  return request(`/api/v1/backoffice/tables/${tableId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}
