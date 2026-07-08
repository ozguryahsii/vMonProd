import { useEffect, useMemo, useState, type ReactNode } from "react";
import { ArrowDown, ArrowUp } from "lucide-react";
import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid } from "recharts";
import { Drawer } from "@/components/ui/drawer";
import { Button } from "@/components/ui/button";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { getStatDetail, type StatDetail } from "@/lib/stats";
import { cn } from "@/lib/utils";

export interface Drill { source: string; value?: string | null; title: string }

const statusCls: Record<string, string> = {
  Up: "bg-emerald-500/15 text-emerald-400",
  Down: "bg-rose-500/15 text-rose-400",
  Hata: "bg-orange-500/15 text-orange-400",
};

const RANGES = [
  { d: 7, label: "7 gün" }, { d: 30, label: "1 ay" }, { d: 90, label: "3 ay" },
  { d: 180, label: "6 ay" }, { d: 365, label: "1 yıl" },
];

function Pct({ v }: { v: number | null }) {
  if (v == null) return <span className="text-muted-foreground">—</span>;
  const color = v >= 85 ? "text-rose-400" : v >= 65 ? "text-amber-400" : "text-emerald-400";
  return <span className={cn("font-semibold tabular-nums", color)}>%{Math.round(v)}</span>;
}

/** Widget drill-down: eşleşen sunucu listesi + (kaynak metriklerinde) seçilebilir aralıklı trend. */
type SortKey = "name" | "os" | "status" | "cpu" | "ram" | "disk";

