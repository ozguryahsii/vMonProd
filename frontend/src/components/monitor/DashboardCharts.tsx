import { useEffect, useState } from "react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Select } from "@/components/ui/input";
import { MultiLineChart, type SeriesDef } from "@/components/charts/MultiLineChart";
import { Skeleton } from "@/components/ui/states";
import { type StatusService, getTimeSeries, getMetricsSeries } from "@/lib/monitor";

const HEALTH_TYPES = ["WindowsHealth", "LinuxHealth"];

/** Eski dashboard'daki canlı grafikler: Yanıt Süreleri + CPU/RAM/Disk (panodaki servislerden). */
export function DashboardCharts({ services }: { services: StatusService[] }) {
  const [minutes, setMinutes] = useState(180);
  const [resp, setResp] = useState<SeriesDef[] | null>(null);
  const [cpu, setCpu] = useState<SeriesDef[] | null>(null);
  const [ram, setRam] = useState<SeriesDef[] | null>(null);
  const [disk, setDisk] = useState<SeriesDef[] | null>(null);

  const idsKey = services.map((s) => s.id).join(",");

  useEffect(() => {
    const ctrl = new AbortController();
    const enabled = services.filter((s) => s.enabled);
    // Yanıt süresi: en yavaş 12 servis (görsel okunabilirlik)
    const respIds = enabled
      .slice().sort((a, b) => (b.lastResponseTimeMs ?? 0) - (a.lastResponseTimeMs ?? 0))
      .slice(0, 12).map((s) => s.id);
    const healthIds = enabled.filter((s) => HEALTH_TYPES.includes(s.type)).slice(0, 20).map((s) => s.id);

    setResp(null); setCpu(null); setRam(null); setDisk(null);

    if (respIds.length > 0)
      getTimeSeries(respIds, minutes, ctrl.signal).then((d) =>
        setResp(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.up ? p.ms : null })) })))
      ).catch(() => setResp([]));
    else setResp([]);

    if (healthIds.length > 0)
      getMetricsSeries(healthIds, minutes, ctrl.signal).then((d) => {
        setCpu(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.cpu })) })));
        setRam(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.ram })) })));
        setDisk(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.disk })) })));
      }).catch(() => { setCpu([]); setRam([]); setDisk([]); });
    else { setCpu([]); setRam([]); setDisk([]); }

    return () => ctrl.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [idsKey, minutes]);

  const hasResp = resp === null || resp.length > 0;
  const hasHealth = cpu === null || (cpu?.length ?? 0) > 0;
  if (!hasResp && !hasHealth) return null;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end">
        <Select value={String(minutes)} onChange={(e) => setMinutes(Number(e.target.value))} className="w-auto">
          <option value="60">Son 1 saat</option>
          <option value="180">Son 3 saat</option>
          <option value="720">Son 12 saat</option>
          <option value="1440">Son 24 saat</option>
        </Select>
      </div>

      {hasResp && (
        <Card>
          <CardHeader>
            <CardTitle>Yanıt Süreleri</CardTitle>
            <CardDescription>En yavaş 12 servis (ms) — kapalı anlar boş bırakılır</CardDescription>
          </CardHeader>
          <CardContent>
            {resp === null ? <Skeleton className="h-[260px] w-full" /> : <MultiLineChart series={resp} unit=" ms" />}
          </CardContent>
        </Card>
      )}

      {hasHealth && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          <MetricCard title="CPU Kullanımı" data={cpu} />
          <MetricCard title="RAM Kullanımı" data={ram} />
          <MetricCard title="Disk Doluluk" data={disk} />
        </div>
      )}
    </div>
  );
}

function MetricCard({ title, data }: { title: string; data: SeriesDef[] | null }) {
  return (
    <Card>
      <CardHeader className="pb-2"><CardTitle className="text-base">{title}</CardTitle></CardHeader>
      <CardContent>
        {data === null ? <Skeleton className="h-[200px] w-full" /> :
          data.length === 0 ? <div className="py-8 text-center text-sm text-muted-foreground">Sağlık verisi yok</div> :
          <MultiLineChart series={data} height={200} unit="%" domainMax={100} />}
      </CardContent>
    </Card>
  );
}
