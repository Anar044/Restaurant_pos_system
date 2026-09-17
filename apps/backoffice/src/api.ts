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

export type ProductPrice = {
  id: string;
  amount: number;
  currencyCode: string;
  validFrom: string;
  validTo: string | null;
};

export type KitchenStation = {
  id: string;
  name: string;
  isActive: boolean;
};

export type MenuProduct = {
  id: string;
  categoryId: string;
  kitchenStationId: string | null;
  kitchenStationName: string | null;
  name: string;
  sku: string | null;
  sortOrder: number;
  isActive: boolean;
  currentPrice: ProductPrice | null;
  priceHistory: ProductPrice[];
};

export type MenuCategory = {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  products: MenuProduct[];
};

export type BackOfficeMenu = {
  currencyCode: string;
  kitchenStations: KitchenStation[];
  categories: MenuCategory[];
};

export type CreateCategoryInput = {
  name: string;
  sortOrder: number;
};

export type UpdateCategoryInput = CreateCategoryInput & {
  isActive: boolean;
};

export type CreateProductInput = {
  categoryId: string;
  kitchenStationId: string | null;
  name: string;
  sku: string | null;
  sortOrder: number;
  price: number;
};

export type UpdateProductInput = {
  categoryId: string;
  kitchenStationId: string | null;
  name: string;
  sku: string | null;
  sortOrder: number;
  isActive: boolean;
};

export type KitchenProductSummary = {
  id: string;
  name: string;
  sku: string | null;
  isActive: boolean;
  categoryId: string;
  categoryName: string | null;
};

export type BackOfficeKitchenStation = KitchenStation & {
  activeProductCount: number;
  totalProductCount: number;
  products: KitchenProductSummary[];
};

export type BackOfficeKitchen = {
  stations: BackOfficeKitchenStation[];
  unassignedProducts: KitchenProductSummary[];
};

export type CreateKitchenStationInput = {
  name: string;
};

export type UpdateKitchenStationInput = {
  name: string;
  isActive: boolean;
};

export type EmployeeRole = {
  id: string;
  name: string;
  permissions: string[];
  employeeCount: number;
  activeEmployeeCount: number;
};

export type BackOfficeEmployee = {
  id: string;
  name: string;
  roleId: string;
  roleName: string | null;
  isActive: boolean;
  createdAt: string;
};

export type BackOfficeEmployees = {
  employees: BackOfficeEmployee[];
  roles: EmployeeRole[];
  availablePermissions: string[];
};

export type CreateRoleInput = {
  name: string;
  permissions: string[];
};

export type UpdateRoleInput = CreateRoleInput;

export type CreateEmployeeInput = {
  name: string;
  roleId: string;
  pin: string;
};

export type UpdateEmployeeInput = {
  name: string;
  roleId: string;
  isActive: boolean;
  newPin: string | null;
};

export type EquipmentDevice = {
  id: string;
  name: string;
  type: string;
  receiptPrinterId: string | null;
  receiptPrinterName: string | null;
  isActive: boolean;
  lastSeenAt: string | null;
  isOnline: boolean;
};

export type EquipmentPrinter = {
  id: string;
  name: string;
  connectionType: string;
  address: string;
  port: number | null;
  isActive: boolean;
  lastSeenAt: string | null;
  isOnline: boolean;
  kitchenStationCount: number;
  posDeviceCount: number;
};

export type EquipmentKitchenStation = {
  id: string;
  name: string;
  isActive: boolean;
  printerId: string | null;
  printerName: string | null;
};

export type BackOfficeEquipment = {
  deviceTypes: string[];
  printerConnectionTypes: string[];
  devices: EquipmentDevice[];
  printers: EquipmentPrinter[];
  kitchenStations: EquipmentKitchenStation[];
};

export type CreatePrinterInput = {
  name: string;
  connectionType: string;
  address: string;
  port: number | null;
};

export type UpdatePrinterInput = CreatePrinterInput & {
  isActive: boolean;
};

