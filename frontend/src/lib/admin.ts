import { apiGet, apiSend } from "./api";

// ---- Denetim ----
export interface AuditRow {
  id: number; at: string; user: string; ip: string | null;
  action: string; target: string | null; detail: string | null; success: boolean;
}
export const getAudit = (q: string, act: string, days: number, take = 500, signal?: AbortSignal, from = "", to = "") =>
  apiGet<{ rows: AuditRow[]; actions: string[] }>(
    `/audit?q=${encodeURIComponent(q)}&act=${encodeURIComponent(act)}&days=${days}&take=${take}` +
    (from ? `&from=${from}` : "") + (to ? `&to=${to}` : ""), signal);
export const verifyAudit = () => apiSend<{ ok: boolean; message: string }>("POST", "/audit/verify");

// ---- Kullanıcılar ----
export interface UserRow {
  id: number; sam: string; displayName: string | null; email: string | null; phone: string | null;
  permissionsCsv: string; isActive: boolean; isLocal: boolean; lastLogin: string | null;
}
export interface UsersData {
  users: UserRow[];
  adminUsers: string;
  allPerms: { key: string; label: string }[];
}
export const getUsers = (signal?: AbortSignal) => apiGet<UsersData>("/users", signal);
export const updateUser = (id: number, perms: string[], phone: string | null, email: string | null) =>
  apiSend<{ ok: boolean }>("PUT", `/users/${id}`, { perms, phone, email });
export const deleteUser = (id: number) => apiSend<{ ok: boolean }>("DELETE", `/users/${id}`);
export const syncUsers = () => apiSend<{ ok: boolean; message: string }>("POST", "/users/sync");

// ---- Kimlik Bilgileri ----
export interface CredentialRow {
  id: number; name: string; username: string; domain: string | null; description: string | null;
  sourceType: string; vaultUrl: string | null; vaultKey: string | null; vaultUserKey: string | null;
  hasPassword: boolean; hasToken: boolean;
}
export interface CredentialInput {
  name: string; username: string; domain: string | null; description: string | null;
  sourceType: string; newPassword: string | null;
  vaultUrl: string | null; newVaultToken: string | null; vaultKey: string | null; vaultUserKey: string | null;
}
export const getCredentials = (signal?: AbortSignal) => apiGet<CredentialRow[]>("/credentials", signal);
export const createCredential = (c: CredentialInput) => apiSend<{ id: number }>("POST", "/credentials", c);
export const updateCredential = (id: number, c: CredentialInput) => apiSend<{ id: number }>("PUT", `/credentials/${id}`, c);
export const deleteCredential = (id: number) => apiSend<{ ok: boolean }>("DELETE", `/credentials/${id}`);
export const vaultTest = (id: number) => apiSend<{ ok: boolean; message: string }>("POST", `/vault-test/${id}`);
