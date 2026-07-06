import { useEffect, useState } from "react";
import {
  LineChart, Line, AreaChart, Area, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend,
} from "recharts";
import { RefreshCw, Play, Square, RotateCw } from "lucide-react";
import { Drawer } from "@/components/ui/drawer";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { type StatusService, catOf, getHistory, getMetrics, getDbDetail, type HistoryData, type MetricsData, type DbDetailData } from "@/lib/monitor";
import { CONTROL_TYPES, DB_METRIC_META, fmtDbValue, hasDbDetail, checkService, serviceAction } from "@/lib/services";
import { cn } from "@/lib/utils";

const badge: Record<string, { label: string; cls: string }> = {
  up: { label: "Çalışıyor", cls: "bg-emerald-500/15 text-emerald-400" },
  slow: { label: "Yavaş", cls: "bg-amber-500/15 text-amber-400" },
  down: { label: "Kapalı", cls: "bg-rose-500/15 text-rose-400" },
  error: { label: "Hata", cls: "bg-orange-500/15 text-orange-400" },
};
const clock = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
const dt = (iso: string) => new Date(iso).toLocaleString();

/** Kesinti süresi: "48 dk" / "2 sa 15 dk" / "3 gün 4 sa" (bitiş yoksa şu ana kadar). */
function duration(startIso: string, endIso: string | null): string {
  const ms = (endIso ? new Date(endIso).getTime() : Date.now()) - new Date(startIso).getTime();
  const mins = Math.max(1, Math.round(ms / 60000));
  if (mins < 60) return `${mins} dk`;
  const h = Math.floor(mins / 60);
  if (h < 24) return `${h} sa ${mins % 60} dk`;
  return `${Math.floor(h / 24)} gün ${h % 24} sa`;
}
const tip = { background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" };

export function ServiceDetailDrawer({ service, onClose, onChanged }: {
  service: StatusService | null;
  onClose: () => void;
  onChanged: () => void;
}) {
  const [hist, setHist] = useState<HistoryData | null>(null);
  const [metrics, setMetrics] = useState<MetricsData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState<string | null>(null);
  const [pendingAction, setPendingAction] = useState<"start" | "stop" | "restart" | null>(null);
  const [rangeMin, setRangeMin] = useState(180); // 3s varsayılan; 24s/7g/1a seçilebilir
  const [dbDetail, setDbDetail] = useState<DbDetailData | null>(null);
  const [dbLoading, setDbLoading] = useState(false);

  const RANGES = [
    { m: 180, label: "3 saat" }, { m: 1440, label: "24 saat" },
    { m: 10080, label: "7 gün" }, { m: 43200, label: "1 ay" },
  ];

  const ACTION_LABEL: Record<string, string> = { start: "BAŞLAT", stop: "DURDUR", restart: "YENİDEN BAŞLAT" };

  const isHealth = service?.type === "WindowsHealth" || service?.type === "LinuxHealth";
  const isControl = service ? CONTROL_TYPES.includes(service.type) : false;
  // DB metrik izlemesi: grafikte ms değil adet/%/sn gösterilir
  const dbMeta = service ? DB_METRIC_META[service.type] ?? null : null;
  const showDbDetail = service ? hasDbDetail(service.type) : false;

  // DB detay listesi CANLI çekilir; DB'yi yormamak için yalnız açılışta + elle yenilemede (oto-poll YOK)
  const loadDbDetail = () => {
    if (!service || !hasDbDetail(service.type)) { setDbDetail(null); return; }
    setDbLoading(true);
    getDbDetail(service.id)
      .then(setDbDetail)
      .catch((e) => setDbDetail({ supported: true, error: (e as Error).message }))
      .finally(() => setDbLoading(false));
  };
  useEffect(() => { setDbDetail(null); loadDbDetail(); /* eslint-disable-next-line */ }, [service?.id]);

  const load = () => {
    if (!service) return;
    const ctrl = new AbortController();
    setLoading(true); setError(null);
    Promise.all([
      getHistory(service.id, 120, ctrl.signal, rangeMin),
      isHealth ? getMetrics(service.id, rangeMin, ctrl.signal) : Promise.resolve(null),
    ])
      .then(([h, m]) => { setHist(h); setMetrics(m); })
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); })
      .finally(() => setLoading(false));
    return () => ctrl.abort();
  };
  useEffect(() => { setFlash(null); return load(); /* eslint-disable-next-line */ }, [service?.id, service?.lastCheckedAt, rangeMin]);
  useEffect(() => { if (service) { setHist(null); setMetrics(null); } /* eslint-disable-next-line */ }, [service?.id]);

  async function act(fn: () => Promise<{ message?: string }>, label: string) {
    setBusy(true); setFlash(null);
    try { const r = await fn(); setFlash(r.message ? `${label}: ${r.message}` : `${label} tamam`); load(); onChanged(); }
    catch (e) { setFlash((e as Error).message); }
    finally { setBusy(false); }
  }

  // Uzun aralıkta eksende tarih de göster
  const label = (iso: string) => {
    const d = new Date(iso);
    const hm = `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
    return rangeMin > 1440 ? `${String(d.getDate()).padStart(2, "0")}.${String(d.getMonth() + 1).padStart(2, "0")} ${hm}` : hm;
  };
  const thr = service?.responseTimeThresholdMs ?? null;
  const checks = (hist?.checks ?? []).slice().reverse().map((c) => ({
    t: label(c.checkedAt), ms: c.responseTimeMs, up: c.isUp, st: c.status,
    slow: c.isUp && thr != null && c.responseTimeMs > thr,
  }));
  const mpts = (metrics?.points ?? []).map((p) => ({ t: label(p.t), cpu: p.cpu, ram: p.ram, disk: p.disk }));
  const cat = service ? catOf(service) : "down";

  // Durum noktaları: DOWN=büyük kırmızı, Yavaş/Hata=koyu sarı (grafikte kaybolmaz)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const statusDot = (props: any) => {
    const { cx, cy, payload, index } = props;
    // ÖNCE hata/yavaş (st=2 eşik aşımı up=false gelse bile HATA'dır → koyu sarı); sonra gerçek down → kırmızı
    if (payload?.st === 2 || payload?.slow)
      return <circle key={index} cx={cx} cy={cy} r={5} fill="hsl(38 92% 45%)" stroke="hsl(var(--card))" strokeWidth={1.5} />;
    if (payload?.up === false)
      return <circle key={index} cx={cx} cy={cy} r={6} fill="hsl(0 84% 55%)" stroke="hsl(var(--card))" strokeWidth={1.5} />;
    return <g key={index} />;
  };

  return (
    <Drawer open={!!service} onClose={onClose}
      title={service?.name ?? ""}
      description={service ? `${service.type} · ${service.target}${service.port ? `:${service.port}` : ""}` : ""}>
      {service && (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center gap-2">
            <span className={cn("inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold", badge[cat].cls)}>{badge[cat].label}</span>
            {service.lastResponseTimeMs != null && service.lastIsUp && <span className="text-sm text-muted-foreground">{fmtDbValue(service.type, service.lastResponseTimeMs)}</span>}
            {service.lastCheckedAt && <span className="text-xs text-muted-foreground">son: {dt(service.lastCheckedAt)}</span>}
            <div className="ml-auto flex gap-1">
              <Button variant="outline" size="sm" disabled={busy} onClick={() => act(() => checkService(service.id), "Kontrol")}>
                <RefreshCw className={cn("h-4 w-4", busy && "animate-spin")} /> Kontrol Et
              </Button>
              {isControl && (
                <>
                  <Button variant="ghost" size="icon" className="h-9 w-9 text-emerald-400" title="Başlat" disabled={busy} onClick={() => setPendingAction("start")}><Play className="h-4 w-4" /></Button>
                  <Button variant="ghost" size="icon" className="h-9 w-9 text-rose-400" title="Durdur" disabled={busy} onClick={() => setPendingAction("stop")}><Square className="h-4 w-4" /></Button>
                  <Button variant="ghost" size="icon" className="h-9 w-9 text-amber-400" title="Yeniden başlat" disabled={busy} onClick={() => setPendingAction("restart")}><RotateCw className="h-4 w-4" /></Button>
                </>
              )}
            </div>
          </div>

          {service.capacityInfo && (
            <div className="rounded-lg border border-border/60 bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
              <span className="font-semibold text-foreground">Atanan kaynaklar: </span>{service.capacityInfo}
            </div>
          )}
          {flash && <div className="rounded-lg border border-border/60 bg-muted/30 px-3 py-2 text-sm">{flash}</div>}
          {service.lastError && cat !== "up" && <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{service.lastError}</div>}

          {loading && !hist ? (
            <div className="space-y-3"><Skeleton className="h-[220px] w-full" /></div>
          ) : error ? (
            <ErrorState message={error} onRetry={load} />
          ) : (
            <>
              <div>
                <div className="mb-2 flex items-center justify-between">
                  <h3 className="text-sm font-semibold">{dbMeta ? `${dbMeta.short} (${checks.length} kontrol)` : `Yanıt Süresi (${checks.length} kontrol)`}</h3>
                  <div className="flex gap-1">
                    {RANGES.map((r) => (
                      <Button key={r.m} variant={rangeMin === r.m ? "default" : "ghost"} size="sm"
                        className="h-7 px-2 text-xs" onClick={() => setRangeMin(r.m)}>{r.label}</Button>
                    ))}
                  </div>
                </div>
                {checks.length === 0 ? <EmptyState title="Bu aralıkta kontrol yok" /> : (
                  <ResponsiveContainer width="100%" height={200}>
                    <AreaChart data={checks} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                      <defs><linearGradient id="gMs" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="hsl(217 91% 60%)" stopOpacity={0.4} /><stop offset="100%" stopColor="hsl(217 91% 60%)" stopOpacity={0} /></linearGradient></defs>
                      <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
                      <XAxis dataKey="t" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={34} />
                      <YAxis tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
                      <Tooltip cursor={{ stroke: "hsl(var(--border))" }} contentStyle={tip}
                        formatter={(v: number, _n, item) => {
                          // eslint-disable-next-line @typescript-eslint/no-explicit-any
                          const p = (item as any)?.payload;
                          const state = p?.st === 2 ? " · HATA" : p?.slow ? " · YAVAŞ" : p?.up === false ? " · DOWN" : "";
                          return [`${fmtDbValue(service.type, v)}${state}`, dbMeta ? dbMeta.short : "Yanıt"];
                        }} />
                      <Area type="monotone" dataKey="ms" stroke="hsl(217 91% 60%)" strokeWidth={2} fill="url(#gMs)"
                        dot={statusDot} activeDot={{ r: 4 }} isAnimationActive={checks.length < 400} />
                    </AreaChart>
                  </ResponsiveContainer>
                )}
                <div className="mt-1 flex gap-4 text-[10px] text-muted-foreground">
                  <span className="flex items-center gap-1"><span className="inline-block h-3 w-3 rounded-full" style={{ background: "hsl(0 84% 55%)" }} /> Down</span>
                  <span className="flex items-center gap-1"><span className="inline-block h-2.5 w-2.5 rounded-full" style={{ background: "hsl(38 92% 45%)" }} /> Yavaş / Hata</span>
                </div>
              </div>

              {showDbDetail && (
                <div>
                  <div className="mb-2 flex items-center justify-between">
                    <h3 className="text-sm font-semibold">
                      {dbDetail?.title ?? "Detay"}
                      {dbDetail?.rows && <span className="ml-1.5 text-xs font-normal text-muted-foreground">({dbDetail.rows.length})</span>}
                    </h3>
                    <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={dbLoading} onClick={loadDbDetail}>
                      <RefreshCw className={cn("h-3.5 w-3.5", dbLoading && "animate-spin")} /> Yenile
                    </Button>
                  </div>
                  {dbLoading && !dbDetail ? (
                    <Skeleton className="h-24 w-full" />
                  ) : dbDetail?.error ? (
                    <div className="rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive">{dbDetail.error}</div>
                  ) : !dbDetail?.rows || dbDetail.rows.length === 0 ? (
                    <div className="rounded-lg border border-border/60 bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">Kayıt yok 🎉</div>
                  ) : (
                    <div className="overflow-x-auto rounded-lg border border-border/60">
                      <table className="w-full text-left text-xs">
                        <thead className="bg-muted/40 text-muted-foreground">
                          <tr>{dbDetail.columns!.map((c) => <th key={c} className="whitespace-nowrap px-2.5 py-1.5 font-medium">{c}</th>)}</tr>
                        </thead>
                        <tbody>
                          {dbDetail.rows.map((row, ri) => (
                            <tr key={ri} className="border-t border-border/40">
                              {row.map((cell, ci) => (
                                <td key={ci} className={cn("px-2.5 py-1.5 align-top tabular-nums", ci === row.length - 1 && dbDetail.columns![ci]?.includes("Sorgu") && "max-w-[240px] truncate font-mono")}
                                  title={cell}>{cell || "—"}</td>
                              ))}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                  {dbDetail?.note && <div className="mt-1 text-[10px] text-muted-foreground">{dbDetail.note}</div>}
                </div>
              )}

              {isHealth && mpts.length > 0 && (
                <div>
                  <h3 className="mb-2 text-sm font-semibold">Kaynak Kullanımı (24s)</h3>
                  <ResponsiveContainer width="100%" height={200}>
                    <LineChart data={mpts} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
                      <XAxis dataKey="t" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={30} />
                      <YAxis domain={[0, 100]} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
                      <Tooltip cursor={{ stroke: "hsl(var(--border))" }} contentStyle={tip} />
                      <Legend iconType="circle" formatter={(v) => <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 12 }}>{v}</span>} />
                      <Line type="monotone" dataKey="cpu" name="CPU" stroke="hsl(358 85% 55%)" strokeWidth={2} dot={false} />
                      <Line type="monotone" dataKey="ram" name="RAM" stroke="hsl(217 91% 60%)" strokeWidth={2} dot={false} />
                      <Line type="monotone" dataKey="disk" name="Disk" stroke="hsl(38 92% 55%)" strokeWidth={2} dot={false} />
                    </LineChart>
                  </ResponsiveContainer>
                </div>
              )}

              <div>
                <h3 className="mb-2 text-sm font-semibold">Kesintiler ({hist?.outages.length ?? 0})</h3>
                {(hist?.outages.length ?? 0) === 0 ? (
                  <div className="rounded-lg border border-border/60 bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">Kesinti kaydı yok 🎉</div>
                ) : (
                  <div className="space-y-2">
                    {hist!.outages.map((o, i) => (
                      <div key={i} className="rounded-lg border border-border/60 bg-muted/20 p-3 text-sm">
                        {/* Sol: başlangıç (kırmızı) · Orta: süre (gri) · Sağ: bitiş (pastel yeşil) / sürüyor (kırmızı) */}
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-medium text-rose-400">{dt(o.startedAt)}</span>
                          <span className="text-xs text-muted-foreground">{duration(o.startedAt, o.endedAt)}</span>
                          {o.endedAt
                            ? <span className="font-medium text-emerald-300/80">{dt(o.endedAt)}</span>
                            : <span className="animate-pulse font-medium text-rose-400">sürüyor</span>}
                        </div>
                        {o.firstError && <div className="mt-1 text-xs text-muted-foreground">{o.firstError}</div>}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          )}
        </div>
      )}
      <ConfirmDialog
        open={!!pendingAction}
        title={pendingAction ? `Servisi ${ACTION_LABEL[pendingAction].toLowerCase()}` : ""}
        message={service && pendingAction ? `"${service.name}" servisine ${ACTION_LABEL[pendingAction]} komutu gönderilecek. Emin misiniz?` : ""}
        confirmLabel={pendingAction ? ACTION_LABEL[pendingAction] : "Onayla"}
        danger={pendingAction === "stop"}
        loading={busy}
        onConfirm={() => {
          if (!service || !pendingAction) return;
          const a = pendingAction;
          setPendingAction(null);
          act(() => serviceAction(service.id, a), ACTION_LABEL[a]);
        }}
        onCancel={() => setPendingAction(null)}
      />
    </Drawer>
  );
}
