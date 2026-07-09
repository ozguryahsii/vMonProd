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
  critical: { diskFull: number; breach: number; cpuHot: number; ramHot: number };
  osEol: {
    real: boolean; count: number; soonCount: number;
    items: { name: string; value: number; status: string; eol: string | null; days: number | null }[];
  };
  uptime: { h24: number; d7: number };
  outages: { count: number; minutes: number; daily: { day: string; value: number }[]; worst: NameValue[] };
  fleet: { day: string; cpu: number; ram: number; disk: number }[];
  capacity: { day: string; cpuUsed: number; cpuAlloc: number; ramUsed: number; ramAlloc: number; diskUsed: number; diskAlloc: number }[];
  histogram: { cpu: number[]; ram: number[]; disk: number[] };
  rising: {
    cpu: { name: string; from: number; to: number; delta: number }[];
    ram: { name: string; from: number; to: number; delta: number }[];
    disk: { name: string; from: number; to: number; delta: number }[];
  };
  heatmap: { rows: string[]; data: [number, number, number][] };
  diskForecast: { name: string; current: number; perDay: number; daysLeft: number; date: string }[];
  certExpiry: { name: string; target: string; daysLeft: number | null; status: number; error: string | null; lastChecked: string | null }[];
  dbHealth: {
    counts: { total: number; ok: number; warn: number; err: number; down: number };
    items: {
      id: number; name: string; target: string; port: number | null; extra: string | null; type: string;
      value: number | null; status: number; slow: boolean;
      lastError: string | null; lastChecked: string | null;
    }[];
  };
}

// ---- Widget düzeni (düzenlenebilir pano) ----
export interface StatWidgetDef {
  id: number;
  type: string;      // counter | resource | pie | gauge | fleet | top | critical | uptime | histogram | capacity | rising | outage | os_eol | heatmap
  source: string;
  title: string | null;
  configJson: string | null;
  x: number; y: number; w: number; h: number;
}

async function csrf(): Promise<string> {
  const r = await fetch("/api/antiforgery", { credentials: "same-origin" });
  return (await r.json()).token as string;
}

export async function getStatWidgets(signal?: AbortSignal): Promise<{ canEdit: boolean; widgets: StatWidgetDef[] }> {
  const res = await fetch("/api/stat-widgets", { headers: { Accept: "application/json" }, credentials: "same-origin", signal });
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json();
}

/** Düzeni kaydet — klasik /Statistics/SaveLayout (admin). */
export async function saveStatLayout(widgets: StatWidgetDef[]): Promise<void> {
  const token = await csrf();
  const body = widgets.map((w) => ({
    id: w.id, type: w.type, source: w.source, title: w.title,
    configJson: w.configJson, x: w.x, y: w.y, w: w.w, h: w.h,
  }));
  const res = await fetch("/Statistics/SaveLayout", {
    method: "POST", credentials: "same-origin",
    headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new ApiError(res.status, res.status === 403 ? "Düzeni yalnız admin kaydedebilir." : `Kaydedilemedi (${res.status})`);
}

// ---- Drill-down (eski /Statistics/Detail — tıklanabilir widget'lar) ----
export interface StatDetailServer {
  name: string; target: string; os: string; status: string;
  cpu: number | null; ram: number | null; disk: number | null;
  capacity: string | null; lastChecked: string | null;
}
export interface StatDetail {
  count: number;
  /** "db" = DB izlemelerinin kendi veri serileri; "cert" = sertifika kalan-gün bilgisi; yoksa sunucu tablosu */
  kind?: "db" | "cert";
  servers: StatDetailServer[];
  trend: { metric: string; days: number; points: { day: string; value: number }[] } | null;
  series?: {
    name: string; type: string; status: number; error: string | null;
    value: number | null; lastChecked: string | null;
    points: { day: string; value: number }[];
  }[];
  certInfo?: {
    name: string; target: string; daysLeft: number | null;
    status: number; error: string | null; lastChecked: string | null;
  }[];
}

export async function getStatDetail(source: string, value?: string | null, days = 7, signal?: AbortSignal): Promise<StatDetail> {
  const qs = new URLSearchParams({ source, days: String(days) });
  if (value) qs.set("value", value);
  const res = await fetch(`/Statistics/Detail?${qs}`, { headers: { Accept: "application/json" }, credentials: "same-origin", signal });
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json();
}

/** Varsayılan düzene dön (tüm widget'ları siler, sonraki yüklemede tohumlanır). */
export async function resetStatLayout(): Promise<void> {
  const token = await csrf();
  const res = await fetch("/Statistics/ResetLayout", {
    method: "POST", credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token },
  });
  if (!res.ok) throw new ApiError(res.status, `Sıfırlanamadı (${res.status})`);
}

// /Statistics/Data MVC controller'ında (/, /api değil) — SPA aynı origin, cookie ile.
export async function getStats(signal?: AbortSignal): Promise<StatsData> {
  const res = await fetch("/Statistics/Data", { headers: { Accept: "application/json" }, credentials: "same-origin", signal });
  if (res.status === 401) throw new ApiError(401, "Oturum gerekli. Lütfen giriş yapın.");
  if (res.status === 403) throw new ApiError(403, "İstatistikleri görme yetkiniz yok.");
  if (!res.ok) throw new ApiError(res.status, `Sunucu hatası (${res.status})`);
  return res.json() as Promise<StatsData>;
}
