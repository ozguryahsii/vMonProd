import { useCallback, useEffect, useMemo, useState } from "react";
import { Activity, TrendingDown, Clock, AlertOctagon, ChevronRight, Download, Search } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Select } from "@/components/ui/input";
import { Drawer } from "@/components/ui/drawer";
import { KpiCard } from "@/components/dashboard/KpiCard";
import { UptimeChart } from "@/components/charts/UptimeChart";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import {
  type ReportSummary, type ReportDetail, type ReportRow,
  getReportSummary, getReport, isoDate, daysAgo,
} from "@/lib/reports";
import { cn } from "@/lib/utils";

const shortDate = (iso: string) => {
  const d = new Date(iso);
  return `${String(d.getDate()).padStart(2, "0")}.${String(d.getMonth() + 1).padStart(2, "0")}`;
};
const fmtDateTime = (iso: string) => new Date(iso).toLocaleString();

function uptimeColor(v: number | null) {
  if (v == null) return "text-muted-foreground";
  if (v >= 99.5) return "text-emerald-400";
  if (v >= 98) return "text-amber-400";
  return "text-rose-400";
}

export function Reports() {
  const [from, setFrom] = useState(daysAgo(7));
  const [to, setTo] = useState(isoDate(new Date()));
  const [data, setData] = useState<ReportSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [detail, setDetail] = useState<ReportRow | null>(null);
  const [q, setQ] = useState("");
  const [typeF, setTypeF] = useState("");
  const [tagF, setTagF] = useState("");

  const load = useCallback(() => {
    const ctrl = new AbortController();
    setLoading(true); setError(null);
    getReportSummary(from, to, ctrl.signal)
      .then(setData)
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); })
      .finally(() => setLoading(false));
    return () => ctrl.abort();
  }, [from, to]);

  useEffect(() => load(), [load]);

  const preset = (n: number) => { setFrom(daysAgo(n)); setTo(isoDate(new Date())); };
  const thisMonth = () => {
    const now = new Date();
    setFrom(isoDate(new Date(now.getFullYear(), now.getMonth(), 1)));
    setTo(isoDate(now));
  };

  // Filtre seçenekleri + filtrelenmiş satırlar
  const types = useMemo(() => Array.from(new Set((data?.services ?? []).map((s) => s.type))).sort(), [data]);
  const tags = useMemo(() => {
    const set = new Set<string>();
    for (const s of data?.services ?? [])
      for (const k of (s.keyword ?? "").split(",").map((x) => x.trim()).filter(Boolean)) set.add(k);
    return Array.from(set).sort((a, b) => a.localeCompare(b, "tr"));
  }, [data]);

  const rows = useMemo(() => {
    let list = data?.services ?? [];
    if (typeF) list = list.filter((s) => s.type === typeF);
    if (tagF) list = list.filter((s) => (s.keyword ?? "").split(",").map((x) => x.trim()).some((k) => k.localeCompare(tagF, "tr", { sensitivity: "accent" }) === 0));
    if (q) { const t = q.toLowerCase(); list = list.filter((s) => s.name.toLowerCase().includes(t) || s.target.toLowerCase().includes(t)); }
    return list;
  }, [data, q, typeF, tagF]);

  function exportCsv() {
    const esc = (v: unknown) => `"${String(v ?? "").replace(/"/g, '""')}"`;
    const lines = [
      "Servis;Tip;Hedef;Uptime %;Kontrol;Basarili;Ort. Yanit ms;Maks. Yanit ms;Kesinti Sayisi;Kesinti dk;Esik Asimi;Etiket",
      ...rows.map((s) => [
        esc(s.name), s.type, esc(s.target + (s.port ? `:${s.port}` : "")),
        s.uptimePercent ?? "", s.checkCount, s.upCount,
        s.avgResponseMs ?? "", s.maxResponseMs ?? "",
        s.outageCount, s.downtimeMinutes, s.errorCount, esc(s.keyword),
      ].join(";")),
    ];
    const blob = new Blob(["﻿" + lines.join("\r\n")], { type: "text/csv;charset=utf-8" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = `vmon-rapor_${from}_${to}.csv`;
    a.click();
    URL.revokeObjectURL(a.href);
  }

  const kpis = useMemo(() => {
    const s = rows;
    const withUp = s.filter((x) => x.uptimePercent != null);
    const avgUptime = withUp.length ? withUp.reduce((a, b) => a + (b.uptimePercent ?? 0), 0) / withUp.length : null;
    const totalDowntime = s.reduce((a, b) => a + b.downtimeMinutes, 0);
    const totalOutages = s.reduce((a, b) => a + b.outageCount, 0);
    const worst = withUp.length ? withUp.reduce((a, b) => ((a.uptimePercent ?? 100) <= (b.uptimePercent ?? 100) ? a : b)) : null;
    return { avgUptime, totalDowntime, totalOutages, worst };
  }, [rows]);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <label className="mb-1.5 block text-xs font-medium text-muted-foreground">Başlangıç</label>
          <Input type="date" value={from} max={to} onChange={(e) => setFrom(e.target.value)} className="w-auto" />
        </div>
        <div>
          <label className="mb-1.5 block text-xs font-medium text-muted-foreground">Bitiş</label>
          <Input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} className="w-auto" />
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => preset(7)}>Son 7 gün</Button>
          <Button variant="outline" size="sm" onClick={() => preset(30)}>Son 30 gün</Button>
          <Button variant="outline" size="sm" onClick={thisMonth}>Bu ay</Button>
        </div>
      </div>

      {loading && !data ? (
        <ReportsSkeleton />
      ) : error ? (
        <ErrorState message={error} onRetry={load} />
      ) : !data || data.services.length === 0 ? (
        <EmptyState title="Bu aralıkta veri yok" hint="Farklı bir tarih aralığı dene veya servis ekle." />
      ) : (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <KpiCard label="Ortalama Uptime" value={kpis.avgUptime != null ? `${kpis.avgUptime.toFixed(2)}%` : "—"} icon={Activity} accent="success" index={0} />
            <KpiCard label="Toplam Kesinti" value={`${Math.round(kpis.totalDowntime)} dk`} icon={Clock} accent="warning" index={1} />
            <KpiCard label="Kesinti Sayısı" value={String(kpis.totalOutages)} icon={AlertOctagon} accent="primary" index={2} />
            <KpiCard label="En Düşük Uptime" value={kpis.worst?.uptimePercent != null ? `${kpis.worst.uptimePercent.toFixed(1)}%` : "—"} icon={TrendingDown} accent="muted" index={3} />
          </div>

          <Card>
            <CardHeader className="flex-row items-center justify-between space-y-0">
              <div>
                <CardTitle>Servis Erişilebilirlik Raporu</CardTitle>
                <CardDescription>{shortDate(data.from)} – {shortDate(data.to)} · {rows.length} servis · satıra tıklayarak detay</CardDescription>
              </div>
              <Button variant="outline" size="sm" onClick={exportCsv} disabled={rows.length === 0}>
                <Download className="h-4 w-4" /> CSV
              </Button>
            </CardHeader>
            <CardContent className="px-5 pb-3">
              <div className="flex flex-wrap gap-3">
                <div className="relative min-w-[200px] flex-1">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                  <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Servis veya hedef ara…" className="h-9 pl-9" />
                </div>
                <Select value={typeF} onChange={(e) => setTypeF(e.target.value)} className="h-9 w-auto">
                  <option value="">Tüm tipler</option>
                  {types.map((t) => <option key={t} value={t}>{t}</option>)}
                </Select>
                <Select value={tagF} onChange={(e) => setTagF(e.target.value)} className="h-9 w-auto">
                  <option value="">Tüm etiketler</option>
                  {tags.map((t) => <option key={t} value={t}>{t}</option>)}
                </Select>
              </div>
            </CardContent>
            <CardContent className="px-0">
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                      <th className="px-5 py-3 font-semibold">Servis</th>
                      <th className="px-5 py-3 font-semibold">Uptime</th>
                      <th className="px-5 py-3 font-semibold">Ort. Yanıt</th>
                      <th className="px-5 py-3 font-semibold">Maks. Yanıt</th>
                      <th className="px-5 py-3 font-semibold">Kesinti</th>
                      <th className="px-5 py-3 font-semibold">Süre</th>
                      <th className="px-5 py-3" />
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((s) => (
                      <tr key={s.id} className="cursor-pointer border-b border-border/60 transition-colors hover:bg-accent/40" onClick={() => setDetail(s)}>
                        <td className="px-5 py-3">
                          <div className="font-medium">{s.name}</div>
                          <div className="text-xs text-muted-foreground">{s.type}</div>
                        </td>
                        <td className={cn("px-5 py-3 font-semibold tabular-nums", uptimeColor(s.uptimePercent))}>
                          {s.uptimePercent != null ? `${s.uptimePercent.toFixed(2)}%` : "—"}
                        </td>
                        <td className="px-5 py-3 tabular-nums text-muted-foreground">{s.avgResponseMs != null ? `${s.avgResponseMs} ms` : "—"}</td>
                        <td className="px-5 py-3 tabular-nums text-muted-foreground">{s.maxResponseMs != null ? `${s.maxResponseMs} ms` : "—"}</td>
                        <td className="px-5 py-3 tabular-nums">{s.outageCount}</td>
                        <td className="px-5 py-3 tabular-nums text-muted-foreground">{s.downtimeMinutes > 0 ? `${Math.round(s.downtimeMinutes)} dk` : "—"}</td>
                        <td className="px-5 py-3 text-right"><ChevronRight className="ml-auto h-4 w-4 text-muted-foreground" /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </CardContent>
          </Card>
        </>
      )}

      <ReportDetailDrawer row={detail} from={from} to={to} onClose={() => setDetail(null)} />
    </div>
  );
}

