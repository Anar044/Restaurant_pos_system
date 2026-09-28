export const API_BASE_URL =
  (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ??
  'http://127.0.0.1:8180';

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
  permissions: string[];
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
  groupId?: string;
  precheckPrinterId?: string | null;
  precheckPrinterName?: string | null;
  name: string;
  sortOrder: number;
  isActive: boolean;
  tables: DiningTable[];
};

export type CreateHallInput = {
  name: string;
  sortOrder: number;
  groupId?: string;
  precheckPrinterId?: string | null;
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
  departmentCount: number;
  posDeviceCount: number;
};

export type BackOfficeEquipment = {
  deviceTypes: string[];
  printerConnectionTypes: string[];
  devices: EquipmentDevice[];
  printers: EquipmentPrinter[];
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

export type PosWindowsPrinter = {
  id: string;
  name: string;
  queueName: string;
  isActive: boolean;
  lastSeenAt: string | null;
  isOnline: boolean;
  isSelectedReceipt: boolean;
};

export type PosPrinterDevice = {
  id: string;
  name: string;
  isActive: boolean;
  lastSeenAt: string | null;
  isOnline: boolean;
  receiptPrinterId: string | null;
  windowsPrinters: PosWindowsPrinter[];
};

export type PosNetworkPrinter = {
  id: string;
  name: string;
  address: string;
  port: number | null;
};

export type BackOfficePosPrinters = {
  posDevices: PosPrinterDevice[];
  networkPrinters: PosNetworkPrinter[];
};

export type FinanceCashRegister = {
  id: string;
  name: string;
  isActive: boolean;
};

export type FinanceSummary = {
  completedOrdersAmount: number;
  completedOrdersCount: number;
  openOrdersAmount: number;
  openOrdersCount: number;
  expectedRevenue: number;
  refundAmount: number;
  netExpectedRevenue: number;
  openShiftsCount: number;
  closedShiftsCount: number;
  cashDifference: number;
};

export type FinanceShift = {
  id: string;
  deviceId: string;
  deviceName: string;
  status: string;
  openedByEmployeeId: string;
  openedByEmployeeName: string | null;
  closedByEmployeeId: string | null;
  closedByEmployeeName: string | null;
  openingCash: number;
  closingCash: number | null;
  expectedCashAtClose: number;
  cashDifference: number | null;
  closingNote: string | null;
  openedAt: string;
  closedAt: string | null;
  ordersCount: number;
  grossSales: number;
  refunds: number;
  netSales: number;
  cashSales: number;
  cashRefunds: number;
  deposits: number;
  withdrawals: number;
};

export type FinanceShiftPaymentTotal = {
  method: string;
  gross: number;
  refunds: number;
  net: number;
};

export type FinanceCashTransaction = {
  id: string;
  type: 'DEPOSIT' | 'WITHDRAWAL';
  amount: number;
  reason: string | null;
  employeeId: string;
  employeeName: string | null;
  createdAt: string;
};

export type FinanceShiftReport = {
  shiftId: string;
  deviceId: string;
  deviceName: string | null;
  openedByEmployeeName: string | null;
  closedByEmployeeName: string | null;
  reportType: 'X' | 'Z';
  status: string;
  openedAt: string;
  closedAt: string | null;
  openingCash: number;
  closingCash: number | null;
  expectedCash: number;
  cashDifference: number | null;
  closingNote: string | null;
  ordersCount: number;
  paymentsCount: number;
  grossSales: number;
  refunds: number;
  netSales: number;
  cashSales: number;
  cashRefunds: number;
  deposits: number;
  withdrawals: number;
  payments: FinanceShiftPaymentTotal[];
  cashTransactions: FinanceCashTransaction[];
};

export type FinanceOrderPaymentMethod = {
  method: string;
  amount: number;
};

export type FinanceShiftOrder = {
  id: string;
  orderNumber: number;
  status: string;
  total: number;
  paidTotal: number;
  remaining: number;
  paidInShift: number;
  refundedInShift: number;
  netPaidInShift: number;
  guestCount: number;
  tableName: string | null;
  hallName: string | null;
  employeeName: string | null;
  openedInThisShift: boolean;
  paymentMethods: FinanceOrderPaymentMethod[];
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
};

export type FinancePayment = {
  id: string;
  orderId: string;
  orderNumber: number;
  orderStatus: string;
  shiftId: string;
  employeeId: string;
  employeeName: string;
  guestNumber: number | null;
  method: string;
  status: string;
  amount: number;
  tenderedAmount: number | null;
  changeAmount: number;
  refundedAmount: number;
  refundableAmount: number;
  currencyCode: string;
  providerReference: string | null;
  createdAt: string;
};

export type FinanceRefund = {
  id: string;
  paymentId: string;
  orderId: string;
  orderNumber: number;
  shiftId: string;
  employeeName: string;
  method: string;
  amount: number;
  currencyCode: string;
  reason: string | null;
  createdAt: string;
};

export type BackOfficeFinance = {
  period: {
    from: string;
    to: string;
  };
  cashRegisters: FinanceCashRegister[];
  summary: FinanceSummary;
  shifts: FinanceShift[];
  selectedShiftReport: FinanceShiftReport | null;
  selectedShiftOrders: FinanceShiftOrder[];
  payments: FinancePayment[];
  refunds: FinanceRefund[];
};

export type AdjustmentPresetRole = {
  id: string;
  name: string;
  canApplyAdjustments: boolean;
};

export type AdjustmentPresetCategory = {
  id: string;
  name: string;
  isActive: boolean;
};

export type AdjustmentPresetProduct = {
  id: string;
  categoryId: string;
  name: string;
  isActive: boolean;
};

export type BackOfficeAdjustmentPreset = {
  id: string;
  name: string;
  type: 'DISCOUNT' | 'SERVICE_CHARGE';
  mode: 'PERCENT' | 'FIXED';
  scope: 'ORDER' | 'GUEST' | 'BOTH';
  applicationMode: 'MANUAL' | 'AUTOMATIC';
  timeBasis: 'ORDER_OPENED_AT' | 'ITEM_ADDED_AT';
  targetMode: 'ALL_ITEMS' | 'PRESET_SELECTION' | 'POS_SELECTION';
  value: number;
  priority: number;
  canStack: boolean;
  weekdayMask: number;
  startMinute: number | null;
  endMinute: number | null;
  requireComment: boolean;
  isActive: boolean;
  roleIds: string[];
  productIds: string[];
  categoryIds: string[];
  createdAt: string;
  updatedAt: string;
};

export type BackOfficeAdjustments = {
  roles: AdjustmentPresetRole[];
  categories: AdjustmentPresetCategory[];
  products: AdjustmentPresetProduct[];
  presets: BackOfficeAdjustmentPreset[];
};

export type UpsertAdjustmentPresetInput = {
  name: string;
  type: 'DISCOUNT' | 'SERVICE_CHARGE';
  mode: 'PERCENT' | 'FIXED';
  scope: 'ORDER' | 'GUEST' | 'BOTH';
  applicationMode: 'MANUAL' | 'AUTOMATIC';
  timeBasis: 'ORDER_OPENED_AT' | 'ITEM_ADDED_AT';
  targetMode: 'ALL_ITEMS' | 'PRESET_SELECTION' | 'POS_SELECTION';
  value: number;
  priority: number;
  canStack: boolean;
  weekdayMask: number;
  startMinute: number | null;
  endMinute: number | null;
  requireComment: boolean;
  isActive: boolean;
  roleIds: string[];
  productIds: string[];
  categoryIds: string[];
};

export type BackOfficeModifier = {
  id: string;
  name: string;
  priceDelta: number;
  isActive: boolean;
  groupIds: string[];
};

export type BackOfficeModifierGroupItem = {
  id: string;
  name: string;
  priceDelta: number;
  isActive: boolean;
  sortOrder: number;
};

export type BackOfficeModifierGroup = {
  id: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  isRequired: boolean;
  isActive: boolean;
  modifiers: BackOfficeModifierGroupItem[];
  productIds: string[];
};

export type ModifierProduct = {
  id: string;
  name: string;
  isActive: boolean;
  groupIds: string[];
};

export type BackOfficeModifiers = {
  currencyCode: string;
  modifiers: BackOfficeModifier[];
  groups: BackOfficeModifierGroup[];
  products: ModifierProduct[];
};

export type RestaurantGroupOption = {
  id: string;
  name: string;
  isActive: boolean;
  deviceIds: string[];
  mainCashRegisterId: string | null;
};

export type GroupDepartmentOption = {
  id: string;
  groupId: string;
  name: string;
  preparationPlaceTypeId: string | null;
  preparationPlaceTypeName: string | null;
  warehouseId: string | null;
  warehouseName: string | null;
  printerId: string | null;
  printerName: string | null;
  isActive: boolean;
};

export type GroupHallOption = {
  id: string;
  groupId: string;
  name: string;
  sortOrder: number;
  precheckPrinterId: string | null;
  precheckPrinterName: string | null;
  tableCount: number;
  tables: DiningTable[];
  isActive: boolean;
};

export type GroupDeviceOption = {
  id: string;
  name: string;
  type: string;
  isActive: boolean;
};

export type GroupLookupOption = {
  id: string;
  name: string;
};

export type BackOfficeGroups = {
  groups: RestaurantGroupOption[];
  departments: GroupDepartmentOption[];
  halls: GroupHallOption[];
  types: PreparationPlaceTypeOption[];
  devices: GroupDeviceOption[];
  warehouses: GroupLookupOption[];
  printers: GroupLookupOption[];
};

export type PreparationPlaceTypeOption = {
  id: string;
  name: string;
  isActive: boolean;
};

export type NomenclatureRecipeLine = {
  id: string;
  ingredientProductId: string;
  ingredientName: string;
  ingredientUnit: string;
  quantity: number;
};

export type NomenclatureItem = {
  id: string;
  categoryId: string | null;
  categoryName: string | null;
  preparationPlaceTypeId: string | null;
  name: string;
  sku: string | null;
  type: 'DISH' | 'GOODS' | 'PREPARATION' | 'MODIFIER';
  unit: string;
  minStock: number;
  trackStock: boolean;
  isSellable: boolean;
  isActive: boolean;
  sortOrder: number;
  currentPrice: number | null;
  recipe: NomenclatureRecipeLine[];
};

export type NomenclatureCategoryOption = {
  id: string;
  name: string;
  isActive: boolean;
};

export type BackOfficeNomenclature = {
  supportedTypes: string[];
  currencyCode: string;
  categories: NomenclatureCategoryOption[];
  preparationPlaceTypes: PreparationPlaceTypeOption[];
  items: NomenclatureItem[];
};

export type UpsertNomenclatureItemInput = {
  name: string;
  sku: string | null;
  type: NomenclatureItem['type'];
  unit: string;
  minStock: number;
  trackStock: boolean;
  isSellable: boolean;
  isActive: boolean;
  sortOrder: number;
  categoryId: string | null;
  preparationPlaceTypeId: string | null;
  price: number | null;
};

export type InventoryWarehouse = {
  id: string;
  name: string;
  isActive: boolean;
  createdAt: string;
};

export type InventoryWarehouseBalance = {
  warehouseId: string;
  warehouseName: string;
  quantity: number;
};

export type InventoryNomenclatureItem = {
  id: string;
  name: string;
  sku: string | null;
  unit: string;
  minStock: number;
  isActive: boolean;
  createdAt: string;
  totalStock: number;
  warehouseBalances: InventoryWarehouseBalance[];
};

export type InventoryMovement = {
  id: string;
  warehouseId: string;
  warehouseName: string;
  productId: string;
  productName: string;
  unit: string;
  employeeId: string;
  type: 'RECEIPT' | 'WRITE_OFF';
  quantityDelta: number;
  note: string | null;
  createdAt: string;
};

export type BackOfficeInventory = {
  warehouses: InventoryWarehouse[];
  items: InventoryNomenclatureItem[];
  recentMovements: InventoryMovement[];
};

export type CreateWarehouseInput = {
  name: string;
};

export type UpdateWarehouseInput = {
  name: string;
  isActive: boolean;
};

export type CreateStockMovementInput = {
  warehouseId: string;
  productId: string;
  type: 'RECEIPT' | 'WRITE_OFF';
  quantity: number;
  note: string | null;
};

export type RefundPaymentResult = {
  refund: {
    id: string;
    paymentId: string;
    orderId: string;
    shiftId: string;
    employeeId: string;
    method: string;
    amount: number;
    currencyCode: string;
    reason: string | null;
    createdAt: string;
  };
  payment: {
    id: string;
    orderId: string;
    shiftId: string;
    employeeId: string;
    method: string;
    status: string;
    amount: number;
    tenderedAmount: number | null;
    changeAmount: number;
    refundedAmount: number;
    refundableAmount: number;
    currencyCode: string;
    providerReference: string | null;
    createdAt: string;
  };
  refundable: number;
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

export async function getBackOfficeModifiers(token: string): Promise<BackOfficeModifiers> {
  return request<BackOfficeModifiers>('/api/v1/backoffice/modifiers', {}, token);
}

export async function createModifierGroup(
  token: string,
  input: CreateModifierGroupInput,
): Promise<BackOfficeModifierGroup> {
  return request('/api/v1/backoffice/modifiers/groups', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateModifierGroup(
  token: string,
  groupId: string,
  input: UpdateModifierGroupInput,
): Promise<BackOfficeModifierGroup> {
  return request(`/api/v1/backoffice/modifiers/groups/${groupId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function setModifierGroupItems(
  token: string,
  groupId: string,
  modifierIds: string[],
): Promise<{ groupId: string; modifierIds: string[] }> {
  return request(`/api/v1/backoffice/modifiers/groups/${groupId}/items`, {
    method: 'PUT',
    body: JSON.stringify({ modifierIds }),
  }, token);
}

export async function setProductModifierGroups(
  token: string,
  productId: string,
  groupIds: string[],
): Promise<{ productId: string; groupIds: string[] }> {
  return request(`/api/v1/backoffice/modifiers/products/${productId}/groups`, {
    method: 'PUT',
    body: JSON.stringify({ groupIds }),
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

export async function getBackOfficePosPrinters(token: string): Promise<BackOfficePosPrinters> {
  return request<BackOfficePosPrinters>('/api/v1/backoffice/pos-printers', {}, token);
}

export async function assignPosReceiptPrinter(
  token: string,
  deviceId: string,
  printerId: string | null,
): Promise<{ id: string; receiptPrinterId: string | null; receiptPrinterName: string | null }> {
  return request(`/api/v1/backoffice/pos-printers/${deviceId}/receipt-printer`, {
    method: 'PUT',
    body: JSON.stringify({ printerId }),
  }, token);
}

export async function getBackOfficeAdjustments(
  token: string,
): Promise<BackOfficeAdjustments> {
  return request<BackOfficeAdjustments>(
    '/api/v1/backoffice/adjustments',
    {},
    token,
  );
}

export async function createAdjustmentPreset(
  token: string,
  input: UpsertAdjustmentPresetInput,
): Promise<BackOfficeAdjustmentPreset> {
  return request<BackOfficeAdjustmentPreset>(
    '/api/v1/backoffice/adjustments',
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
    token,
  );
}

export async function updateAdjustmentPreset(
  token: string,
  presetId: string,
  input: UpsertAdjustmentPresetInput,
): Promise<BackOfficeAdjustmentPreset> {
  return request<BackOfficeAdjustmentPreset>(
    `/api/v1/backoffice/adjustments/${presetId}`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    },
    token,
  );
}

export async function getBackOfficeGroups(token: string): Promise<BackOfficeGroups> {
  return request<BackOfficeGroups>('/api/v1/backoffice/groups', {}, token);
}

export async function createRestaurantGroup(token: string, name: string): Promise<{ id: string }> {
  return request('/api/v1/backoffice/groups', {
    method: 'POST',
    body: JSON.stringify({ name }),
  }, token);
}

export async function updateRestaurantGroup(
  token: string,
  id: string,
  input: { name: string; isActive: boolean },
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/groups/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function setRestaurantGroupDevices(
  token: string,
  groupId: string,
  input: { deviceIds: string[]; mainCashRegisterId: string | null },
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/groups/${groupId}/devices`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createRestaurantDepartment(
  token: string,
  groupId: string,
  input: { name: string; preparationPlaceTypeId: string | null; warehouseId: string | null; printerId: string | null; isActive?: boolean },
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/groups/${groupId}/departments`, {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateRestaurantDepartment(
  token: string,
  groupId: string,
  id: string,
  input: { name: string; preparationPlaceTypeId: string | null; warehouseId: string | null; printerId: string | null; isActive: boolean },
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/groups/${groupId}/departments/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createGroupPreparationType(token: string, name: string): Promise<{ id: string }> {
  return request('/api/v1/backoffice/groups/types', {
    method: 'POST',
    body: JSON.stringify({ name }),
  }, token);
}

export async function createGroupHall(
  token: string,
  groupId: string,
  input: { name: string; sortOrder: number; precheckPrinterId: string | null },
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/groups/${groupId}/halls`, {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateGroupHall(
  token: string,
  groupId: string,
  hallId: string,
  input: { name: string; sortOrder: number; precheckPrinterId: string | null; isActive: boolean },
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/groups/${groupId}/halls/${hallId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}


export async function getBackOfficeNomenclature(token: string): Promise<BackOfficeNomenclature> {
  return request<BackOfficeNomenclature>('/api/v1/backoffice/nomenclature', {}, token);
}

export async function createNomenclatureItem(
  token: string,
  input: UpsertNomenclatureItemInput,
): Promise<{ id: string }> {
  return request<{ id: string }>('/api/v1/backoffice/nomenclature/items', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateNomenclatureItem(
  token: string,
  itemId: string,
  input: UpsertNomenclatureItemInput,
): Promise<{ id: string }> {
  return request<{ id: string }>(`/api/v1/backoffice/nomenclature/items/${itemId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function updateNomenclatureRecipe(
  token: string,
  itemId: string,
  lines: Array<{ ingredientProductId: string; quantity: number }>,
): Promise<{ id: string; lineCount: number }> {
  return request<{ id: string; lineCount: number }>(
    `/api/v1/backoffice/nomenclature/items/${itemId}/recipe`,
    {
      method: 'PUT',
      body: JSON.stringify({ lines }),
    },
    token,
  );
}

export async function getBackOfficeInventory(token: string): Promise<BackOfficeInventory> {
  return request<BackOfficeInventory>('/api/v1/backoffice/inventory', {}, token);
}

export async function createWarehouse(
  token: string,
  input: CreateWarehouseInput,
): Promise<InventoryWarehouse> {
  return request<InventoryWarehouse>('/api/v1/backoffice/inventory/warehouses', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function updateWarehouse(
  token: string,
  warehouseId: string,
  input: UpdateWarehouseInput,
): Promise<InventoryWarehouse> {
  return request<InventoryWarehouse>(`/api/v1/backoffice/inventory/warehouses/${warehouseId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  }, token);
}

export async function createStockMovement(
  token: string,
  input: CreateStockMovementInput,
): Promise<InventoryMovement> {
  return request<InventoryMovement>('/api/v1/backoffice/inventory/movements', {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}

export async function getBackOfficeFinance(
  token: string,
  options: {
    shiftId?: string | null;
    from: string;
    to: string;
    deviceIds?: string[];
  },
): Promise<BackOfficeFinance> {
  const query = new URLSearchParams({
    from: options.from,
    to: options.to,
    take: '300',
  });

  if (options.shiftId) query.set('shiftId', options.shiftId);
  if ((options.deviceIds?.length ?? 0) > 0) {
    query.set('deviceIds', options.deviceIds!.join(','));
  }

  return request<BackOfficeFinance>(
    `/api/v1/backoffice/finance?${query.toString()}`,
    {},
    token,
  );
}

export async function addShiftCashTransaction(
  token: string,
  shiftId: string,
  input: {
    type: 'DEPOSIT' | 'WITHDRAWAL';
    amount: number;
    reason: string;
  },
): Promise<void> {
  await request(
    `/api/v1/shifts/${shiftId}/cash-transactions`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
    token,
  );
}

export async function closeShiftFromBackOffice(
  token: string,
  shiftId: string,
  input: {
    closingCash: number;
    reason: string | null;
  },
): Promise<{
  expectedCash: number;
  difference: number;
  report: FinanceShiftReport;
}> {
  return request(
    `/api/v1/shifts/${shiftId}/close`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    },
    token,
  );
}

export async function refundPayment(
  token: string,
  paymentId: string,
  input: { shiftId: string; amount: number; reason: string },
): Promise<RefundPaymentResult> {
  return request<RefundPaymentResult>(`/api/v1/payments/${paymentId}/refund`, {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
}
