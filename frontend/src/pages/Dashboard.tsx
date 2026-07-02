import { motion } from "framer-motion";
import { Server, ShieldCheck, AlertTriangle, Timer, MoreHorizontal, CircleDot } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { KpiCard } from "@/components/dashboard/KpiCard";
import { UptimeChart } from "@/components/charts/UptimeChart";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { getDashboard } from "@/lib/api";
import { useAsync } from "@/hooks/useAsync";
import { cn } from "@/lib/utils";

const statusMap: Record<string, { dot: string; label: string; badge: string }> = {
  up: { dot: "bg-emerald-500", label: "Çalışıyor", badge: "bg-emerald-500/15 text-emerald-400" },
  slow: { dot: "bg-amber-500", label: "Yavaş", badge: "bg-amber-500/15 text-amber-400" },
  down: { dot: "bg-rose-500", label: "Kapalı", badge: "bg-rose-500/15 text-rose-400" },
  error: { dot: "bg-orange-500", label: "Hata", badge: "bg-orange-500/15 text-orange-400" },
};

export function Dashboard() {
  // 30 sn'de bir otomatik yenile (canlı izleme hissi)
  const { data, loading, error, reload } = useAsync(getDashboard, 30000);

  if (loading && !data) return <DashboardSkeleton />;
  if (error) return <ErrorState message={error} onRetry={reload} />;
  if (!data) return null;

  const { kpis, distribution, uptime24h, services } = data;
  const dist = [
    { label: "Çalışıyor", val: distribution.running, color: "bg-emerald-500" },
    { label: "Yavaş", val: distribution.slow, color: "bg-amber-500" },
    { label: "Kapalı", val: distribution.down, color: "bg-rose-500" },
  ];
  const distTotal = Math.max(1, distribution.running + distribution.slow + distribution.down);

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard label="Toplam Servis" value={String(kpis.total)} icon={Server} accent="primary" index={0} />
        <KpiCard label="Çalışan" value={String(kpis.up)} icon={ShieldCheck} accent="success" index={1} />
        <KpiCard label="Sorunlu" value={String(kpis.problem)} icon={AlertTriangle} accent="warning" index={2} />
        <KpiCard label="Ort. Yanıt" value={kpis.avgMs != null ? `${kpis.avgMs} ms` : "—"} icon={Timer} accent="muted" index={3} />
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <motion.div className="lg:col-span-2" initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.5, delay: 0.25, ease: [0.16, 1, 0.3, 1] }}>
          <Card>
            <CardHeader className="flex-row items-center justify-between space-y-0">
              <div>
                <CardTitle>Erişilebilirlik — Son 24 Saat</CardTitle>
                <CardDescription>Tüm servislerin ortalama uptime yüzdesi</CardDescription>
              </div>
            </CardHeader>
            <CardContent>
              {uptime24h.length === 0
                ? <EmptyState title="Henüz veri yok" hint="Servisler kontrol edildikçe grafik dolacak." />
                : <UptimeChart data={uptime24h} />}
            </CardContent>
          </Card>
        </motion.div>

        <motion.div initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.5, delay: 0.32, ease: [0.16, 1, 0.3, 1] }}>
          <Card className="h-full">
            <CardHeader>
              <CardTitle>Durum Dağılımı</CardTitle>
              <CardDescription>Anlık servis durumu</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              {dist.map((s) => (
                <div key={s.label}>
                  <div className="mb-1.5 flex items-center justify-between text-sm">
                    <span className="flex items-center gap-2 text-muted-foreground">
                      <CircleDot className="h-3.5 w-3.5" /> {s.label}
                    </span>
                    <span className="font-semibold tabular-nums">{s.val}</span>
                  </div>
                  <div className="h-2 overflow-hidden rounded-full bg-muted">
                    <motion.div
                      className={cn("h-full rounded-full", s.color)}
                      initial={{ width: 0 }}
                      animate={{ width: `${(s.val / distTotal) * 100}%` }}
                      transition={{ duration: 0.8, delay: 0.4, ease: "easeOut" }}
                    />
                  </div>
                </div>
              ))}
            </CardContent>
          </Card>
        </motion.div>
      </div>

      <motion.div initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.5, delay: 0.4, ease: [0.16, 1, 0.3, 1] }}>
        <Card>
          <CardHeader className="flex-row items-center justify-between space-y-0">
            <div>
              <CardTitle>Servisler</CardTitle>
              <CardDescription>En son kontrol sonuçları</CardDescription>
            </div>
            <Button variant="outline" size="sm">Tümünü Gör</Button>
          </CardHeader>
          <CardContent className="px-0">
            {services.length === 0 ? (
              <EmptyState title="Henüz servis yok" hint="Servisler ekranından ilk servisini ekle." />
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                      <th className="px-5 py-3 font-semibold">Servis</th>
                      <th className="px-5 py-3 font-semibold">Tip</th>
                      <th className="px-5 py-3 font-semibold">Durum</th>
                      <th className="px-5 py-3 font-semibold">Yanıt</th>
                      <th className="px-5 py-3 font-semibold">Uptime (24s)</th>
                      <th className="px-5 py-3" />
                    </tr>
                  </thead>
                  <tbody>
                    {services.map((s) => {
                      const st = statusMap[s.status] ?? statusMap.down;
                      return (
                        <tr key={s.id} className="border-b border-border/60 transition-colors hover:bg-accent/40">
                          <td className="px-5 py-3 font-medium">{s.name}</td>
                          <td className="px-5 py-3 text-muted-foreground">{s.type}</td>
                          <td className="px-5 py-3">
                            <span className={cn("inline-flex items-center gap-1.5 rounded-md px-2 py-0.5 text-xs font-semibold", st.badge)}>
                              <span className={cn("h-1.5 w-1.5 rounded-full", st.dot)} /> {st.label}
                            </span>
                          </td>
                          <td className="px-5 py-3 tabular-nums text-muted-foreground">{s.ms != null ? `${s.ms} ms` : "—"}</td>
                          <td className="px-5 py-3 tabular-nums">{s.uptime != null ? `${s.uptime}%` : "—"}</td>
                          <td className="px-5 py-3 text-right">
                            <Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      </motion.div>
    </div>
  );
}

function DashboardSkeleton() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Card key={i} className="p-5">
            <Skeleton className="h-3 w-24" />
            <Skeleton className="mt-3 h-8 w-20" />
            <Skeleton className="mt-3 h-3 w-28" />
          </Card>
        ))}
      </div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <Card className="lg:col-span-2 p-5"><Skeleton className="h-[280px] w-full" /></Card>
        <Card className="p-5 space-y-4">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-8 w-full" />)}
        </Card>
      </div>
      <Card className="p-5 space-y-3">
        {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-9 w-full" />)}
      </Card>
    </div>
  );
}
