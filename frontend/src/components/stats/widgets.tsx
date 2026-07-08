import type { ReactNode } from "react";
import { ChevronRight } from "lucide-react";
import { Area, AreaChart, Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend, Line, LineChart } from "recharts";
import { DonutChart } from "@/components/charts/DonutChart";
import type { StatsData, StatWidgetDef } from "@/lib/stats";
import type { Drill } from "./StatDetailDrawer";
import { DB_METRIC_META, DB_PLATFORM_CLS, fmtDbValue, type DbPlatform } from "@/lib/services";
import { cn } from "@/lib/utils";

type OnDrill = (d: Drill) => void;

/** Tıklanabilir sarmalayıcı (eski ekrandaki gibi: her kutu detaya iner) */
function Clickable({ onClick, children, className }: { onClick?: () => void; children: ReactNode; className?: string }) {
  if (!onClick) return <div className={cn("h-full", className)}>{children}</div>;
  return (
    <button type="button" onClick={onClick}
      className={cn("block h-full w-full cursor-pointer text-left transition-colors hover:bg-accent/20", className)}>
      {children}
    </button>
  );
}

/* ---------- Widget kataloğu (eski palet birebir) ---------- */
export interface CatalogItem { type: string; source: string; label: string; group: string; w: number; h: number }
export const WIDGET_CATALOG: CatalogItem[] = [
  { type: "counter", source: "total_servers", label: "Toplam Sunucu", group: "Sayaç", w: 3, h: 2 },
  { type: "counter", source: "up", label: "Çalışan", group: "Sayaç", w: 3, h: 2 },
  { type: "counter", source: "down", label: "Kapalı", group: "Sayaç", w: 3, h: 2 },
  { type: "counter", source: "error", label: "Hata", group: "Sayaç", w: 3, h: 2 },
  { type: "resource", source: "cpu", label: "CPU (kullanılan/atanan)", group: "Kaynak", w: 4, h: 3 },
  { type: "resource", source: "ram", label: "RAM (kullanılan/atanan)", group: "Kaynak", w: 4, h: 3 },
  { type: "resource", source: "disk", label: "Disk (kullanılan/atanan)", group: "Kaynak", w: 4, h: 3 },
  { type: "pie", source: "os_kind", label: "OS dağılımı (Windows/Linux)", group: "Pasta", w: 4, h: 4 },
  { type: "pie", source: "os_version", label: "OS sürümleri", group: "Pasta", w: 4, h: 4 },
  { type: "pie", source: "tag", label: "Etiket (Tag) dağılımı", group: "Pasta", w: 4, h: 4 },
  { type: "gauge", source: "avg_cpu", label: "Ort. CPU %", group: "Gösterge", w: 4, h: 3 },
  { type: "gauge", source: "avg_ram", label: "Ort. RAM %", group: "Gösterge", w: 4, h: 3 },
  { type: "gauge", source: "avg_disk", label: "Ort. Disk %", group: "Gösterge", w: 4, h: 3 },
  { type: "fleet", source: "fleet", label: "Filo trendi (30 gün)", group: "İçgörü", w: 12, h: 4 },
  { type: "top", source: "top", label: "Top 10 Kaynak Tüketen", group: "İçgörü", w: 12, h: 4 },
  { type: "critical", source: "critical", label: "Kritik durum", group: "İçgörü", w: 4, h: 2 },
  { type: "uptime", source: "uptime", label: "Erişilebilirlik (uptime)", group: "İçgörü", w: 4, h: 2 },
  { type: "histogram", source: "histogram", label: "Kullanım dağılımı", group: "İçgörü", w: 6, h: 4 },
  { type: "capacity", source: "capacity", label: "Kapasite (kullanılan/atanan)", group: "İçgörü", w: 6, h: 4 },
  { type: "rising", source: "rising", label: "Kapasite kullanımı artan", group: "İçgörü", w: 6, h: 4 },
  { type: "outage", source: "outage", label: "Kesinti özeti (7 gün)", group: "İçgörü", w: 6, h: 4 },
  { type: "os_eol", source: "os_eol", label: "Destek sonu (EOL) OS", group: "İçgörü", w: 6, h: 4 },
  { type: "heatmap", source: "heatmap", label: "CPU ısı haritası (24s)", group: "İçgörü", w: 12, h: 5 },
  { type: "disk_forecast", source: "disk_forecast", label: "Yaklaşan Disk Dolumları", group: "İçgörü", w: 6, h: 4 },
  { type: "cert_expiry", source: "cert_expiry", label: "Yaklaşan Sertifika Bitişleri", group: "İçgörü", w: 6, h: 4 },
  // DB İzleme Fazı — veritabanı sağlık widget'ları
  { type: "db_health", source: "db_health", label: "Veritabanı Sağlığı", group: "Veritabanı", w: 12, h: 4 },
  { type: "db_usage", source: "db_usage", label: "DB Bağlantı Doluluğu", group: "Veritabanı", w: 6, h: 4 },
  { type: "db_alerts", source: "db_alerts", label: "DB Uyarıları", group: "Veritabanı", w: 6, h: 4 },
];

