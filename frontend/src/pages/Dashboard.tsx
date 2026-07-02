import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import { LayoutGrid, ChevronRight } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Select } from "@/components/ui/input";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { ServiceDetailDrawer } from "@/components/monitor/ServiceDetailDrawer";
import { DashboardCharts } from "@/components/monitor/DashboardCharts";
import { useAsync } from "@/hooks/useAsync";
import {
  type StatusService, type Board, type Cat, catOf, getStatus, getBoards,
} from "@/lib/monitor";
import { cn } from "@/lib/utils";

const cats: { key: Cat; label: string; num: string; ring: string; dot: string }[] = [
  { key: "all", label: "Toplam", num: "text-foreground", ring: "ring-border", dot: "bg-muted-foreground" },
  { key: "up", label: "Çalışan", num: "text-emerald-400", ring: "ring-emerald-500/40", dot: "bg-emerald-500" },
  { key: "slow", label: "Yavaş", num: "text-amber-400", ring: "ring-amber-500/40", dot: "bg-amber-500" },
  { key: "down", label: "Kapalı", num: "text-rose-400", ring: "ring-rose-500/40", dot: "bg-rose-500" },
  { key: "error", label: "Hata", num: "text-orange-400", ring: "ring-orange-500/40", dot: "bg-orange-500" },
];

const cardCls = "card-glow rounded-lg border border-border bg-gradient-to-b from-card to-card/60 shadow-[0_10px_30px_-14px_rgba(0,0,0,0.5)]";

const borderOf: Record<string, string> = {
  up: "border-l-emerald-500", slow: "border-l-amber-500", down: "border-l-rose-500", error: "border-l-orange-500",
};
const badgeOf: Record<string, { label: string; cls: string }> = {
  up: { label: "Çalışıyor", cls: "bg-emerald-500/15 text-emerald-400" },
  slow: { label: "Yavaş", cls: "bg-amber-500/15 text-amber-400" },
  down: { label: "Kapalı", cls: "bg-rose-500/15 text-rose-400" },
  error: { label: "Hata", cls: "bg-orange-500/15 text-orange-400" },
};

export function Dashboard() {
  const { data, loading, error, reload } = useAsync(getStatus, 20000);
  const [boards, setBoards] = useState<Board[]>([]);
  const [boardId, setBoardId] = useState<number | "all">("all");
  const [active, setActive] = useState<Cat>("all");
  const [detail, setDetail] = useState<StatusService | null>(null);

  useEffect(() => { getBoards().then(setBoards).catch(() => {}); }, []);

  const all = data?.services ?? [];
  const board = boards.find((b) => b.id === boardId);
  const inBoard = useMemo(
    () => (board ? all.filter((s) => board.serviceIds.includes(s.id)) : all),
    [all, board]
  );

  const counts = useMemo(() => {
    const c = { all: inBoard.length, up: 0, slow: 0, down: 0, error: 0 };
    for (const s of inBoard) c[catOf(s)]++;
    return c;
  }, [inBoard]);

  const visible = active === "all" ? inBoard : inBoard.filter((s) => catOf(s) === active);

  if (loading && !data) return <DashSkeleton />;
  if (error) return <ErrorState message={error} onRetry={reload} />;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          <LayoutGrid className="h-4 w-4" /> Pano:
        </div>
        <Select value={String(boardId)} onChange={(e) => { setBoardId(e.target.value === "all" ? "all" : Number(e.target.value)); setActive("all"); }} className="w-auto min-w-[200px]">
          <option value="all">Hepsi ({all.length})</option>
          {boards.map((b) => <option key={b.id} value={b.id}>{b.name} ({b.serviceIds.length})</option>)}
        </Select>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {cats.map((c, i) => {
          const val = counts[c.key as keyof typeof counts] ?? 0;
          const on = active === c.key;
          return (
            <motion.button
              key={c.key}
              onClick={() => setActive(c.key)}
              initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.4, delay: i * 0.05, ease: [0.16, 1, 0.3, 1] }}
              className={cn(
                cardCls, "p-4 text-center transition-all hover:-translate-y-0.5",
                on ? cn("ring-2", c.ring) : "ring-1 ring-transparent"
              )}
            >
              <div className={cn("text-3xl font-bold tabular-nums", c.num)}>{val}</div>
              <div className="mt-0.5 flex items-center justify-center gap-1.5 text-xs uppercase tracking-wide text-muted-foreground">
                <span className={cn("h-1.5 w-1.5 rounded-full", c.dot)} /> {c.label}
              </div>
            </motion.button>
          );
        })}
      </div>

      <DashboardCharts services={inBoard} />

      {visible.length === 0 ? (
        <EmptyState title={inBoard.length === 0 ? "Bu panoda servis yok" : "Bu kategoride servis yok"}
          hint={inBoard.length === 0 ? "Servisler ekranından ekle veya başka pano seç." : "Başka bir kategori seç."} />
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {visible.map((s) => {
            const c = catOf(s);
            return (
              <button key={s.id} onClick={() => setDetail(s)}
                className={cn(cardCls, "border-l-4 p-4 text-left transition-all hover:-translate-y-0.5", borderOf[c], !s.enabled && "opacity-50")}>
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="truncate font-semibold">{s.name}</div>
                    <div className="truncate font-mono text-xs text-muted-foreground">{s.target}{s.port ? `:${s.port}` : ""}</div>
                  </div>
                  <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                </div>
                <div className="mt-3 flex items-center justify-between">
                  <span className={cn("inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold", badgeOf[c].cls)}>{badgeOf[c].label}</span>
                  <span className="text-xs tabular-nums text-muted-foreground">{s.lastIsUp && s.lastResponseTimeMs != null ? `${s.lastResponseTimeMs} ms` : s.type}</span>
                </div>
                {(s.lastCpuPercent != null || s.lastRamPercent != null || s.lastMaxDiskPercent != null) && (
                  <div className="mt-3 grid grid-cols-3 gap-2 text-[11px] text-muted-foreground">
                    <ResourceMini label="CPU" v={s.lastCpuPercent} />
                    <ResourceMini label="RAM" v={s.lastRamPercent} />
                    <ResourceMini label="Disk" v={s.lastMaxDiskPercent} />
                  </div>
                )}
              </button>
            );
          })}
        </div>
      )}

      <ServiceDetailDrawer service={detail} onClose={() => setDetail(null)} onChanged={reload} />
    </div>
  );
}

function ResourceMini({ label, v }: { label: string; v: number | null }) {
  const pct = v ?? 0;
  const color = pct >= 85 ? "bg-rose-500" : pct >= 65 ? "bg-amber-500" : "bg-emerald-500";
  return (
    <div>
      <div className="mb-0.5 flex justify-between"><span>{label}</span><span className="tabular-nums">{v != null ? `${Math.round(v)}%` : "—"}</span></div>
      <div className="h-1 overflow-hidden rounded-full bg-muted"><div className={cn("h-full rounded-full", color)} style={{ width: `${pct}%` }} /></div>
    </div>
  );
}

function DashSkeleton() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        {Array.from({ length: 5 }).map((_, i) => <Card key={i} className="p-4"><Skeleton className="mx-auto h-8 w-12" /><Skeleton className="mx-auto mt-2 h-3 w-16" /></Card>)}
      </div>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {Array.from({ length: 8 }).map((_, i) => <Card key={i} className="p-4"><Skeleton className="h-5 w-32" /><Skeleton className="mt-2 h-3 w-40" /><Skeleton className="mt-3 h-5 w-full" /></Card>)}
      </div>
    </div>
  );
}
