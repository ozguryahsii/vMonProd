// vMon API istemcisi — .NET backend ile aynı origin (cookie auth otomatik gider).
// GET istekleri CSRF gerektirmez; POST'larda gerekiyorsa header eklenir.

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function handle<T>(res: Response): Promise<T> {
  if (res.status === 401) {
    // Oturum düşmüş → login'e, dönüş adresiyle (SPA derin linki korunur)
    const ret = encodeURIComponent(window.location.pathname + window.location.search);
    window.location.href = `/Account/Login?returnUrl=${ret}`;
    throw new ApiError(401, "Oturum gerekli. Giriş sayfasına yönlendiriliyorsunuz…");
  }
  if (res.status === 403) throw new ApiError(403, "Bu işlem için yetkiniz yok.");
  if (!res.ok) {
    let msg = `Sunucu hatası (${res.status})`;
    try { const t = await res.text(); if (t) msg = t; } catch { /* yoksay */ }
    throw new ApiError(res.status, msg);
  }
  const txt = await res.text();
  return (txt ? JSON.parse(txt) : null) as T;
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const res = await fetch(`/api${path}`, {
    headers: { Accept: "application/json" },
    credentials: "same-origin",
    signal,
  });
  return handle<T>(res);
}

// CSRF token'ı bir kez çekilir, bellekte tutulur (global AutoValidateAntiforgery için).
let _csrf: string | null = null;
async function csrfToken(): Promise<string> {
  if (_csrf) return _csrf;
  const r = await fetch("/api/antiforgery", { credentials: "same-origin" });
  const d = await r.json();
  _csrf = d.token as string;
  return _csrf;
}

/** Form-encoded POST ([FromForm] bekleyen mevcut uçlar için) — CSRF dahil. */
export async function apiForm<T>(path: string, params: Record<string, string>): Promise<T> {
  const token = await csrfToken();
  const res = await fetch(`/api${path}`, {
    method: "POST",
    credentials: "same-origin",
    headers: { Accept: "application/json", "X-CSRF-TOKEN": token, "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams(params),
  });
  return handle<T>(res);
}

export async function apiSend<T>(method: "POST" | "PUT" | "DELETE", path: string, body?: unknown, _retried = false): Promise<T> {
  const token = await csrfToken();
  const res = await fetch(`/api${path}`, {
    method,
    credentials: "same-origin",
    headers: {
      Accept: "application/json",
      "X-CSRF-TOKEN": token,
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  // 400 hem CSRF hem doğrulama hatası olabilir → token'ı yenileyip YALNIZ BİR KEZ tekrar dene
  if (res.status === 400 && !_retried) {
    _csrf = null;
    return apiSend<T>(method, path, body, true);
  }
  return handle<T>(res);
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
