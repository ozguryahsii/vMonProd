import { apiGet, apiSend } from "./api";

export interface StatusService {
  id: number;
  name: string;
  type: string;
  target: string;
  port: number | null;
  extra: string | null;
  enabled: boolean;
  lastCheckedAt: string | null;
  lastIsUp: boolean | null;
  lastResponseTimeMs: number | null;
  lastError: string | null;
  consecutiveFailures: number;
  responseTimeThresholdMs: number | null;
  lastCpuPercent: number | null;
  lastRamPercent: number | null;
  lastMaxDiskPercent: number | null;
  capacityInfo: string | null;
  lastStatus: number;
  description: string | null;
  selfHealEnabled: boolean;
  selfHealMaxRetries: number;
  selfHealAfterFailures: number;
  lastSelfHealAt: string | null;
  lastSelfHealAttempts: number | null;
  lastSelfHealOk: boolean | null;
  lastDiskInfo: string | null;   // "mount|kullanılanGb|toplamGb|yüzde" ';' ayraçlı
  isError: boolean;
  slow: boolean;
  downSince: string | null;
}
export interface StatusResponse { now: string; services: StatusService[] }

export type Cat = "all" | "up" | "slow" | "down" | "error";

export function catOf(s: StatusService): Exclude<Cat, "all"> {
  if (s.lastIsUp === true) return s.slow ? "slow" : "up";
  if (s.isError) return "error";
  return "down";
}

export interface Board {
  id: number;
  name: string;
  sortOrder: number;
  typeFilter: string | null;
  keywordFilter: string | null;
  serviceIds: number[];
}

export interface HistoryData {
  service: { id: number; name: string; type: string };
  checks: { checkedAt: string; isUp: boolean; status: number; responseTimeMs: number; error: string | null }[];
  outages: { startedAt: string; endedAt: string | null; firstError: string | null }[];
}

export interface MetricsData {
  points: { t: string; cpu: number | null; ram: number | null; disk: number | null; diskDetail: string | null }[];
}

export interface TimeSeriesData {
  since: string;
  series: { id: number; name: string; points: { t: string; ms: number; up: boolean; st: number }[] }[];
}
export interface MetricsSeriesData {
  series: { id: number; name: string; points: { t: string; cpu: number | null; ram: number | null; disk: number | null }[] }[];
}

export const getTimeSeries = (ids: number[], minutes = 180, signal?: AbortSignal) =>
  apiGet<TimeSeriesData>(`/timeseries?ids=${ids.join(",")}&minutes=${minutes}`, signal);
export const getMetricsSeries = (ids: number[], minutes = 180, signal?: AbortSignal) =>
  apiGet<MetricsSeriesData>(`/metrics-series?ids=${ids.join(",")}&minutes=${minutes}`, signal);

export interface BoardsMeta {
  services: { id: number; name: string; type: string; keyword: string | null }[];
  types: string[];
  keywords: string[];
}
export interface BoardInput {
  name: string;
  serviceIds: number[];
  typeFilter: string | null;
  keywordFilter: string | null;
  sortOrder: number;
}

export const getStatus = (signal?: AbortSignal) => apiGet<StatusResponse>("/status", signal);
export const getBoards = (signal?: AbortSignal) => apiGet<Board[]>("/dashboards", signal);
export const getBoardsMeta = (signal?: AbortSignal) => apiGet<BoardsMeta>("/dashboards/meta", signal);
export const getHistory = (id: number, take = 120, signal?: AbortSignal, minutes = 0) =>
  apiGet<HistoryData>(`/history/${id}?take=${take}${minutes > 0 ? `&minutes=${minutes}` : ""}`, signal);

export const createBoard = (b: BoardInput) => apiSend<{ id: number }>("POST", "/dashboards", b);
export const updateBoard = (id: number, b: BoardInput) => apiSend<{ id: number }>("PUT", `/dashboards/${id}`, b);
export const deleteBoard = (id: number) => apiSend<{ ok: boolean }>("DELETE", `/dashboards/${id}`);
export const getMetrics = (id: number, minutes = 1440, signal?: AbortSignal) => apiGet<MetricsData>(`/metrics/${id}?minutes=${minutes}`, signal);

// DB İzleme detayı (metrik kutusuna tıklayınca canlı oturum/sorgu/tablespace listesi)
export interface DbDetailData {
  supported: boolean;
  title?: string;
  columns?: string[];
  rows?: string[][];
  note?: string | null;
  error?: string;
}
export const getDbDetail = (id: number, signal?: AbortSignal) => apiGet<DbDetailData>(`/db-detail/${id}`, signal);