export function widgetLabel(w: StatWidgetDef): string {
  if (w.title) return w.title;
  const c = WIDGET_CATALOG.find((x) => x.type === w.type && x.source === w.source);
  return c?.label ?? `${w.type}/${w.source}`;
}

const tip = { background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" };

/* ---------- Tek widget içeriği ---------- */
export function WidgetRenderer({ w, data, onDrill }: { w: StatWidgetDef; data: StatsData; onDrill: OnDrill }) {
  switch (w.type) {
    case "counter": return <Counter w={w} d={data} onDrill={onDrill} />;
    case "resource": return <Resource w={w} d={data} onDrill={onDrill} />;
    case "pie": return <PieW w={w} d={data} onDrill={onDrill} />;
    case "gauge": return <Gauge w={w} d={data} onDrill={onDrill} />;
    case "fleet": return <Fleet d={data} />;
    case "top": return <TopW d={data} onDrill={onDrill} />;
    case "critical": return <Critical d={data} onDrill={onDrill} />;
    case "uptime": return <UptimeW d={data} onDrill={onDrill} />;
    case "histogram": return <Histogram d={data} onDrill={onDrill} />;
    case "capacity": return <Capacity d={data} />;
    case "rising": return <Rising d={data} onDrill={onDrill} />;
    case "outage": return <Outage d={data} onDrill={onDrill} />;
    case "os_eol": return <OsEol d={data} onDrill={onDrill} />;
    case "heatmap": return <Heatmap d={data} />;
    case "disk_forecast": return <DiskForecastW d={data} onDrill={onDrill} />;
    case "cert_expiry": return <CertExpiryW d={data} />;
    case "db_health": return <DbHealthW d={data} onDrill={onDrill} />;
    case "db_usage": return <DbUsageW d={data} onDrill={onDrill} />;
    case "db_alerts": return <DbAlertsW d={data} onDrill={onDrill} />;
    default: return <div className="p-4 text-sm text-muted-foreground">Bilinmeyen widget: {w.type}</div>;
  }
}

const COUNTER_TITLE: Record<string, string> = {
  total_servers: "Tüm sağlık sunucuları", up: "Çalışan sunucular", down: "Kapalı sunucular", error: "Hatalı sunucular",
};

function Counter({ w, d, onDrill }: { w: StatWidgetDef; d: StatsData; onDrill: OnDrill }) {
  const map: Record<string, { v: number; cls: string }> = {
    total_servers: { v: d.counts.total, cls: "text-foreground" },
    up: { v: d.counts.up, cls: "text-emerald-400" },
    down: { v: d.counts.down, cls: "text-rose-400" },
    error: { v: d.counts.error, cls: "text-orange-400" },
  };
  const m = map[w.source] ?? map.total_servers;
  return (
    <Clickable onClick={() => onDrill({ source: w.source, title: COUNTER_TITLE[w.source] ?? w.source })}
      className="flex items-center justify-center">
      <span className={cn("mx-auto block text-center text-4xl font-bold tabular-nums transition-transform hover:scale-110", m.cls)}>{m.v}</span>
    </Clickable>
  );
}

function Resource({ w, d, onDrill }: { w: StatWidgetDef; d: StatsData; onDrill: OnDrill }) {
  const r = w.source === "ram" ? d.ram : w.source === "disk" ? d.disk : d.cpu;
  const pct = r.alloc > 0 ? Math.min(100, Math.round((r.used / r.alloc) * 100)) : 0;
  const color = pct >= 85 ? "bg-rose-500" : pct >= 65 ? "bg-amber-500" : "bg-emerald-500";
  return (
    <Clickable onClick={() => onDrill({ source: w.source, title: `${w.source.toUpperCase()} — sunucular ve trend` })}
      className="flex flex-col justify-center gap-2 px-4">
      <div className="flex items-baseline gap-1.5">
        <span className="text-2xl font-bold tabular-nums">{r.used}</span>
        <span className="text-sm text-muted-foreground">/ {r.alloc} {r.unit}</span>
      </div>
      <div className="h-2.5 overflow-hidden rounded-full bg-muted">
        <div className={cn("h-full rounded-full transition-all", color)} style={{ width: `${pct}%` }} />
      </div>
      <div className="text-right text-xs tabular-nums text-muted-foreground">%{pct} kullanımda</div>
    </Clickable>
  );
}

const PIE_TITLE: Record<string, string> = { os_kind: "OS", os_version: "OS sürümü", tag: "Etiket" };

function PieW({ w, d, onDrill }: { w: StatWidgetDef; d: StatsData; onDrill: OnDrill }) {
  const arr = w.source === "os_kind" ? d.osKind : w.source === "os_version" ? d.osVersion : d.tags;
  if (!arr || arr.length === 0) return <Empty />;
  return (
    <div className="h-full px-2 pb-1">
      <DonutChart data={arr} height="100%"
        onSliceClick={(name) => onDrill({ source: w.source, value: name, title: `${PIE_TITLE[w.source] ?? w.source}: ${name}` })}
        onCenterClick={() => onDrill({ source: w.source, title: `${PIE_TITLE[w.source] ?? w.source} — tümü` })} />
    </div>
  );
}

function Gauge({ w, d, onDrill }: { w: StatWidgetDef; d: StatsData; onDrill: OnDrill }) {
  const v = w.source === "avg_ram" ? d.avg.ram : w.source === "avg_disk" ? d.avg.disk : d.avg.cpu;
  if (v == null) return <Empty />;
  const pct = Math.max(0, Math.min(100, v));
  const color = pct >= 85 ? "hsl(0 72% 51%)" : pct >= 65 ? "hsl(38 92% 55%)" : "hsl(158 64% 44%)";
  // 210° → -30° yay (eski gauge açıları)
  const polar = (deg: number, r: number) => {
    const rad = (deg * Math.PI) / 180;
    return [50 + r * Math.cos(rad), 50 - r * Math.sin(rad)];
  };
  const arc = (from: number, to: number, r: number) => {
    const [x1, y1] = polar(from, r); const [x2, y2] = polar(to, r);
    const large = Math.abs(from - to) > 180 ? 1 : 0;
    return `M ${x1} ${y1} A ${r} ${r} 0 ${large} 1 ${x2} ${y2}`;
  };
  const endDeg = 210 - (240 * pct) / 100;
  return (
    <Clickable onClick={() => onDrill({ source: w.source, title: `Ortalama ${w.source.replace("avg_", "").toUpperCase()} — sunucular ve trend` })}
      className="flex items-center justify-center">
      <svg viewBox="0 0 100 78" className="mx-auto h-full max-h-40 w-auto transition-transform hover:scale-105">
        <path d={arc(210, -30, 40)} fill="none" stroke="hsl(var(--muted))" strokeWidth="8" strokeLinecap="round" />
        <path d={arc(210, endDeg, 40)} fill="none" stroke={color} strokeWidth="8" strokeLinecap="round" />
        <text x="50" y="52" textAnchor="middle" fontSize="16" fontWeight="700" fill="hsl(var(--foreground))">%{Math.round(pct)}</text>
      </svg>
    </Clickable>
  );
}

function Fleet({ d }: { d: StatsData }) {
  if (!d.fleet || d.fleet.length === 0) return <Empty />;
  return (
    <ResponsiveContainer width="100%" height="100%">
      <AreaChart data={d.fleet} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="day" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={24} />
        <YAxis domain={[0, 100]} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <Tooltip contentStyle={tip} />
        <Legend iconType="circle" formatter={(v) => <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 11 }}>{v}</span>} />
        <Area type="monotone" dataKey="cpu" name="CPU" stroke="hsl(358 85% 55%)" strokeWidth={2} fillOpacity={0.12} fill="hsl(358 85% 55%)" />
        <Area type="monotone" dataKey="ram" name="RAM" stroke="hsl(271 76% 63%)" strokeWidth={2} fillOpacity={0.12} fill="hsl(271 76% 63%)" />
        <Area type="monotone" dataKey="disk" name="Disk" stroke="hsl(174 62% 47%)" strokeWidth={2} fillOpacity={0.12} fill="hsl(174 62% 47%)" />
      </AreaChart>
    </ResponsiveContainer>
  );
}

