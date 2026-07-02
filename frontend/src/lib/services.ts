import { apiGet, apiSend } from "./api";

export interface ServiceItem {
  id: number;
  name: string;
  type: string;
  target: string;
  port: number | null;
  extra: string | null;
  useSsl: boolean;
  ignoreCertErrors: boolean;
  credentialId: number | null;
  credentialName: string | null;
  enabled: boolean;
  intervalMinutesOverride: number | null;
  responseTimeThresholdMs: number | null;
  timeoutSeconds: number;
  cpuThresholdPercent: number | null;
  ramThresholdPercent: number | null;
  diskThresholdPercent: number | null;
  keyword: string | null;
  description: string | null;
  alertMail: boolean;
  alertSms: boolean;
  alertWhatsapp: boolean;
  alertCall: boolean;
  lastCheckedAt: string | null;
  lastIsUp: boolean | null;
  lastStatus: number;
  lastResponseTimeMs: number | null;
  lastError: string | null;
  slow: boolean;
}

export type ServiceStatus = "up" | "slow" | "down" | "error";

export function statusOf(s: ServiceItem): ServiceStatus {
  if (s.lastStatus === 2) return "error";
  if (s.lastIsUp !== true) return "down";
  if (s.slow) return "slow";
  return "up";
}

export interface ServiceInput {
  name: string;
  type: string;
  target: string;
  port: number | null;
  extra: string | null;
  useSsl: boolean;
  ignoreCertErrors: boolean;
  credentialId: number | null;
  enabled: boolean;
  intervalMinutesOverride: number | null;
  responseTimeThresholdMs: number | null;
  timeoutSeconds: number;
  cpuThresholdPercent: number | null;
  ramThresholdPercent: number | null;
  diskThresholdPercent: number | null;
  keyword: string | null;
  description: string | null;
  alertMail: boolean;
  alertSms: boolean;
  alertWhatsapp: boolean;
  alertCall: boolean;
}

export interface ServicesMeta {
  types: string[];
  credentials: { id: number; name: string }[];
}

export const CONTROL_TYPES = ["WindowsServiceControl", "LinuxServiceControl"];

export const listServices = (signal?: AbortSignal) => apiGet<ServiceItem[]>("/services", signal);
export const servicesMeta = (signal?: AbortSignal) => apiGet<ServicesMeta>("/services/meta", signal);
export const createService = (input: ServiceInput) => apiSend<{ id: number }>("POST", "/services", input);
export const updateService = (id: number, input: ServiceInput) => apiSend<{ id: number }>("PUT", `/services/${id}`, input);
export const deleteService = (id: number) => apiSend<{ ok: boolean }>("DELETE", `/services/${id}`);
export const checkService = (id: number) => apiSend<{ isUp: boolean; responseTimeMs: number; error: string | null }>("POST", `/check/${id}`);
// ---- Toplu işlemler + CSV ----
export interface BulkEditInput {
  ids: number[];
  alertMail: string | null;      // "on" | "off" | null (dokunma)
  alertSms: string | null;
  alertWhatsapp: string | null;
  alertCall: string | null;
  enabled: string | null;
  setInterval: boolean; interval: number | null;
  setSlow: boolean; slow: number | null;
  setCpu: boolean; cpu: number | null;
  setRam: boolean; ram: number | null;
  setDisk: boolean; disk: number | null;
  addKeywords: string | null;
}

export const bulkDelete = (ids: number[]) => apiSend<{ deleted: number }>("POST", "/services/bulk-delete", { ids });
export const bulkEdit = (input: BulkEditInput) => apiSend<{ updated: number; changes: string[] }>("POST", "/services/bulk-edit", input);

export const exportCsvUrl = "/api/services/export";
export const sampleCsvUrl = "/Services/SampleCsv";

/** CSV içe aktarım — multipart + CSRF header. */
export async function importCsv(file: File): Promise<{ added: number; skipped: number; errors: string[] }> {
  const token = await formCsrf();
  const fd = new FormData();
  fd.append("csvFile", file);
  const res = await fetch("/api/services/import", {
    method: "POST", credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
    body: fd,
  });
  if (!res.ok) throw new ApiError(res.status, (await res.text()) || `Sunucu hatası (${res.status})`);
  return res.json();
}

/** Görünen servisleri toplu kontrol (eski 'Görünenleri Kontrol Et'). */
export const checkIds = (ids: number[]) =>
  sendForm<{ id: number; isUp: boolean; responseTimeMs?: number; error: string | null }[]>(
    "/check-ids", new URLSearchParams({ ids: ids.join(",") }));

export const serviceAction = (id: number, action: "start" | "stop" | "restart") => {
  const body = new URLSearchParams({ action });
  // service-action [FromForm] bekliyor → form-encoded gönderiyoruz (apiSend JSON; burada özel fetch)
  return sendForm<{ ok: boolean; message: string }>(`/service-action/${id}`, body);
};

// service-action [FromForm] olduğu için form-encoded gönderen küçük yardımcı (CSRF dahil)
import { ApiError } from "./api";
let _csrf: string | null = null;
async function formCsrf(): Promise<string> {
  if (_csrf) return _csrf;
  const r = await fetch("/api/antiforgery", { credentials: "same-origin" });
  _csrf = (await r.json()).token as string;
  return _csrf;
}
async function sendForm<T>(path: string, body: URLSearchParams): Promise<T> {
  const token = await formCsrf();
  const res = await fetch(`/api${path}`, {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token, "Content-Type": "application/x-www-form-urlencoded" },
    body,
  });
  if (res.status === 401) throw new ApiError(401, "Oturum gerekli.");
  if (res.status === 403) throw new ApiError(403, "Yetkiniz yok.");
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json() as Promise<T>;
}
