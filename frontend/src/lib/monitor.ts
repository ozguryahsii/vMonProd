import { apiGet } from "./api";

export interface StatusService {
  id: number;
  name: string;
  type: string;
  target: string;
  port: number | null;
  enabled: boolean;
  lastCheckedAt: string | null;
  lastIsUp: boolean | null;
  lastResponseTimeMs: number | null;
  lastError: string | null;
  consecutiveFailures: number;
  lastCpuPercent: number | null;
  lastRamPercent: number | null;
  lastMaxDiskPercent: number | null;
  capacityInfo: string | null;
  lastStatus: number;
  description: string | null;
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

export const getStatus = (signal?: AbortSignal) => apiGet<StatusResponse>("/status", signal);
export const getBoards = (signal?: AbortSignal) => apiGet<Board[]>("/dashboards", signal);
export const getHistory = (id: number, take = 120, signal?: AbortSignal) => apiGet<HistoryData>(`/history/${id}?take=${take}`, signal);
export const getMetrics = (id: number, minutes = 1440, signal?: AbortSignal) => apiGet<MetricsData>(`/metrics/${id}?minutes=${minutes}`, signal);
