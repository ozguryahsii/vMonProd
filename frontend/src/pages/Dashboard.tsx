import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useSearchParams } from "react-router-dom";
import { motion } from "framer-motion";
import { LayoutGrid, ChevronRight, Plus, Pencil, Trash2, RefreshCw, Search, X } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { ServiceDetailDrawer } from "@/components/monitor/ServiceDetailDrawer";
import { DashboardCharts } from "@/components/monitor/DashboardCharts";
import { DbHealthPanel } from "@/components/monitor/DbHealthPanel";
import { JobPanel } from "@/components/monitor/JobPanel";
import { BoardForm } from "@/components/monitor/BoardForm";
import { useAsync } from "@/hooks/useAsync";
import {
  type StatusService, type Board, type Cat, catOf, getStatus, getBoards, deleteBoard,
} from "@/lib/monitor";
import { checkIds, isDbHealthType, isJobType, fmtDbValue } from "@/lib/services";
import { useMe } from "@/hooks/useMe";
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
  const { me } = useMe();
  const [boards, setBoards] = useState<Board[]>([]);
  const [boardId, setBoardId] = useState<number | "all">("all");
  const [active, setActive] = useState<Cat>("all");
  const [detailId, setDetailId] = useState<number | null>(null);
  // Job kartı mini kutusundan gelen görev filtresi: drawer yalnız o görevin geçmişini çeker
  const [detailJob, setDetailJob] = useState<string | null>(null);
  const openDetail = (id: number, job: string | null = null) => { setDetailJob(job); setDetailId(id); };
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Board | null>(null);
  const [toDelete, setToDelete] = useState<Board | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [checking, setChecking] = useState(false);
  const [refreshedAt, setRefreshedAt] = useState<string | null>(null);

  const loadBoards = () => getBoards().then(setBoards).catch(() => {});
  useEffect(() => { loadBoards(); }, []);
  useEffect(() => { if (data) setRefreshedAt(new Date().toLocaleTimeString()); }, [data]);

  const all = data?.services ?? [];
  const board = boards.find((b) => b.id === boardId);
  const inBoard = useMemo(
    () => (board ? all.filter((s) => board.serviceIds.includes(s.id)) : all),
    [all, board]
  );

  // Üst arama çubuğu dashboard'dayken buraya ?q= yazar — sonuç yerinde süzülür (ad/hedef/açıklama/ekstra)
  const [params, setParams] = useSearchParams();
  const q = (params.get("q") ?? "").trim();
  const searched = useMemo(() => {
    if (!q) return inBoard;
    const norm = (x: string | null | undefined) => (x ?? "").toLocaleLowerCase("tr");
    const qq = norm(q);
    return inBoard.filter((s) =>
      norm(s.name).includes(qq) || norm(s.target).includes(qq) ||
      norm(s.description).includes(qq) || norm(s.extra).includes(qq));
  }, [inBoard, q]);

  const counts = useMemo(() => {
    const c = { all: searched.length, up: 0, slow: 0, down: 0, error: 0 };
    for (const s of searched) c[catOf(s)]++;
    return c;
  }, [searched]);

  const visible = active === "all" ? searched : searched.filter((s) => catOf(s) === active);
  // DB sağlık ve Zamanlanmış Görev izlemeleri kendi panellerinde gösterilir, genel ızgarada tekrarlanmaz
  const dbVisible = visible.filter((s) => isDbHealthType(s.type));
  const jobVisible = visible.filter((s) => isJobType(s.type));
  const gridVisible = visible.filter((s) => !isDbHealthType(s.type) && !isJobType(s.type));
  // Drawer id ile takip eder → 20sn oto-yenilemede içerik CANLI güncellenir (bayat snapshot yok)
  const detail = detailId != null ? all.find((s) => s.id === detailId) ?? null : null;

  async function checkVisible() {
    if (visible.length === 0) return;
    setChecking(true);
    try { await checkIds(visible.map((s) => s.id)); reload(); }
    catch { /* durum yenilenince görünür */ }
    finally { setChecking(false); }
  }
  async function doDeleteBoard() {
    if (!toDelete) return;
    setDeleting(true);
    try {
      await deleteBoard(toDelete.id);
      if (boardId === toDelete.id) setBoardId("all");
      setToDelete(null); loadBoards();
    } finally { setDeleting(false); }
  }

  if (loading && !data) return <DashSkeleton />;
  if (error) return <ErrorState message={error} onRetry={reload} />;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center gap-2 border-b border-border pb-2">
        <BoardTab active={boardId === "all"} onClick={() => { setBoardId("all"); setActive("all"); }}>
          <LayoutGrid className="h-3.5 w-3.5" /> Hepsi <span className="opacity-60">({all.length})</span>
        </BoardTab>
        {boards.map((b) => (
          <BoardTab key={b.id} active={boardId === b.id} onClick={() => { setBoardId(b.id); setActive("all"); }}>
            {b.name} <span className="opacity-60">({b.serviceIds.length})</span>
          </BoardTab>
        ))}
        <div className="ml-auto flex items-center gap-2">
          {refreshedAt && <span className="hidden text-xs text-muted-foreground sm:inline">Son yenileme: {refreshedAt}</span>}
          <Button variant="outline" size="sm" disabled={checking || visible.length === 0} onClick={checkVisible}>
            <RefreshCw className={cn("h-4 w-4", checking && "animate-spin")} /> Görünenleri Kontrol Et
          </Button>
          {board && (
            <>
              <Button variant="outline" size="sm" onClick={() => { setEditing(board); setFormOpen(true); }}><Pencil className="h-4 w-4" /> Düzenle</Button>
              <Button variant="destructive" size="sm" onClick={() => setToDelete(board)}><Trash2 className="h-4 w-4" /> Sil</Button>
            </>
          )}
          {/* Lisans limiti (Basic 5): limit dolunca buton kilitli — kullanıcı denemeye uğraşmasın */}
          {(() => {
            const maxDash = me?.license?.maxDashboards ?? null;
            const dashFull = maxDash != null && boards.length >= maxDash;
            return (
              <Button size="sm" disabled={dashFull}
                title={dashFull ? `Lisans limiti: ${me?.license?.edition} paket en fazla ${maxDash} dashboard destekler.` : undefined}
                onClick={() => { setEditing(null); setFormOpen(true); }}>
                <Plus className="h-4 w-4" /> Yeni Dashboard
              </Button>
            );
          })()}
        </div>
      </div>

      {q && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Search className="h-4 w-4 text-muted-foreground" />
          <span className="inline-flex items-center gap-1.5 rounded-lg bg-primary/15 px-2.5 py-1 font-medium text-primary">
            "{q}"
            <button onClick={() => setParams({}, { replace: true })} title="Temizle" className="rounded p-0.5 hover:bg-primary/20">
              <X className="h-3.5 w-3.5" />
            </button>
          </span>
          <span className="text-xs text-muted-foreground">{searched.length} sonuç</span>
        </div>
      )}

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

      {/* Grafikler seçili kategoriye göre süzülür (Yavaş'a basınca yalnız yavaşlar çizilir) */}
      <DashboardCharts services={visible} />

      {/* DB izlemeleri enstans (platform+host) kartlarında toplanır */}
      <DbHealthPanel services={dbVisible} onOpen={(id) => openDetail(id)} />

      {/* Zamanlanmış Görev setleri: kart başına görev mini kutuları (8+ → Tümünü göster popup'ı) */}
      <JobPanel services={jobVisible} onOpen={(id, job) => openDetail(id, job ?? null)} />

      {visible.length === 0 ? (
        <EmptyState title={q && searched.length === 0 ? "Aramayla eşleşen servis yok" : inBoard.length === 0 ? "Bu panoda servis yok" : "Bu kategoride servis yok"}
          hint={q && searched.length === 0 ? "Farklı bir arama dene veya aramayı temizle." : inBoard.length === 0 ? "Servisler ekranından ekle veya başka pano seç." : "Başka bir kategori seç."} />
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {gridVisible.map((s) => {
            const c = catOf(s);
            return (
              <button key={s.id} onClick={() => openDetail(s.id)}
                className={cn(cardCls, "border-l-4 p-4 text-left transition-all hover:-translate-y-0.5", borderOf[c], !s.enabled && "opacity-50")}>
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <div className="truncate font-semibold">{s.name}</div>
                    <div className="truncate font-mono text-xs text-muted-foreground">{s.target}{s.port ? `:${s.port}` : ""}</div>
                    {s.description && (
                      <div className="mt-0.5 truncate text-[11px] text-muted-foreground/90" title={s.description}>{s.description}</div>
                    )}
                  </div>
                  <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                </div>
                <div className="mt-3 flex items-center justify-between">
                  {c === "down" || c === "error" ? (
                    // Eski tasarımdaki gibi: sorunlu servis BÜYÜK kırmızı/turuncu punto + nabız
                    <span className={cn("animate-pulse text-base font-extrabold uppercase tracking-wide",
                      c === "down" ? "text-rose-500" : "text-orange-400")}>
                      {badgeOf[c].label}
                      {c === "down" && s.downSince && <span className="ml-1.5 text-xs font-semibold normal-case opacity-80">· {since(s.downSince)}</span>}
                    </span>
                  ) : (
                    <span className={cn("inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold", badgeOf[c].cls)}>
                      {badgeOf[c].label}
                    </span>
                  )}
                  <span className="text-xs tabular-nums text-muted-foreground">{s.lastIsUp && s.lastResponseTimeMs != null ? fmtDbValue(s.type, s.lastResponseTimeMs) : s.type}</span>
                </div>
                {(s.lastCpuPercent != null || s.lastRamPercent != null || s.lastMaxDiskPercent != null) && (
                  <div className="mt-3 grid grid-cols-3 gap-2 text-[11px] text-muted-foreground">
                    <ResourceMini label="CPU" v={s.lastCpuPercent} />
                    <ResourceMini label="RAM" v={s.lastRamPercent} />
                    <ResourceMini label="Disk" v={s.lastMaxDiskPercent} />
                  </div>
                )}
                {s.capacityInfo && (
                  <div className="mt-2 truncate text-[10px] text-muted-foreground/80" title={s.capacityInfo}>
                    {s.capacityInfo}
                  </div>
                )}
              </button>
            );
          })}
        </div>
      )}

      <ServiceDetailDrawer service={detail} jobFilter={detailJob}
        onClose={() => { setDetailId(null); setDetailJob(null); }} onChanged={reload} />
      <BoardForm open={formOpen} board={editing} onClose={() => setFormOpen(false)} onSaved={loadBoards} />
      <ConfirmDialog
        open={!!toDelete}
        title="Panoyu sil"
        message={toDelete ? `"${toDelete.name}" panosu silinecek (servisler silinmez). Emin misiniz?` : ""}
        loading={deleting}
        onConfirm={doDeleteBoard}
        onCancel={() => setToDelete(null)}
      />
    </div>
  );
}

/** "3 sa 12 dk" biçiminde süre */
function since(iso: string): string {
  const mins = Math.max(1, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
  if (mins < 60) return `${mins} dk`;
  const h = Math.floor(mins / 60);
  return h < 24 ? `${h} sa ${mins % 60} dk` : `${Math.floor(h / 24)} gün ${h % 24} sa`;
}

function BoardTab({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors",
        active
          ? "bg-primary/15 text-primary ring-1 ring-inset ring-primary/30"
          : "text-muted-foreground hover:bg-accent/60 hover:text-foreground"
      )}
    >
      {children}
    </button>
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
