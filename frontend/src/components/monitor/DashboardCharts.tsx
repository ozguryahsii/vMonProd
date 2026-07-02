import { useEffect, useRef, useState } from "react";
import { Filter, ChevronDown } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Select } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { MultiLineChart, type SeriesDef } from "@/components/charts/MultiLineChart";
import { Skeleton } from "@/components/ui/states";
import { type StatusService, getTimeSeries, getMetricsSeries } from "@/lib/monitor";
import { cn } from "@/lib/utils";

const HEALTH_TYPES = ["WindowsHealth", "LinuxHealth"];

/** Eski dashboard'daki canlı grafikler: Yanıt Süreleri + CPU/RAM/Disk (panodaki servislerden). */
export function DashboardCharts({ services }: { services: StatusService[] }) {
  const [minutes, setMinutes] = useState(180);
  const [resp, setResp] = useState<SeriesDef[] | null>(null);
  const [cpu, setCpu] = useState<SeriesDef[] | null>(null);
  const [ram, setRam] = useState<SeriesDef[] | null>(null);
  const [disk, setDisk] = useState<SeriesDef[] | null>(null);
  const [manualIds, setManualIds] = useState<Set<number>>(new Set()); // boş = otomatik (en yavaş 12)

  const idsKey = services.map((s) => s.id).join(",");
  const manualKey = Array.from(manualIds).sort((a, b) => a - b).join(",");

  useEffect(() => {
    const ctrl = new AbortController();
    const enabled = services.filter((s) => s.enabled);
    // Yanıt süresi: elle seçim varsa o; yoksa en yavaş 12 servis (görsel okunabilirlik)
    const respIds = manualIds.size > 0
      ? enabled.filter((s) => manualIds.has(s.id)).slice(0, 20).map((s) => s.id)
      : enabled
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
  }, [idsKey, minutes, manualKey]);

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
          <CardHeader className="flex-row items-center justify-between space-y-0">
            <div>
              <CardTitle>Yanıt Süreleri</CardTitle>
              <CardDescription>{manualIds.size > 0 ? `${manualIds.size} seçili servis` : "En yavaş 12 servis"} (ms) — kapalı anlar boş bırakılır</CardDescription>
            </div>
            <ServicePicker services={services.filter((s) => s.enabled)} selected={manualIds} onChange={setManualIds} />
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

/** "Servis Seç" — grafiğe girecek servisleri elle seçme (boş = otomatik en yavaş 12). */
function ServicePicker({ services, selected, onChange }: {
  services: StatusService[];
  selected: Set<number>;
  onChange: (s: Set<number>) => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onDoc = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, []);

  const toggle = (id: number) => {
    const n = new Set(selected);
    if (n.has(id)) n.delete(id); else n.add(id);
    onChange(n);
  };

  return (
    <div className="relative" ref={ref}>
      <Button variant="outline" size="sm" onClick={() => setOpen((o) => !o)}>
        <Filter className="h-3.5 w-3.5" /> Servis Seç
        {selected.size > 0 && <span className="rounded bg-primary/20 px-1 text-[10px] font-bold text-primary">{selected.size}</span>}
        <ChevronDown className="h-3 w-3 opacity-60" />
      </Button>
      {open && (
        <div className="absolute right-0 top-full z-30 mt-1 max-h-80 w-72 overflow-y-auto rounded-xl border border-border bg-card p-2 shadow-2xl">
          <button className="mb-1 w-full rounded-md px-2 py-1.5 text-left text-xs text-muted-foreground transition hover:bg-accent/60"
            onClick={() => onChange(new Set())}>
            ⟲ Otomatik (en yavaş 12)
          </button>
          {services.map((s) => (
            <label key={s.id} className={cn("flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors hover:bg-accent/60", selected.has(s.id) && "bg-primary/10")}>
              <input type="checkbox" checked={selected.has(s.id)} onChange={() => toggle(s.id)}
                className="h-3.5 w-3.5 rounded border-border accent-[hsl(var(--primary))]" />
              <span className="truncate">{s.name}</span>
            </label>
          ))}
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
