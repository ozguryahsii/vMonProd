import { apiGet, apiSend, ApiError } from "./api";

export interface ChannelRow {
  id: number; name: string; kind: string; recipients: string | null; templateSid: string | null;
  method: string; url: string; contentType: string; body: string | null; headers: string | null;
  authType: string; username: string; sender: string; successContains: string | null;
  enabled: boolean; hasPassword: boolean; hasApiKey: boolean;
}
export interface ChannelInput {
  name: string; kind: string; recipients: string | null; templateSid: string | null;
  method: string; url: string; contentType: string; body: string | null; headers: string | null;
  authType: string; username: string; sender: string; successContains: string | null;
  enabled: boolean; newPassword: string | null; newApiKey: string | null;
}

export const KIND_LABELS: Record<string, string> = { Sms: "SMS", Whatsapp: "WhatsApp", Ivr: "Sesli/IVR" };

export const getChannels = (signal?: AbortSignal) => apiGet<ChannelRow[]>("/channels", signal);
export const createChannel = (c: ChannelInput) => apiSend<{ id: number }>("POST", "/channels", c);
export const updateChannel = (id: number, c: ChannelInput) => apiSend<{ id: number }>("PUT", `/channels/${id}`, c);
export const deleteChannel = (id: number) => apiSend<{ ok: boolean }>("DELETE", `/channels/${id}`);
export const toggleChannel = (id: number) => apiSend<{ enabled: boolean }>("POST", `/channels/${id}/toggle`);
export const testChannel = (id: number, to: string) => apiSend<{ ok: boolean; message: string }>("POST", `/channels/${id}/test`, { to });

// ---- Yedekler ----
export interface BackupsData {
  isSqlite: boolean;
  path: string;
  files: { name: string; sizeMb: number; modifiedUtc: string }[];
}
export const getBackups = (signal?: AbortSignal) => apiGet<BackupsData>("/backups", signal);
export const backupNow = () => apiSend<{ ok: boolean; message: string }>("POST", "/backups/now");
export const backupDelete = (file: string) => apiSend<{ ok: boolean }>("POST", "/backups/delete", { file });
export const backupRestore = (file: string) => apiSend<{ ok: boolean; message: string }>("POST", "/backups/restore", { file });
export const backupDownloadUrl = (file: string) => `/Settings/DownloadBackup?file=${encodeURIComponent(file)}`;

// ---- Logo ----
async function csrf(): Promise<string> {
  const r = await fetch("/api/antiforgery", { credentials: "same-origin" });
  return (await r.json()).token as string;
}
export async function uploadLogo(file: File): Promise<{ ok: boolean; file: string }> {
  const token = await csrf();
  const fd = new FormData();
  fd.append("logoFile", file);
  const res = await fetch("/api/logo", { method: "POST", credentials: "same-origin", headers: { "X-CSRF-TOKEN": token }, body: fd });
  if (!res.ok) throw new ApiError(res.status, (await res.text()) || `Sunucu hatası (${res.status})`);
  return res.json();
}
export const removeLogo = () => apiSend<{ ok: boolean }>("DELETE", "/logo");
