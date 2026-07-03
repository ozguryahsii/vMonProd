import { useEffect, useState } from "react";
import {
  LineChart, Line, AreaChart, Area, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend,
} from "recharts";
import { RefreshCw, Play, Square, RotateCw } from "lucide-react";
import { Drawer } from "@/components/ui/drawer";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { type StatusService, catOf, getHistory, getMetrics, type HistoryData, type MetricsData } from "@/lib/monitor";
import { CONTROL_TYPES, checkService, serviceAction } from "@/lib/services";
import { cn } from "@/lib/utils";

const badge: Record<string, { label: string; cls: string }> = {
  up: { label: "Çalışıyor", cls: "bg-emerald-500/15 text-emerald-400" },
  slow: { label: "Yavaş", cls: "bg-amber-500/15 text-amber-400" },
  down: { label: "Kapalı", cls: "bg-rose-500/15 text-rose-400" },
  error: { label: "Hata", cls: "bg-orange-500/15 text-orange-400" },
};
const clock = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
const dt = (iso: string) => new Date(iso).toLocaleString();
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

  const ACTION_LABEL: Record<string, string> = { start: "BAŞLAT", stop: "DURDUR", restart: "YENİDEN BAŞLAT" };

  const isHealth = service?.type === "WindowsHealth" || service?.type === "LinuxHealth";
  const isControl = service ? CONTROL_TYPES.includes(service.type) : false;

  const load = () => {
    if (!service) return;
    const ctrl = new AbortController();
    setLoading(true); setError(null);
    Promise.all([
      getHistory(service.id, 120, ctrl.signal),
      isHealth ? getMetrics(service.id, 1440, ctrl.signal) : Promise.resolve(null),
    ])
      .then(([h, m]) => { setHist(h); setMetrics(m); })
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); })
      .finally(() => setLoading(false));
    return () => ctrl.abort();
  };
  useEffect(() => { setHist(null); setMetrics(null); setFlash(null); return load(); /* eslint-disable-next-line */ }, [service]);

  async function act(fn: () => Promise<{ message?: string }>, label: string) {
    setBusy(true); setFlash(null);
    try { const r = await fn(); setFlash(r.message ? `${label}: ${r.message}` : `${label} tamam`); load(); onChanged(); }
    catch (e) { setFlash((e as Error).message); }
    finally { setBusy(false); }
  }

  const checks = (hist?.checks ?? []).slice().reverse().map((c) => ({ t: clock(c.checkedAt), ms: c.responseTimeMs, up: c.isUp }));
  const mpts = (metrics?.points ?? []).map((p) => ({ t: clock(p.t), cpu: p.cpu, ram: p.ram, disk: p.disk }));
  const cat = service ? catOf(service) : "down";

  return (
    <Drawer open={!!service} onClose={onClose}
      title={service?.name ?? ""}
      description={service ? `${service.type} · ${service.target}${service.port ? `:${service.port}` : ""}` : ""}>
      {service && (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center gap-2">
            <span className={cn("inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold", badge[cat].cls)}>{badge[cat].label}</span>
            {service.lastResponseTimeMs != null && service.lastIsUp && <span className="text-sm text-muted-foreground">{service.lastResponseTimeMs} ms</span>}
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
                <h3 className="mb-2 text-sm font-semibold">Yanıt Süresi (son {checks.length} kontrol)</h3>
                {checks.length === 0 ? <EmptyState title="Kontrol geçmişi yok" /> : (
                  <ResponsiveContainer width="100%" height={200}>
                    <AreaChart data={checks} margin={{ top: 8, right: 8, left: -20, bottom: 0 }}>
                      <defs><linearGradient id="gMs" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="hsl(217 91% 60%)" stopOpacity={0.4} /><stop offset="100%" stopColor="hsl(217 91% 60%)" stopOpacity={0} /></linearGradient></defs>
                      <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
                      <XAxis dataKey="t" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={30} />
                      <YAxis tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
                      <Tooltip cursor={{ stroke: "hsl(var(--border))" }} contentStyle={tip} formatter={(v: number) => [`${v} ms`, "Yanıt"]} />
                      <Area type="monotone" dataKey="ms" stroke="hsl(217 91% 60%)" strokeWidth={2} fill="url(#gMs)" />
                    </AreaChart>
                  </ResponsiveContainer>
                )}
              </div>

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
                        <div className="flex items-center justify-between">
                          <span className="font-medium text-rose-400">{dt(o.startedAt)}</span>
                          <span className="text-xs text-muted-foreground">{o.endedAt ? dt(o.endedAt) : "sürüyor"}</span>
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