function ReportDetailDrawer({ row, from, to, onClose }: { row: ReportRow | null; from: string; to: string; onClose: () => void }) {
  const [detail, setDetail] = useState<ReportDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!row) { setDetail(null); return; }
    const ctrl = new AbortController();
    setLoading(true); setError(null); setDetail(null);
    getReport(row.id, from, to, ctrl.signal)
      .then(setDetail)
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); })
      .finally(() => setLoading(false));
    return () => ctrl.abort();
  }, [row, from, to]);

  const chartData = (detail?.days ?? []).map((d) => ({ t: d.date, uptime: d.uptimePercent ?? 0 }));

  return (
    <Drawer open={!!row} onClose={onClose} title={row?.name ?? ""} description={row ? `${row.type} · ${row.target}${row.port ? `:${row.port}` : ""}` : ""}>
      {loading ? (
        <div className="space-y-3"><Skeleton className="h-[240px] w-full" /><Skeleton className="h-24 w-full" /></div>
      ) : error ? (
        <ErrorState message={error} />
      ) : detail ? (
        <div className="space-y-6">
          <div className="grid grid-cols-3 gap-3">
            <MiniStat label="Uptime" value={row?.uptimePercent != null ? `${row.uptimePercent.toFixed(2)}%` : "—"} />
            <MiniStat label="Kesinti" value={String(row?.outageCount ?? 0)} />
            <MiniStat label="Süre" value={row ? `${Math.round(row.downtimeMinutes)} dk` : "—"} />
          </div>

          {row?.health && (
            <div className="grid grid-cols-3 gap-3">
              <MiniStat label="Ort. CPU" value={row.health.avgCpu != null ? `${row.health.avgCpu}%` : "—"} />
              <MiniStat label="Ort. RAM" value={row.health.avgRam != null ? `${row.health.avgRam}%` : "—"} />
              <MiniStat label="Ort. Disk" value={row.health.avgDisk != null ? `${row.health.avgDisk}%` : "—"} />
            </div>
          )}

          <div>
            <h3 className="mb-2 text-sm font-semibold">Günlük Erişilebilirlik</h3>
            {chartData.length === 0 ? <EmptyState title="Veri yok" /> : (
              <>
                {/* Eski uptime şeridi: her gün bir hücre, renk = uptime bandı */}
                <div className="mb-3 flex flex-wrap gap-1">
                  {detail.days.map((d) => (
                    <div key={d.date}
                      title={`${d.date} — ${d.uptimePercent != null ? `%${d.uptimePercent}` : "veri yok"} (${d.checkCount} kontrol, ort. ${d.avgResponseMs} ms)`}
                      className={cn(
                        "h-7 w-4 cursor-default rounded transition-transform hover:scale-y-110",
                        d.uptimePercent == null ? "bg-muted" :
                        d.uptimePercent >= 99.5 ? "bg-emerald-500" :
                        d.uptimePercent >= 98 ? "bg-amber-500" : "bg-rose-500"
                      )} />
                  ))}
                </div>
                <div className="mb-3 flex items-center gap-3 text-[11px] text-muted-foreground">
                  <span className="flex items-center gap-1"><span className="h-2.5 w-2.5 rounded-sm bg-emerald-500" /> ≥99.5</span>
                  <span className="flex items-center gap-1"><span className="h-2.5 w-2.5 rounded-sm bg-amber-500" /> ≥98</span>
                  <span className="flex items-center gap-1"><span className="h-2.5 w-2.5 rounded-sm bg-rose-500" /> &lt;98</span>
                  <span className="flex items-center gap-1"><span className="h-2.5 w-2.5 rounded-sm bg-muted" /> veri yok</span>
                </div>
                <UptimeChart data={chartData} xFormat={shortDate} />
              </>
            )}
          </div>

          <div>
            <h3 className="mb-2 text-sm font-semibold">Kesintiler ({detail.outages.length})</h3>
            {detail.outages.length === 0 ? (
              <div className="rounded-lg border border-border/60 bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">Bu aralıkta kesinti yok 🎉</div>
            ) : (
              <div className="space-y-2">
                {detail.outages.map((o, i) => (
                  <div key={i} className="rounded-lg border border-border/60 bg-muted/20 p-3 text-sm">
                    <div className="flex items-center justify-between">
                      <span className="font-medium text-rose-400">{fmtDateTime(o.startedAt)}</span>
                      <span className="text-xs text-muted-foreground">{o.endedAt ? fmtDateTime(o.endedAt) : "sürüyor"}</span>
                    </div>
                    {o.firstError && <div className="mt-1 text-xs text-muted-foreground">{o.firstError}</div>}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      ) : null}
    </Drawer>
  );
}

function MiniStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/60 bg-muted/20 p-3 text-center">
      <div className="text-lg font-bold tabular-nums">{value}</div>
      <div className="text-xs text-muted-foreground">{label}</div>
    </div>
  );
}

function ReportsSkeleton() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Card key={i} className="p-5"><Skeleton className="h-3 w-24" /><Skeleton className="mt-3 h-8 w-20" /></Card>
        ))}
      </div>
      <Card className="p-5 space-y-3">{Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-10 w-full" />)}</Card>
    </div>
  );
}
