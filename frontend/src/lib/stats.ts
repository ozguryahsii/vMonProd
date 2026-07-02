import { ApiError } from "./api";

export interface NameValue { name: string; value: number }

export interface StatsData {
  lastUpdated: string;
  counts: { total: number; up: number; down: number; error: number };
  cpu: { used: number; alloc: number; unit: string };
  ram: { used: number; alloc: number; unit: string };
  disk: { used: number; alloc: number; unit: string };
  avg: { cpu: number | null; ram: number | null; disk: number | null };
  osKind: NameValue[];
  osVersion: NameValue[];
  tags: NameValue[];
  top: {
    cpu: { name: string; value: number; os: string | null }[];
    ram: { name: string; value: number; os: string | null }[];
    disk: { name: string; value: number; os: string | null }[];
  };
  critical: { diskFull: number; breach: number };
  osEol: {
    real: boolean; count: number; soonCount: number;
    items: { name: string; value: number; status: string; eol: string | null; days: number | null }[];
  };
  uptime: { h24: number; d7: number };
  outages: { count: number; minutes: number; daily: { day: string; value: number }[]; worst: NameValue[] };
  fleet: { day: string; cpu: number; ram: number; disk: number }[];
  capacity: { day: string; cpuUsed: number; cpuAlloc: number; ramUsed: number; ramAlloc: number; diskUsed: number; diskAlloc: number }[];
}

// /Statistics/Data MVC controller'ında (/, /api değil) — SPA aynı origin, cookie ile.
export async function getStats(signal?: AbortSignal): Promise<StatsData> {
  const res = await fetch("/Statistics/Data", { headers: { Accept: "application/json" }, credentials: "same-origin", signal });
  if (res.status === 401) throw new ApiError(401, "Oturum gerekli. Lütfen giriş yapın.");
  if (res.status === 403) throw new ApiError(403, "İstatistikleri görme yetkiniz yok.");
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json() as Promise<StatsData>;
}
