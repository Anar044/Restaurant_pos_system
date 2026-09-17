import { API_BASE_URL } from './api';

export type DiscoveredWindowsPrinter = {
  id: string;
  queueName: string;
  isDefault: boolean;
  lastSeenAt: string | null;
  isOnline: boolean;
  isConfigured: boolean;
};

export type ConfiguredPosPrinter = {
  id: string;
  name: string;
  connectionType: 'WindowsQueue' | 'Network' | string;
  address: string;
  port: number | null;
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
  discoveredWindowsPrinters: DiscoveredWindowsPrinter[];
  configuredPrinters: ConfiguredPosPrinter[];
};

export type BackOfficePosPrinters = {
  posDevices: PosPrinterDevice[];
};

export type ConfigurePosPrinterInput = {
  name: string;
  connectionType: 'WindowsQueue' | 'Network';
  queueName: string | null;
  address: string | null;
  port: number | null;
};

async function readError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { message?: string; title?: string };
    return body.message ?? body.title ?? `HTTP ${response.status}`;
  } catch {
    return `HTTP ${response.status}`;
  }
}

async function request<T>(path: string, options: RequestInit, token: string): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set('Authorization', `Bearer ${token}`);
  if (options.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const response = await fetch(`${API_BASE_URL}${path}`, { ...options, headers });
  if (!response.ok) throw new Error(await readError(response));
  return (await response.json()) as T;
}

export async function getBackOfficePosPrinters(token: string): Promise<BackOfficePosPrinters> {
  return request<BackOfficePosPrinters>('/api/v1/backoffice/pos-printers', {}, token);
}

export async function configurePosPrinter(
  token: string,
  deviceId: string,
  input: ConfigurePosPrinterInput,
): Promise<{ id: string }> {
  return request(`/api/v1/backoffice/pos-printers/${deviceId}/printers`, {
    method: 'POST',
    body: JSON.stringify(input),
  }, token);
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