export type CreateDeviceInput = {
  name: string;
  type: string;
  receiptPrinterId: string | null;
};

export type UpdateDeviceInput = CreateDeviceInput & {
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

export async function getBackOfficeMenu(token: string): Promise<BackOfficeMenu> {
  return request<BackOfficeMenu>('/api/v1/backoffice/menu', {}, token);
}

export async function createCategory(
  token: string,
  input: CreateCategoryInput,
): Promise<Omit<MenuCategory, 'products'>> {
  return request('/api/v1/backoffice/menu/categories', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateCategory(
  token: string,
  categoryId: string,
  input: UpdateCategoryInput,
): Promise<Omit<MenuCategory, 'products'>> {
  return request(`/api/v1/backoffice/menu/categories/${categoryId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createProduct(
  token: string,
  input: CreateProductInput,
): Promise<{ id: string }> {
  return request('/api/v1/backoffice/menu/products', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateProduct(
  token: string,
  productId: string,
  input: UpdateProductInput,
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/menu/products/${productId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function updateProductPrice(
  token: string,
  productId: string,
  amount: number,
): Promise<ProductPrice> {
  return request(`/api/v1/backoffice/menu/products/${productId}/price`, {
    method: 'PUT',
    body: JSON.stringify({ amount }),
  }, token);
}

export async function getBackOfficeKitchen(token: string): Promise<BackOfficeKitchen> {
  return request<BackOfficeKitchen>('/api/v1/backoffice/kitchen', {}, token);
}

export async function createKitchenStation(
  token: string,
  input: CreateKitchenStationInput,
): Promise<BackOfficeKitchenStation> {
  return request('/api/v1/backoffice/kitchen/stations', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateKitchenStation(
  token: string,
  stationId: string,
  input: UpdateKitchenStationInput,
): Promise<KitchenStation> {
  return request(`/api/v1/backoffice/kitchen/stations/${stationId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function getBackOfficeEmployees(token: string): Promise<BackOfficeEmployees> {
  return request<BackOfficeEmployees>('/api/v1/backoffice/employees', {}, token);
}

export async function createRole(
  token: string,
  input: CreateRoleInput,
): Promise<EmployeeRole> {
  return request('/api/v1/backoffice/employees/roles', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateRole(
  token: string,
  roleId: string,
  input: UpdateRoleInput,
): Promise<EmployeeRole> {
  return request(`/api/v1/backoffice/employees/roles/${roleId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createEmployee(
  token: string,
  input: CreateEmployeeInput,
): Promise<BackOfficeEmployee> {
  return request('/api/v1/backoffice/employees', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateEmployee(
  token: string,
  employeeId: string,
  input: UpdateEmployeeInput,
): Promise<BackOfficeEmployee> {
  return request(`/api/v1/backoffice/employees/${employeeId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function getBackOfficeEquipment(token: string): Promise<BackOfficeEquipment> {
  return request<BackOfficeEquipment>('/api/v1/backoffice/devices', {}, token);
}

export async function createPrinter(
  token: string,
  input: CreatePrinterInput,
): Promise<{ id: string }> {
  return request('/api/v1/backoffice/devices/printers', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updatePrinter(
  token: string,
  printerId: string,
  input: UpdatePrinterInput,
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/devices/printers/${printerId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createDevice(
  token: string,
  input: CreateDeviceInput,
): Promise<{ id: string }> {
  return request('/api/v1/backoffice/devices/terminals', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateDevice(
  token: string,
  deviceId: string,
  input: UpdateDeviceInput,
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/devices/terminals/${deviceId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function assignKitchenPrinter(
  token: string,
  stationId: string,
  printerId: string | null,
): Promise<{ id: string; printerId: string | null }> {
  return request(`/api/v1/backoffice/devices/kitchen-stations/${stationId}/printer`, {
    method: 'PUT',
    body: JSON.stringify({ printerId }),
  }, token);
}
