import { motion } from "framer-motion";
import { Server, ShieldCheck, Cpu, HardDrive, MemoryStick, Activity, AlertTriangle } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { KpiCard } from "@/components/dashboard/KpiCard";
import { DonutChart } from "@/components/charts/DonutChart";
import { FleetTrendChart } from "@/components/charts/FleetTrendChart";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { useAsync } from "@/hooks/useAsync";
import { getStats, type StatsData } from "@/lib/stats";
import { cn } from "@/lib/utils";

export function Statistics() {
  const { data, loading, error, reload } = useAsync(getStats, 30000);

  if (loading && !data) return <StatsSkeleton />;
  if (error) return <ErrorState message={error} onRetry={reload} />;
  if (!data) return null;
  if (data.counts.total === 0)
    return <EmptyState title="Sağlık verisi olan sunucu yok" hint="İstatistikler yalnız Windows/Linux Sağlık tipiyle izlenen sunuculardan gelir. Bu tiplerden servis ekleyince dolar." />;

  return (
    <div className="space-y-5">
      {(data.osEol.count > 0 || data.osEol.soonCount > 0) && (
        <div className="flex items-center gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-2.5 text-sm text-amber-400">
          <AlertTriangle className="h-4 w-4" />
          {data.osEol.count > 0 && <span>{data.osEol.count} sunucu <b>destek sonu (EOL)</b> işletim sistemi kullanıyor.</span>}
          {data.osEol.soonCount > 0 && <span className="ml-1">{data.osEol.soonCount} tanesinin desteği yakında bitiyor.</span>}
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Sağlık Sunucusu" value={String(data.counts.total)} icon={Server} accent="primary" index={0} />
        <KpiCard label="Çalışan" value={String(data.counts.up)} icon={ShieldCheck} accent="success" index={1} />
        <KpiCard label="Uptime (24s)" value={`${data.uptime.h24}%`} icon={Activity} accent="success" index={2} />
        <KpiCard label="Uptime (7g)" value={`${data.uptime.d7}%`} icon={Activity} accent="muted" index={3} />
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        <ResourceCard icon={Cpu} title="CPU" used={data.cpu.used} alloc={data.cpu.alloc} unit={data.cpu.unit} avg={data.avg.cpu} index={0} />
        <ResourceCard icon={MemoryStick} title="RAM" used={data.ram.used} alloc={data.ram.alloc} unit={data.ram.unit} avg={data.avg.ram} index={1} />
        <ResourceCard icon={HardDrive} title="Disk" used={data.disk.used} alloc={data.disk.alloc} unit={data.disk.unit} avg={data.avg.disk} index={2} />
      </div>

      {data.fleet.length > 1 && (
        <Card>
          <CardHeader><CardTitle>Filo Kaynak Trendi</CardTitle><CardDescription>Son 30 gün — ortalama CPU/RAM/Disk %</CardDescription></CardHeader>
          <CardContent><FleetTrendChart data={data.fleet} /></CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>İşletim Sistemi Dağılımı</CardTitle></CardHeader>
          <CardContent>{data.osKind.length === 0 ? <EmptyState title="Veri yok" /> : <DonutChart data={data.osKind} />}</CardContent>
        </Card>
        <Card>
          <CardHeader><CardTitle>Etiket Dağılımı</CardTitle></CardHeader>
          <CardContent>{data.tags.length === 0 ? <EmptyState title="Etiket yok" /> : <DonutChart data={data.tags} />}</CardContent>
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <TopList title="En Yüksek CPU" items={data.top.cpu} suffix="%" />
        <TopList title="En Yüksek RAM" items={data.top.ram} suffix="%" />
        <TopList title="En Yüksek Disk" items={data.top.disk} suffix="%" />
      </div>

      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0">
          <div><CardTitle>Kesinti Özeti</CardTitle><CardDescription>Son 7 gün</CardDescription></div>
          <div className="text-right text-sm">
            <span className="font-semibold tabular-nums">{data.outages.count}</span> <span className="text-muted-foreground">kesinti ·</span>{" "}
            <span className="font-semibold tabular-nums">{data.outages.minutes}</span> <span className="text-muted-foreground">dk</span>
          </div>
        </CardHeader>
        <CardContent>
          {data.outages.worst.length === 0 ? (
            <div className="py-6 text-center text-sm text-muted-foreground">Son 7 günde kesinti yok 🎉</div>
          ) : (
            <div className="space-y-2">
              {data.outages.worst.map((w) => (
                <div key={w.name} className="flex items-center justify-between rounded-lg border border-border/60 bg-muted/20 px-4 py-2 text-sm">
                  <span className="font-medium">{w.name}</span>
                  <span className="tabular-nums text-rose-400">{w.value} kesinti</span>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function ResourceCard({ icon: Icon, title, used, alloc, unit, avg, index }: {
  icon: typeof Cpu; title: string; used: number; alloc: number; unit: string; avg: number | null; index: number;
}) {
  const pct = alloc > 0 ? Math.min(100, Math.round((used / alloc) * 100)) : 0;
  const color = pct >= 85 ? "bg-rose-500" : pct >= 65 ? "bg-amber-500" : "bg-emerald-500";
  return (
    <motion.div initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.45, delay: index * 0.06, ease: [0.16, 1, 0.3, 1] }}>
      <Card className="p-5">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground"><Icon className="h-4 w-4" /> {title}</div>
          {avg != null && <span className="text-xs text-muted-foreground">ort. %{avg}</span>}
        </div>
        <div className="mt-2 flex items-baseline gap-1.5">
          <span className="text-2xl font-bold tabular-nums">{used}</span>
          <span className="text-sm text-muted-foreground">/ {alloc} {unit}</span>
        </div>
        <div className="mt-3 h-2 overflow-hidden rounded-full bg-muted">
          <motion.div className={cn("h-full rounded-full", color)} initial={{ width: 0 }} animate={{ width: `${pct}%` }} transition={{ duration: 0.8, delay: 0.3, ease: "easeOut" }} />
        </div>
        <div className="mt-1 text-right text-xs text-muted-foreground tabular-nums">%{pct} kullanımda</div>
      </Card>
    </motion.div>
  );
}

function TopList({ title, items, suffix }: { title: string; items: { name: string; value: number }[]; suffix?: string }) {
  const max = Math.max(1, ...items.map((i) => i.value));
  return (
    <Card>
      <CardHeader><CardTitle className="text-base">{title}</CardTitle></CardHeader>
      <CardContent className="space-y-2.5">
        {items.length === 0 ? <div className="py-4 text-center text-sm text-muted-foreground">Veri yok</div> : items.map((i) => (
          <div key={i.name}>
            <div className="mb-1 flex items-center justify-between text-sm">
              <span className="truncate pr-2">{i.name}</span>
              <span className="shrink-0 tabular-nums text-muted-foreground">{i.value}{suffix}</span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted">
              <div className="h-full rounded-full bg-primary/70" style={{ width: `${(i.value / max) * 100}%` }} />
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function StatsSkeleton() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => <Card key={i} className="p-5"><Skeleton className="h-3 w-24" /><Skeleton className="mt-3 h-8 w-20" /></Card>)}
      </div>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => <Card key={i} className="p-5"><Skeleton className="h-3 w-16" /><Skeleton className="mt-3 h-7 w-28" /><Skeleton className="mt-3 h-2 w-full" /></Card>)}
      </div>
      <Card className="p-5"><Skeleton className="h-[280px] w-full" /></Card>
    </div>
  );
}
