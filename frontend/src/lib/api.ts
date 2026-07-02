// vMon API istemcisi — .NET backend ile aynı origin (cookie auth otomatik gider).
// GET istekleri CSRF gerektirmez; POST'larda gerekiyorsa header eklenir.

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(`/api${path}`, {
    headers: { Accept: "application/json" },
    credentials: "same-origin",
    signal,
  });
  if (res.status === 401) throw new ApiError(401, "Oturum gerekli. Lütfen giriş yapın.");
  if (res.status === 403) throw new ApiError(403, "Bu veriye erişim yetkiniz yok.");
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json() as Promise<T>;
}

// ---- Dashboard tipleri ----
export interface DashboardData {
  kpis: { total: number; up: number; problem: number; avgMs: number | null };
  distribution: { running: number; slow: number; down: number };
  uptime24h: { t: string; uptime: number }[];
  services: {
    id: number;
    name: string;
    type: string;
    status: "up" | "slow" | "down" | "error";
    ms: number | null;
    uptime: number | null;
    lastCheckedAt: string | null;
  }[];
}

export const getDashboard = (signal?: AbortSignal) =>
  apiGet<DashboardData>("/dashboard", signal);
