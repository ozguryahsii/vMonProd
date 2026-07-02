import { apiGet } from "./api";

export interface ReportRow {
  id: number;
  name: string;
  type: string;
  target: string;
  port: number | null;
  enabled: boolean;
  keyword: string | null;
  description: string | null;
  checkCount: number;
  upCount: number;
  uptimePercent: number | null;
  avgResponseMs: number | null;
  maxResponseMs: number | null;
  outageCount: number;
  downtimeMinutes: number;
  errorCount: number;
  capacityInfo: string | null;
  health: {
    avgCpu: number | null; maxCpu: number | null;
    avgRam: number | null; maxRam: number | null;
    avgDisk: number | null; maxDisk: number | null;
  } | null;
}

export interface ReportSummary {
  from: string;
  to: string;
  services: ReportRow[];
}

export interface ReportDetail {
  service: { id: number; name: string; type: string; target: string; port: number | null };
  from: string;
  to: string;
  days: { date: string; checkCount: number; upCount: number; uptimePercent: number | null; avgResponseMs: number }[];
  outages: { startedAt: string; endedAt: string | null; firstError: string | null }[];
}

const qs = (from: string, to: string) => `from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`;

export const getReportSummary = (from: string, to: string, signal?: AbortSignal) =>
  apiGet<ReportSummary>(`/report-summary?${qs(from, to)}`, signal);

export const getReport = (id: number, from: string, to: string, signal?: AbortSignal) =>
  apiGet<ReportDetail>(`/report/${id}?${qs(from, to)}`, signal);

// yyyy-MM-dd (yerel) yardımcıları
export function isoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
export function daysAgo(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return isoDate(d);
}