function MiniTop({ title, items, onClick }: { title: string; items: { name: string; value: number }[]; onClick?: () => void }) {
  const max = Math.max(1, ...items.map((i) => i.value));
  return (
    <div className="min-w-0 flex-1">
      <button type="button" onClick={onClick}
        className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground transition-colors hover:text-primary">{title}</button>
      <div className="space-y-1.5">
        {items.slice(0, 10).map((i) => (
          <div key={i.name}>
            <div className="flex justify-between text-xs"><span className="truncate pr-1">{i.name}</span><span className="shrink-0 tabular-nums text-muted-foreground">%{i.value}</span></div>
            <div className="h-1 overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-primary/70" style={{ width: `${(i.value / max) * 100}%` }} /></div>
          </div>
        ))}
        {items.length === 0 && <p className="text-xs text-muted-foreground">Veri yok</p>}
      </div>
    </div>
  );
}
function TopW({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  return (
    <div className="flex h-full gap-4 overflow-y-auto px-4 py-2">
      <MiniTop title="CPU" items={d.top.cpu} onClick={() => onDrill({ source: "cpu", title: "CPU — sunucular ve trend" })} />
      <MiniTop title="RAM" items={d.top.ram} onClick={() => onDrill({ source: "ram", title: "RAM — sunucular ve trend" })} />
      <MiniTop title="Disk" items={d.top.disk} onClick={() => onDrill({ source: "disk", title: "Disk — sunucular ve trend" })} />
    </div>
  );
}

function BigPair({ a, b }: {
  a: { label: string; v: string; cls?: string; onClick?: () => void };
  b: { label: string; v: string; cls?: string; onClick?: () => void };
}) {
  return (
    <div className="flex h-full items-center justify-around">
      {[a, b].map((x) => (
        <button key={x.label} type="button" onClick={x.onClick} disabled={!x.onClick}
          className={cn("rounded-lg px-3 py-1 text-center transition-transform", x.onClick && "cursor-pointer hover:scale-110")}>
          <div className={cn("text-3xl font-bold tabular-nums", x.cls ?? "text-foreground")}>{x.v}</div>
          <div className="text-xs uppercase tracking-wide text-muted-foreground">{x.label}</div>
        </button>
      ))}
    </div>
  );
}
function Critical({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  // Tek kutuda 4 kritik gösterge: CPU/RAM/Disk ≥85 + eşik aşımı
  const items = [
    { label: "CPU ≥85%", v: d.critical.cpuHot ?? 0, warn: "text-rose-400", source: "cpu_full", title: "CPU kullanımı ≥85% sunucular" },
    { label: "RAM ≥85%", v: d.critical.ramHot ?? 0, warn: "text-rose-400", source: "ram_full", title: "RAM kullanımı ≥85% sunucular" },
    { label: "Disk ≥85%", v: d.critical.diskFull, warn: "text-rose-400", source: "disk_full", title: "Disk doluluğu ≥85% sunucular" },
    { label: "Eşik aşımı", v: d.critical.breach, warn: "text-orange-400", source: "error", title: "Eşik aşımı (hata) sunucular" },
  ];
  return (
    <div className="flex h-full items-center justify-around">
      {items.map((x) => (
        <button key={x.label} type="button" onClick={() => onDrill({ source: x.source, title: x.title })}
          className="cursor-pointer rounded-lg px-2 py-1 text-center transition-transform hover:scale-110">
          <div className={cn("text-3xl font-bold tabular-nums", x.v > 0 ? x.warn : "text-emerald-400")}>{x.v}</div>
          <div className="text-xs uppercase tracking-wide text-muted-foreground">{x.label}</div>
        </button>
      ))}
    </div>
  );
}
function UptimeW({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const open = () => onDrill({ source: "up", title: "Çalışan sunucular" });
  return <BigPair
    a={{ label: "Son 24 saat", v: `%${d.uptime.h24}`, cls: d.uptime.h24 >= 99 ? "text-emerald-400" : "text-amber-400", onClick: open }}
    b={{ label: "Son 7 gün", v: `%${d.uptime.d7}`, cls: d.uptime.d7 >= 99 ? "text-emerald-400" : "text-amber-400", onClick: open }} />;
}

const BANDS = ["0-20", "20-40", "40-60", "60-80", "80-100"];
function Histogram({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  // Eski tasarım: DİKEY gruplu çubuklar; çubuğa tıkla → o metrik + bandın sunucuları (2.15.2)
  const rows = BANDS.map((b, i) => ({ band: b, CPU: d.histogram.cpu[i] ?? 0, RAM: d.histogram.ram[i] ?? 0, Disk: d.histogram.disk[i] ?? 0 }));
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const click = (metric: string) => (entry: any) => {
    const band = entry?.band ?? entry?.payload?.band;
    if (band) onDrill({ source: `hist_${metric}`, value: band, title: `${metric.toUpperCase()} %${band} bandındaki sunucular` });
  };
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart data={rows} margin={{ top: 8, right: 8, left: -20, bottom: 0 }} barCategoryGap="22%">
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="band" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <YAxis tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} allowDecimals={false} />
        <Tooltip cursor={{ fill: "hsl(var(--accent) / 0.4)" }} contentStyle={tip} />
        <Legend iconType="circle" formatter={(v) => <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 11 }}>{v}</span>} />
        <Bar dataKey="CPU" fill="hsl(358 85% 55%)" radius={[3, 3, 0, 0]} cursor="pointer" onClick={click("cpu")} animationDuration={700} />
        <Bar dataKey="RAM" fill="hsl(271 76% 63%)" radius={[3, 3, 0, 0]} cursor="pointer" onClick={click("ram")} animationDuration={700} />
        <Bar dataKey="Disk" fill="hsl(174 62% 47%)" radius={[3, 3, 0, 0]} cursor="pointer" onClick={click("disk")} animationDuration={700} />
      </BarChart>
    </ResponsiveContainer>
  );
}