export function StatDetailDrawer({ drill, onClose }: { drill: Drill | null; onClose: () => void }) {
  const [days, setDays] = useState(7);
  const [data, setData] = useState<StatDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [sortKey, setSortKey] = useState<SortKey>("name");
  const [sortAsc, setSortAsc] = useState(true);

  const sorted = useMemo(() => {
    const list = (data?.servers ?? []).slice();
    const dir = sortAsc ? 1 : -1;
    list.sort((a, b) => {
      if (sortKey === "cpu" || sortKey === "ram" || sortKey === "disk") {
        const av = a[sortKey] ?? -1, bv = b[sortKey] ?? -1;
        return (av - bv) * dir;
      }
      return String(a[sortKey] ?? "").localeCompare(String(b[sortKey] ?? ""), "tr") * dir;
    });
    return list;
  }, [data, sortKey, sortAsc]);

  const clickSort = (k: SortKey) => {
    if (sortKey === k) setSortAsc((v) => !v);
    else { setSortKey(k); setSortAsc(k === "name" || k === "os" || k === "status"); }
  };
  const SortTh = ({ k, children, center }: { k: SortKey; children: ReactNode; center?: boolean }) => (
    <th className={cn("cursor-pointer select-none px-3 py-2 font-semibold transition-colors hover:text-primary", center && "text-center")}
      onClick={() => clickSort(k)}>
      <span className="inline-flex items-center gap-0.5">
        {children}
        {sortKey === k && (sortAsc ? <ArrowUp className="h-3 w-3" /> : <ArrowDown className="h-3 w-3" />)}
      </span>
    </th>
  );

  useEffect(() => { setDays(7); }, [drill?.source, drill?.value]);
  useEffect(() => {
    if (!drill) { setData(null); return; }
    const ctrl = new AbortController();
    setError(null); setData(null);
    getStatDetail(drill.source, drill.value ?? null, days, ctrl.signal)
      .then(setData)
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); });
    return () => ctrl.abort();
  }, [drill, days]);

  return (
    <Drawer open={!!drill} onClose={onClose}
      title={drill?.title ?? ""}
      description={data ? `${data.count} sunucu` : ""}>
      {error ? <ErrorState message={error} /> :
        !data ? <div className="space-y-3"><Skeleton className="h-40 w-full" /><Skeleton className="h-64 w-full" /></div> : (
          <div className="space-y-5">
            {data.trend && (
              <div>
                <div className="mb-2 flex items-center justify-between">
                  {/* Disk / bağlantı doluluğu / kalan gün: "Ortalama" denmez; CPU/RAM'de ortalama anlamlı */}
                  <h3 className="text-sm font-semibold">
                    {data.trend.metric === "disk" ? "Disk trendi"
                      : data.trend.metric === "bağlantı doluluğu" ? "Bağlantı doluluğu trendi"
                      : data.trend.metric === "kalan gün" ? "Kalan gün trendi"
                      : `Ortalama ${data.trend.metric.toUpperCase()} trendi`}
                  </h3>
                  <div className="flex gap-1">
                    {RANGES.map((r) => (
                      <Button key={r.d} variant={days === r.d ? "default" : "ghost"} size="sm"
                        className="h-7 px-2 text-xs" onClick={() => setDays(r.d)}>{r.label}</Button>
                    ))}
                  </div>
                </div>
                <ResponsiveContainer width="100%" height={180}>
                  <AreaChart data={data.trend.points} margin={{ top: 6, right: 6, left: -20, bottom: 0 }}>
                    <defs><linearGradient id="gT" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="hsl(217 91% 60%)" stopOpacity={0.4} /><stop offset="100%" stopColor="hsl(217 91% 60%)" stopOpacity={0} /></linearGradient></defs>
                    <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
                    <XAxis dataKey="day" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={26} />
                    {/* Kalan gün 100'ü aşabilir → serbest eksen; yüzde metriklerinde 0-100 sabit */}
                    <YAxis domain={data.trend.metric === "kalan gün" ? [0, "auto"] : [0, 100]} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
                    <Tooltip contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
                      formatter={(v: number) => data.trend!.metric === "kalan gün" ? [`${v} gün`, "Kalan"]
                        : [`%${v}`, data.trend!.metric === "cpu" || data.trend!.metric === "ram" ? "Ortalama" : "Doluluk"]} />
                    <Area type="monotone" dataKey="value" stroke="hsl(217 91% 60%)" strokeWidth={2} fill="url(#gT)" animationDuration={700} />
                  </AreaChart>
                </ResponsiveContainer>
              </div>
            )}

            {data.servers.length === 0 ? <EmptyState title="Eşleşen sunucu yok" /> : (
              <div className="overflow-x-auto rounded-lg border border-border/60">
                <table className="w-full text-sm">
                  {/* Başlık scroll'da sabit; başlığa tıkla → sırala (A-Z/Z-A, büyük-küçük) */}
                  <thead className="sticky top-0 z-10">
                    <tr className="border-b border-border bg-card text-left text-[10px] uppercase tracking-wider text-muted-foreground">
                      <SortTh k="name">Sunucu</SortTh>
                      <SortTh k="os">OS</SortTh>
                      <SortTh k="status">Durum</SortTh>
                      <SortTh k="cpu" center>CPU</SortTh>
                      <SortTh k="ram" center>RAM</SortTh>
                      <SortTh k="disk" center>Disk</SortTh>
                    </tr>
                  </thead>
                  <tbody>
                    {sorted.map((s) => (
                      <tr key={s.name} className="border-b border-border/40 transition-colors hover:bg-accent/40" title={s.capacity ?? undefined}>
                        <td className="px-3 py-2">
                          <div className="font-medium">{s.name}</div>
                          <div className="font-mono text-[10px] text-muted-foreground">{s.target}</div>
                        </td>
                        <td className="max-w-[150px] truncate px-3 py-2 text-xs text-muted-foreground" title={s.os}>{s.os}</td>
                        <td className="px-3 py-2">
                          <span className={cn("rounded px-1.5 py-0.5 text-[10px] font-bold", statusCls[s.status] ?? "bg-secondary text-muted-foreground")}>{s.status}</span>
                        </td>
                        <td className="px-3 py-2 text-center"><Pct v={s.cpu} /></td>
                        <td className="px-3 py-2 text-center"><Pct v={s.ram} /></td>
                        <td className="px-3 py-2 text-center"><Pct v={s.disk} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
    </Drawer>
  );
}
