import { useEffect, useRef, useState } from "react";
import { Filter, ChevronDown, Search } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Input, Select } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { MultiLineChart, type SeriesDef } from "@/components/charts/MultiLineChart";
import { Skeleton } from "@/components/ui/states";
import { type StatusService, getTimeSeries, getMetricsSeries } from "@/lib/monitor";
import { isDbHealthType, isCertType } from "@/lib/services";
import { cn } from "@/lib/utils";

const HEALTH_TYPES = ["WindowsHealth", "LinuxHealth"];
// NOT: istemci tarafı inceltme kaldırıldı — backend artık seri başına ~300 kovalı özet döndürür
// (sunucu tarafı özetleme; spike'lar max ile, down/hata durumları kova içinde korunur).

/** Eski dashboard'daki canlı grafikler: Yanıt Süreleri + CPU/RAM/Disk (panodaki servislerden). */
export function DashboardCharts({ services }: { services: StatusService[] }) {
  const [minutes, setMinutes] = useState(180);
  const [resp, setResp] = useState<SeriesDef[] | null>(null);
  const [cpu, setCpu] = useState<SeriesDef[] | null>(null);
  const [ram, setRam] = useState<SeriesDef[] | null>(null);
  const [disk, setDisk] = useState<SeriesDef[] | null>(null);
  const [manualIds, setManualIds] = useState<Set<number>>(new Set()); // boş = otomatik (en yavaş 12)
  const [tick, setTick] = useState(0); // her veri yenilemesinde artar → grafik remount → animasyon HER SEFERİNDE oynar
  // Lejantta üstü çizilen (gizlenen) seriler — DÖRT grafik de aynı kümeyi paylaşır:
  // yanıt süresinde bir servisi gizleyince CPU/RAM/Disk'ten de kalkar (tick remount'unda da korunur)
  const [hiddenNames, setHiddenNames] = useState<Set<string>>(new Set());
  const toggleName = (name: string) => setHiddenNames((prev) => {
    const n = new Set(prev);
    if (n.has(name)) n.delete(name); else n.add(name);
    return n;
  });

  const idsKey = services.map((s) => s.id).join(",");
  const manualKey = Array.from(manualIds).sort((a, b) => a - b).join(",");

  useEffect(() => {
    const ctrl = new AbortController();
    const enabled = services.filter((s) => s.enabled);
    // Yanıt süresi: elle seçim varsa o; yoksa en yavaş 12 servis (görsel okunabilirlik).
    // DB metrik izlemeleri (adet/%/sn — ms değil) otomatik seçime girmez; elle seçilebilir.
    const respIds = manualIds.size > 0
      ? enabled.filter((s) => manualIds.has(s.id)).slice(0, 20).map((s) => s.id)
      : enabled
          .filter((s) => !isDbHealthType(s.type) && !isCertType(s.type))
          .slice().sort((a, b) => (b.lastResponseTimeMs ?? 0) - (a.lastResponseTimeMs ?? 0))
          .slice(0, 12).map((s) => s.id);
    const healthIds = enabled.filter((s) => HEALTH_TYPES.includes(s.type)).slice(0, 20).map((s) => s.id);

    setResp(null); setCpu(null); setRam(null); setDisk(null);

    // Eşik haritası: yavaş noktaları (koyu sarı) işaretlemek için
    const thrMap = new Map(enabled.map((s) => [s.id, s.responseTimeThresholdMs]));
    if (respIds.length > 0)
      getTimeSeries(respIds, minutes, ctrl.signal).then((d) => {
        setResp(d.series.map((s) => {
          const thr = thrMap.get(s.id) ?? null;
          return {
            name: s.name,
            // Eski davranış: down anı da DEĞERİYLE çizilir (timeout süresi spike olur),
            // kırmızı nokta çizginin ÜZERİNDE tepede görünür — ayrı şerit yok.
            // Öncelik: HATA (st=2, eşik aşımı) / yavaş → koyu sarı; gerçek down → büyük kırmızı.
            points: s.points.map((p) => ({
              t: p.t,
              v: p.ms,
              mark: p.st === 2 || (p.up && thr != null && p.ms > thr) ? ("warn" as const)
                : !p.up ? ("down" as const)
                : undefined,
            })),
          };
        }));
        setTick((t) => t + 1);
      }).catch(() => setResp([]));
    else setResp([]);

    if (healthIds.length > 0)
      getMetricsSeries(healthIds, minutes, ctrl.signal).then((d) => {
        setCpu(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.cpu })) })));
        setRam(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.ram })) })));
        setDisk(d.series.map((s) => ({ name: s.name, points: s.points.map((p) => ({ t: p.t, v: p.disk })) })));
        setTick((t) => t + 1);
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
          <option value="10080">Son 7 gün</option>
          <option value="43200">Son 1 ay</option>
        </Select>
      </div>

      {hasResp && (
        <Card>
          <CardHeader className="flex-row items-center justify-between space-y-0">
            <div>
              <CardTitle>Yanıt Süreleri</CardTitle>
              <CardDescription>{manualIds.size > 0 ? `${manualIds.size} seçili servis` : "En yavaş 12 servis"} (ms) — 🔴 down · 🟡 yavaş/hata çizgi üzerinde işaretlenir</CardDescription>
            </div>
            <ServicePicker services={services.filter((s) => s.enabled)} selected={manualIds} onChange={setManualIds} />
          </CardHeader>
          <CardContent>
            {resp === null ? <Skeleton className="h-[260px] w-full" /> : <MultiLineChart key={`resp-${tick}`} series={resp} unit=" ms" longRange={minutes > 1440} hiddenNames={hiddenNames} onToggleName={toggleName} />}
          </CardContent>
        </Card>
      )}

      {hasHealth && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          <MetricCard key={`cpu-${tick}`} title="CPU Kullanımı" data={cpu} longRange={minutes > 1440} hiddenNames={hiddenNames} onToggleName={toggleName} />
          <MetricCard key={`ram-${tick}`} title="RAM Kullanımı" data={ram} longRange={minutes > 1440} hiddenNames={hiddenNames} onToggleName={toggleName} />
          <MetricCard key={`disk-${tick}`} title="Disk Doluluk" data={disk} longRange={minutes > 1440} hiddenNames={hiddenNames} onToggleName={toggleName} />
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
  const [q, setQ] = useState("");   // liste içi arama (eski tasarımdaki gibi)
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

  const norm = (x: string) => x.toLocaleLowerCase("tr");
  const filtered = q.trim() ? services.filter((s) => norm(s.name).includes(norm(q.trim()))) : services;

  return (
    <div className="relative" ref={ref}>
      <Button variant="outline" size="sm" onClick={() => setOpen((o) => { if (!o) setQ(""); return !o; })}>
        <Filter className="h-3.5 w-3.5" /> Servis Seç
        {selected.size > 0 && <span className="rounded bg-primary/20 px-1 text-[10px] font-bold text-primary">{selected.size}</span>}
        <ChevronDown className="h-3 w-3 opacity-60" />
      </Button>
      {open && (
        <div className="absolute right-0 top-full z-30 mt-1 flex max-h-80 w-72 flex-col rounded-xl border border-border bg-card p-2 shadow-2xl">
          <div className="relative mb-1.5 shrink-0">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input autoFocus value={q} onChange={(e) => setQ(e.target.value)} placeholder="Servis ara…" className="h-8 pl-8 text-sm" />
          </div>
          <div className="min-h-0 overflow-y-auto">
            <button className="mb-1 w-full rounded-md px-2 py-1.5 text-left text-xs text-muted-foreground transition hover:bg-accent/60"
              onClick={() => onChange(new Set())}>
              ⟲ Otomatik (en yavaş 12)
            </button>
            {filtered.length === 0 && <div className="px-2 py-3 text-center text-xs text-muted-foreground">Eşleşen servis yok</div>}
            {filtered.map((s) => (
              <label key={s.id} className={cn("flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-sm transition-colors hover:bg-accent/60", selected.has(s.id) && "bg-primary/10")}>
                <input type="checkbox" checked={selected.has(s.id)} onChange={() => toggle(s.id)}
                  className="h-3.5 w-3.5 rounded border-border accent-[hsl(var(--primary))]" />
                <span className="truncate">{s.name}</span>
              </label>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function MetricCard({ title, data, longRange, hiddenNames, onToggleName }: {
  title: string; data: SeriesDef[] | null; longRange?: boolean;
  hiddenNames?: Set<string>; onToggleName?: (name: string) => void;
}) {
  return (
    <Card>
      <CardHeader className="pb-2"><CardTitle className="text-base">{title}</CardTitle></CardHeader>
      <CardContent>
        {data === null ? <Skeleton className="h-[200px] w-full" /> :
          data.length === 0 ? <div className="py-8 text-center text-sm text-muted-foreground">Sağlık verisi yok</div> :
          <MultiLineChart series={data} height={200} unit="%" domainMax={100} longRange={longRange} hiddenNames={hiddenNames} onToggleName={onToggleName} />}
      </CardContent>
    </Card>
  );
}