function Capacity({ d }: { d: StatsData }) {
  if (!d.capacity || d.capacity.length === 0) return <Empty />;
  const rows = [
    { k: "cpu", label: "CPU (çekirdek)", used: "cpuUsed", alloc: "cpuAlloc", color: "hsl(358 85% 55%)" },
    { k: "ram", label: "RAM (GB)", used: "ramUsed", alloc: "ramAlloc", color: "hsl(271 76% 63%)" },
    { k: "disk", label: "Disk (GB)", used: "diskUsed", alloc: "diskAlloc", color: "hsl(174 62% 47%)" },
  ] as const;
  return (
    <div className="flex h-full flex-col justify-around gap-1 px-3 py-1">
      {rows.map((r) => {
        const last = d.capacity[d.capacity.length - 1] as unknown as Record<string, number>;
        return (
          <div key={r.k} className="min-h-0 flex-1">
            <div className="flex justify-between text-[11px] text-muted-foreground">
              <span>{r.label}</span>
              <span className="tabular-nums">{last[r.used]} / {last[r.alloc]}</span>
            </div>
            <ResponsiveContainer width="100%" height="80%">
              <LineChart data={d.capacity} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                <Tooltip contentStyle={tip} labelFormatter={(l) => String(l)} />
                <XAxis dataKey="day" hide /><YAxis hide />
                <Line type="monotone" dataKey={r.used} name="kullanılan" stroke={r.color} strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey={r.alloc} name="atanan" stroke="hsl(var(--muted-foreground))" strokeWidth={1} strokeDasharray="4 3" dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        );
      })}
    </div>
  );
}

function RisingList({ title, items }: { title: string; items: { name: string; from: number; to: number; delta: number }[] }) {
  return (
    <div className="min-w-0 flex-1">
      <p className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</p>
      {items.length === 0 ? <p className="text-xs text-muted-foreground">Artış yok</p> : (
        <div className="space-y-1">
          {items.slice(0, 5).map((i) => (
            <div key={i.name} className="flex items-center justify-between text-xs">
              <span className="truncate pr-1">{i.name}</span>
              <span className="shrink-0 tabular-nums"><span className="text-muted-foreground">%{i.from}→%{i.to}</span> <span className="font-semibold text-rose-400">+{i.delta}</span></span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
function Rising({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  return (
    <div className="flex h-full gap-4 overflow-y-auto px-4 py-2">
      {(["cpu", "ram", "disk"] as const).map((m) => (
        <button key={m} type="button" className="min-w-0 flex-1 text-left"
          onClick={() => onDrill({ source: m, title: `${m.toUpperCase()} — sunucular ve trend` })}>
          <RisingList title={m.toUpperCase()} items={d.rising[m]} />
        </button>
      ))}
    </div>
  );
}

function Outage({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const max = Math.max(1, ...d.outages.daily.map((x) => x.value));
  const open = () => onDrill({ source: "outage", title: "Son 7 günde kesinti yaşayan sunucular" });
  return (
    <button type="button" onClick={open}
      className="flex h-full w-full cursor-pointer flex-col gap-2 px-4 py-2 text-left transition-colors hover:bg-accent/20">
      <div className="flex items-center justify-around">
        <div className="text-center"><div className="text-2xl font-bold tabular-nums text-rose-400">{d.outages.count}</div><div className="text-[11px] uppercase text-muted-foreground">kesinti</div></div>
        <div className="text-center"><div className="text-2xl font-bold tabular-nums text-amber-400">{d.outages.minutes}</div><div className="text-[11px] uppercase text-muted-foreground">dakika</div></div>
      </div>
      {d.outages.daily.length > 0 && (
        <div className="flex h-10 items-end gap-1">
          {d.outages.daily.map((x) => (
            <div key={x.day} title={`${x.day}: ${x.value}`} className="flex-1 rounded-t bg-rose-500/70" style={{ height: `${(x.value / max) * 100}%` }} />
          ))}
        </div>
      )}
      <div className="min-h-0 flex-1 space-y-1 overflow-y-auto">
        {d.outages.worst.map((x) => (
          <div key={x.name} className="flex justify-between text-xs"><span className="truncate pr-1">{x.name}</span><span className="tabular-nums text-rose-400">{x.value}</span></div>
        ))}
        {d.outages.worst.length === 0 && <p className="text-center text-xs text-muted-foreground">Son 7 günde kesinti yok 🎉</p>}
      </div>
    </button>
  );
}

function OsEol({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const e = d.osEol;
  if (!e || e.items.length === 0)
    return <div className="flex h-full items-center justify-center text-sm text-emerald-400">EOL işletim sistemi yok 🎉</div>;
  return (
    <div className="h-full space-y-1.5 overflow-y-auto px-4 py-2">
      {e.items.map((i) => (
        <button key={i.name} type="button"
          onClick={() => onDrill({ source: "os_version", value: i.name, title: `${i.name} çalıştıran sunucular` })}
          className="flex w-full items-center justify-between gap-2 rounded-md border border-border/60 bg-muted/20 px-2.5 py-1.5 text-left text-xs transition-colors hover:bg-accent/50">
          <span className="truncate">{i.name}</span>
          <span className="flex shrink-0 items-center gap-1.5">
            <span className="tabular-nums text-muted-foreground">{i.value} sunucu</span>
            <span className={cn("rounded px-1.5 py-0.5 font-semibold", i.status === "eol" ? "bg-rose-500/15 text-rose-400" : "bg-amber-500/15 text-amber-400")}>
              {i.status === "eol" ? "EOL" : i.days != null ? `${i.days} gün` : "yakında"}
            </span>
          </span>
        </button>
      ))}
    </div>
  );
}

function Heatmap({ d }: { d: StatsData }) {
  const { rows, data } = d.heatmap;
  if (!rows || rows.length === 0) return <Empty />;
  const map = new Map<string, number>();
  for (const [h, y, v] of data) map.set(`${y}:${h}`, v);
  const color = (v: number | undefined) => {
    if (v == null) return "hsl(var(--muted))";
    const t = Math.min(1, v / 100);
    return `hsl(${Math.round(150 - 150 * t)} 70% 45%)`; // yeşil→kırmızı
  };
  return (
    <div className="h-full overflow-auto px-3 py-2">
      <div className="grid gap-0.5 text-[9px]" style={{ gridTemplateColumns: `88px repeat(24, minmax(10px, 1fr))` }}>
        <div />
        {Array.from({ length: 24 }, (_, h) => <div key={h} className="text-center text-muted-foreground">{h}</div>)}
        {rows.map((name, y) => (
          [<div key={`n${y}`} className="truncate pr-1 text-right text-[10px] text-muted-foreground" title={name}>{name}</div>,
          ...Array.from({ length: 24 }, (_, h) => {
            const v = map.get(`${y}:${h}`);
            return <div key={`${y}-${h}`} title={`${name} ${h}:00 — ${v != null ? `%${v}` : "veri yok"}`} className="aspect-square rounded-sm" style={{ background: color(v) }} />;
          })]
        ))}
      </div>
    </div>
  );
}

function Empty() {
  return <div className="flex h-full items-center justify-center text-sm text-muted-foreground">Veri yok</div>;
}

/* ================= Yol haritası #2: Yaklaşan Disk Dolumları ================= */
/** Son 30 günlük eğilimle ~90 gün içinde dolması beklenen diskler ("bu gidişle X günde dolar"). */
function DiskForecastW({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const items = d.diskForecast ?? [];
  if (items.length === 0)
    return <div className="flex h-full items-center justify-center text-sm text-emerald-400">90 gün içinde dolması beklenen disk yok 🎉</div>;
  return (
    <div className="h-full space-y-1.5 overflow-y-auto px-4 py-2">
      {items.map((i) => {
        const cls = i.daysLeft <= 7 ? "bg-rose-500/15 text-rose-400"
          : i.daysLeft <= 30 ? "bg-amber-500/15 text-amber-400" : "bg-muted text-muted-foreground";
        const bar = i.current >= 85 ? "bg-rose-500" : i.current >= 65 ? "bg-amber-500" : "bg-emerald-500";
        return (
          <button key={i.name} type="button"
            onClick={() => onDrill({ source: "disk", value: i.name, title: `${i.name} — disk trendi` })}
            className="group block w-full cursor-pointer rounded-md border border-border/60 bg-muted/20 px-2.5 py-1.5 text-left text-xs transition-colors hover:border-primary/50 hover:bg-accent">
            <div className="flex items-center justify-between gap-2">
              <span className="truncate font-medium group-hover:text-primary">{i.name}</span>
              <span className="flex shrink-0 items-center gap-1.5 tabular-nums">
                <span className="text-muted-foreground">%{i.current} · +{i.perDay}%/gün</span>
                <span className={cn("rounded px-1.5 py-0.5 font-semibold", cls)}>~{i.daysLeft} gün · {i.date}</span>
                <ChevronRight className="h-3.5 w-3.5 shrink-0 text-primary opacity-0 transition-opacity group-hover:opacity-100" />
              </span>
            </div>
            <div className="mt-1 h-1 overflow-hidden rounded-full bg-muted">
              <div className={cn("h-full rounded-full", bar)} style={{ width: `${Math.min(100, i.current)}%` }} />
            </div>
          </button>
        );
      })}
    </div>
  );
}

/* ================= SSL Sertifika: Yaklaşan Bitişler ================= */
/** SSL sertifika izlemelerinin kalan günleri (≤7 kırmızı, ≤30 amber); iç/dış uyumsuzluk işaretlenir. */
function CertExpiryW({ d }: { d: StatsData }) {
  const items = d.certExpiry ?? [];
  if (items.length === 0)
    return (
      <div className="flex h-full flex-col items-center justify-center gap-1 text-sm text-muted-foreground">
        <span>Henüz SSL sertifika izlemesi yok</span>
        <span className="text-xs">İzlemeler ekranından "SSL Sertifikası" tipiyle ekleyebilirsiniz.</span>
      </div>
    );
  return (
    <div className="h-full space-y-1.5 overflow-y-auto px-4 py-2">
      {items.map((i) => {
        const days = i.daysLeft;
        const mismatch = (i.error ?? "").includes("FARKLI");
        const expired = days != null && days < 0;
        const chip = expired || mismatch ? "bg-rose-500/15 text-rose-400"
          : days != null && days <= 7 ? "bg-rose-500/15 text-rose-400"
          : days != null && days <= 30 ? "bg-amber-500/15 text-amber-400"
          : "bg-emerald-500/15 text-emerald-400";
        return (
          <div key={i.name} title={i.error ?? i.target}
            className="flex w-full items-center justify-between gap-2 rounded-md border border-border/60 bg-muted/20 px-2.5 py-1.5 text-xs">
            <span className="min-w-0">
              <span className="block truncate font-medium">{i.name}</span>
              <span className="block truncate font-mono text-[10px] text-muted-foreground">{i.target}</span>
            </span>
            <span className="flex shrink-0 items-center gap-1.5">
              {mismatch && <span className="rounded bg-rose-500/15 px-1.5 py-0.5 font-semibold text-rose-400">iç≠dış</span>}
              <span className={cn("rounded px-1.5 py-0.5 font-semibold tabular-nums", chip)}>
                {days == null ? "—" : expired ? "SÜRESİ DOLDU" : `${days} gün`}
              </span>
            </span>
          </div>
        );
      })}
    </div>
  );
}

/* ================= DB İzleme Fazı widget'ları ================= */

type DbItem = StatsData["dbHealth"]["items"][number];

/** DB izlemesinin durum sınıfı: 0=up (slow=YAVAŞ), 1=down, 2=hata */
function dbCat(i: DbItem): "up" | "slow" | "down" | "error" {
  if (i.status === 1) return "down";
  if (i.status === 2) return "error";
  return i.slow ? "slow" : "up";
}
const dbCatCls: Record<string, string> = {
  up: "text-emerald-400", slow: "text-amber-400", down: "text-rose-400", error: "text-orange-400",
};

function DbEmpty() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-1 text-sm text-muted-foreground">
      <span>Henüz veritabanı izlemesi yok</span>
      <span className="text-xs">İzlemeler ekranından Veritabanı kategorisinde ekleyebilirsiniz.</span>
    </div>
  );
}

/** Veritabanı Sağlığı: enstans (platform+host) grupları + metrik rozetleri; tıkla → DB listesi */
function DbHealthW({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const items = d.dbHealth?.items ?? [];
  if (items.length === 0) return <DbEmpty />;
  const c = d.dbHealth.counts;

  // Dashboard paneliyle aynı kural: önce ServiceName/DB adına göre grupla (ortamlar ayrışır), ad yoksa host
  const groups = new Map<string, { platform: DbPlatform; dbName: string | null; host: string; items: DbItem[] }>();
  for (const i of items) {
    const meta = DB_METRIC_META[i.type];
    if (!meta) continue;
    const host = `${i.target}${i.port ? `:${i.port}` : ""}`;
    const dbName = (i.extra ?? "").trim().replace(/^SID=/i, "") || null;
    const key = dbName ? `${meta.platform}|db:${dbName.toUpperCase()}` : `${meta.platform}|host:${host}`;
    let g = groups.get(key);
    if (!g) { g = { platform: meta.platform, dbName, host, items: [] }; groups.set(key, g); }
    g.items.push(i);
  }

  const counters: { label: string; v: number; cls: string }[] = [
    { label: "Sağlıklı", v: c.ok, cls: "text-emerald-400" },
    { label: "Yavaş", v: c.warn, cls: "text-amber-400" },
    { label: "Hata", v: c.err, cls: "text-orange-400" },
    { label: "Kapalı", v: c.down, cls: "text-rose-400" },
  ];

  return (
    <div className="flex h-full flex-col gap-2 px-4 py-2">
      <div className="flex items-center justify-around">
        {counters.map((x) => (
          <button key={x.label} type="button" className="text-center transition-transform hover:scale-110"
            onClick={() => onDrill({ source: "db_health", title: "Veritabanı izlemeleri" })}>
            <div className={cn("text-2xl font-bold tabular-nums", x.cls)}>{x.v}</div>
            <div className="text-[10px] uppercase tracking-wide text-muted-foreground">{x.label}</div>
          </button>
        ))}
      </div>
      <div className="min-h-0 flex-1 space-y-1.5 overflow-y-auto">
        {Array.from(groups.values()).map((g) => (
          <button key={`${g.platform}|${g.dbName ?? g.host}`} type="button"
            onClick={() => onDrill({ source: "db_health", value: g.platform, title: `${g.platform} izlemeleri` })}
            className="flex w-full flex-wrap items-center gap-x-2 gap-y-1 rounded-md border border-border/60 bg-muted/20 px-2.5 py-1.5 text-left text-xs transition-colors hover:bg-accent/50">
            {g.dbName && <span className="font-semibold">{g.dbName}</span>}
            <span className={cn("font-semibold", DB_PLATFORM_CLS[g.platform])}>{g.dbName ? `· ${g.platform}` : g.platform}</span>
            <span className="truncate font-mono text-muted-foreground">{g.host}</span>
            <span className="ml-auto flex flex-wrap justify-end gap-x-2.5 gap-y-0.5">
              {g.items.map((i) => {
                const cat = dbCat(i);
                return (
                  <span key={i.id} className="whitespace-nowrap tabular-nums" title={i.lastError ?? i.name}>
                    <span className="text-muted-foreground">{DB_METRIC_META[i.type].short}: </span>
                    <span className={cn("font-semibold", cat === "up" ? "text-foreground" : dbCatCls[cat])}>
                      {cat === "down" ? "KAPALI" : fmtDbValue(i.type, i.value)}
                    </span>
                  </span>
                );
              })}
            </span>
          </button>
        ))}
      </div>
    </div>
  );
}

/** DB Bağlantı Doluluğu: ConnectionUsage izlemeleri % çubuklarıyla (dolu → kırmızı) */
function DbUsageW({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const items = (d.dbHealth?.items ?? [])
    .filter((i) => DB_METRIC_META[i.type]?.metric === "usage")
    .sort((a, b) => (b.value ?? 0) - (a.value ?? 0));
  if (items.length === 0) return <DbEmpty />;
  return (
    <div className="h-full space-y-2 overflow-y-auto px-4 py-2">
      {items.map((i) => {
        const meta = DB_METRIC_META[i.type];
        const pct = Math.max(0, Math.min(100, i.value ?? 0));
        const bar = pct >= 90 ? "bg-rose-500" : pct >= 70 ? "bg-amber-500" : "bg-emerald-500";
        return (
          <button key={i.id} type="button"
            className="group block w-full cursor-pointer rounded-md border border-transparent px-2 py-1.5 text-left transition-colors hover:border-primary/50 hover:bg-accent"
            onClick={() => onDrill({ source: "db_usage", value: i.name, title: `${i.name} — bağlantı doluluğu trendi` })}>
            <div className="flex justify-between text-xs">
              <span className="truncate pr-1 group-hover:text-primary"><span className={cn("font-semibold", DB_PLATFORM_CLS[meta.platform])}>{meta.platform}</span> {i.target}{i.port ? `:${i.port}` : ""}</span>
              <span className="flex shrink-0 items-center gap-1">
                <span className={cn("tabular-nums font-semibold", dbCat(i) === "down" ? "text-rose-400" : "text-muted-foreground")}>
                  {dbCat(i) === "down" ? "KAPALI" : `%${pct}`}
                </span>
                <ChevronRight className="h-3.5 w-3.5 shrink-0 text-primary opacity-0 transition-opacity group-hover:opacity-100" />
              </span>
            </div>
            <div className="mt-0.5 h-1.5 overflow-hidden rounded-full bg-muted">
              <div className={cn("h-full rounded-full transition-all", bar)} style={{ width: `${pct}%` }} />
            </div>
          </button>
        );
      })}
    </div>
  );
}

/** DB Uyarıları: sorunlu (kapalı/hata/yavaş) DB izlemeleri listesi; hepsi sağlıklıysa kutlama */
function DbAlertsW({ d, onDrill }: { d: StatsData; onDrill: OnDrill }) {
  const all = d.dbHealth?.items ?? [];
  if (all.length === 0) return <DbEmpty />;
  const sevOrder = { down: 0, error: 1, slow: 2, up: 3 } as const;
  const problems = all.filter((i) => dbCat(i) !== "up")
    .sort((a, b) => sevOrder[dbCat(a)] - sevOrder[dbCat(b)]);
  if (problems.length === 0)
    return <div className="flex h-full items-center justify-center text-sm text-emerald-400">Tüm veritabanı izlemeleri sağlıklı 🎉</div>;
  const catLabel: Record<string, string> = { down: "KAPALI", error: "HATA", slow: "YAVAŞ" };
  const catBadge: Record<string, string> = {
    down: "bg-rose-500/15 text-rose-400", error: "bg-orange-500/15 text-orange-400", slow: "bg-amber-500/15 text-amber-400",
  };
  return (
    <div className="h-full space-y-1.5 overflow-y-auto px-4 py-2">
      {problems.map((i) => {
        const meta = DB_METRIC_META[i.type];
        const cat = dbCat(i);
        return (
          <button key={i.id} type="button"
            onClick={() => onDrill({ source: "db_health", value: meta.platform, title: `${meta.platform} izlemeleri` })}
            className="flex w-full items-center justify-between gap-2 rounded-md border border-border/60 bg-muted/20 px-2.5 py-1.5 text-left text-xs transition-colors hover:bg-accent/50">
            <span className="min-w-0">
              <span className="block truncate font-medium">{i.name}</span>
              <span className="block truncate text-[10px] text-muted-foreground" title={i.lastError ?? undefined}>
                <span className={DB_PLATFORM_CLS[meta.platform]}>{meta.platform}</span> · {meta.short}
                {i.lastError ? ` — ${i.lastError}` : cat !== "down" && i.value != null ? ` — ${fmtDbValue(i.type, i.value)}` : ""}
              </span>
            </span>
            <span className={cn("shrink-0 rounded px-1.5 py-0.5 font-semibold", catBadge[cat])}>{catLabel[cat]}</span>
          </button>
        );
      })}
    </div>
  );
}
