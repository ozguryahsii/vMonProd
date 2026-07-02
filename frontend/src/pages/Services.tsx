import { useEffect, useMemo, useState } from "react";
import {
  Plus, Search, RefreshCw, Pencil, Trash2, Play, Square, RotateCw, CheckCircle2, XCircle,
} from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Select } from "@/components/ui/input";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { ServiceForm } from "@/components/services/ServiceForm";
import { useAsync } from "@/hooks/useAsync";
import {
  type ServiceItem, type ServicesMeta, statusOf, CONTROL_TYPES,
  listServices, servicesMeta, deleteService, checkService, serviceAction,
} from "@/lib/services";
import { cn } from "@/lib/utils";

const badge: Record<string, { label: string; cls: string; dot: string }> = {
  up: { label: "Çalışıyor", cls: "bg-emerald-500/15 text-emerald-400", dot: "bg-emerald-500" },
  slow: { label: "Yavaş", cls: "bg-amber-500/15 text-amber-400", dot: "bg-amber-500" },
  down: { label: "Kapalı", cls: "bg-rose-500/15 text-rose-400", dot: "bg-rose-500" },
  error: { label: "Hata", cls: "bg-orange-500/15 text-orange-400", dot: "bg-orange-500" },
};

export function Services() {
  const { data, loading, error, reload } = useAsync(listServices, 30000);
  const [meta, setMeta] = useState<ServicesMeta | null>(null);
  const [q, setQ] = useState("");
  const [typeF, setTypeF] = useState("");
  const [statusF, setStatusF] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<ServiceItem | null>(null);
  const [toDelete, setToDelete] = useState<ServiceItem | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [flash, setFlash] = useState<{ ok: boolean; msg: string } | null>(null);

  useEffect(() => { servicesMeta().then(setMeta).catch(() => {}); }, []);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 3500); return () => clearTimeout(t); } }, [flash]);

  const filtered = useMemo(() => {
    const items = data ?? [];
    return items.filter((s) => {
      if (typeF && s.type !== typeF) return false;
      if (statusF && statusOf(s) !== statusF) return false;
      if (q) {
        const t = q.toLowerCase();
        if (!s.name.toLowerCase().includes(t) && !s.target.toLowerCase().includes(t) &&
            !(s.keyword ?? "").toLowerCase().includes(t)) return false;
      }
      return true;
    });
  }, [data, q, typeF, statusF]);

  async function doCheck(s: ServiceItem) {
    setBusyId(s.id);
    try { const r = await checkService(s.id); setFlash({ ok: r.isUp, msg: `${s.name}: ${r.isUp ? "çalışıyor" : "kapalı"}${r.responseTimeMs ? ` (${r.responseTimeMs} ms)` : ""}` }); reload(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusyId(null); }
  }
  async function doAction(s: ServiceItem, action: "start" | "stop" | "restart") {
    setBusyId(s.id);
    try { const r = await serviceAction(s.id, action); setFlash({ ok: r.ok, msg: `${s.name}: ${r.message}` }); reload(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusyId(null); }
  }
  async function doDelete() {
    if (!toDelete) return;
    setDeleting(true);
    try { await deleteService(toDelete.id); setFlash({ ok: true, msg: `${toDelete.name} silindi.` }); setToDelete(null); reload(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setDeleting(false); }
  }

  if (loading && !data) return <ListSkeleton />;
  if (error) return <ErrorState message={error} onRetry={reload} />;

  const types = meta?.types ?? [];

  return (
    <div className="space-y-4">
      {flash && (
        <div className={cn("flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm",
          flash.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
          {flash.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {flash.msg}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-[220px] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ad, hedef veya etiket ara…" className="pl-9" />
        </div>
        <Select value={typeF} onChange={(e) => setTypeF(e.target.value)} className="w-auto">
          <option value="">Tüm tipler</option>
          {types.map((t) => <option key={t} value={t}>{t}</option>)}
        </Select>
        <Select value={statusF} onChange={(e) => setStatusF(e.target.value)} className="w-auto">
          <option value="">Tüm durumlar</option>
          <option value="up">Çalışıyor</option>
          <option value="slow">Yavaş</option>
          <option value="down">Kapalı</option>
          <option value="error">Hata</option>
        </Select>
        <Button size="sm" onClick={() => { setEditing(null); setFormOpen(true); }}>
          <Plus className="h-4 w-4" /> Yeni Servis
        </Button>
      </div>

      <Card>
        <CardContent className="px-0 py-0">
          {filtered.length === 0 ? (
            <EmptyState title={data && data.length > 0 ? "Eşleşen servis yok" : "Henüz servis yok"}
              hint={data && data.length > 0 ? "Filtreleri değiştirmeyi dene." : "Yeni Servis ile ilk servisini ekle."} />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                    <th className="px-5 py-3 font-semibold">Servis</th>
                    <th className="px-5 py-3 font-semibold">Tip</th>
                    <th className="px-5 py-3 font-semibold">Hedef</th>
                    <th className="px-5 py-3 font-semibold">Durum</th>
                    <th className="px-5 py-3 font-semibold">Yanıt</th>
                    <th className="px-5 py-3 text-right font-semibold">İşlemler</th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((s) => {
                    const st = badge[statusOf(s)];
                    const busy = busyId === s.id;
                    const control = CONTROL_TYPES.includes(s.type);
                    return (
                      <tr key={s.id} className={cn("border-b border-border/60 transition-colors hover:bg-accent/40", !s.enabled && "opacity-50")}>
                        <td className="px-5 py-3">
                          <div className="font-medium">{s.name}</div>
                          {s.keyword && <div className="text-xs text-muted-foreground">{s.keyword}</div>}
                        </td>
                        <td className="px-5 py-3 text-muted-foreground">{s.type}</td>
                        <td className="px-5 py-3 font-mono text-xs text-muted-foreground">{s.target}{s.port ? `:${s.port}` : ""}</td>
                        <td className="px-5 py-3">
                          <span className={cn("inline-flex items-center gap-1.5 rounded-md px-2 py-0.5 text-xs font-semibold", st.cls)}>
                            <span className={cn("h-1.5 w-1.5 rounded-full", st.dot)} /> {st.label}
                          </span>
                        </td>
                        <td className="px-5 py-3 tabular-nums text-muted-foreground">{s.lastIsUp === true && s.lastResponseTimeMs != null ? `${s.lastResponseTimeMs} ms` : "—"}</td>
                        <td className="px-5 py-3">
                          <div className="flex items-center justify-end gap-1">
                            <Button variant="ghost" size="icon" className="h-8 w-8" title="Şimdi kontrol et" disabled={busy} onClick={() => doCheck(s)}>
                              <RefreshCw className={cn("h-4 w-4", busy && "animate-spin")} />
                            </Button>
                            {control && (
                              <>
                                <Button variant="ghost" size="icon" className="h-8 w-8 text-emerald-400" title="Başlat" disabled={busy} onClick={() => doAction(s, "start")}><Play className="h-4 w-4" /></Button>
                                <Button variant="ghost" size="icon" className="h-8 w-8 text-rose-400" title="Durdur" disabled={busy} onClick={() => doAction(s, "stop")}><Square className="h-4 w-4" /></Button>
                                <Button variant="ghost" size="icon" className="h-8 w-8 text-amber-400" title="Yeniden başlat" disabled={busy} onClick={() => doAction(s, "restart")}><RotateCw className="h-4 w-4" /></Button>
                              </>
                            )}
                            <Button variant="ghost" size="icon" className="h-8 w-8" title="Düzenle" onClick={() => { setEditing(s); setFormOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                            <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive" title="Sil" onClick={() => setToDelete(s)}><Trash2 className="h-4 w-4" /></Button>
                          </div>
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

      <div className="text-xs text-muted-foreground">{filtered.length} / {data?.length ?? 0} servis</div>

      <ServiceForm open={formOpen} service={editing} meta={meta} onClose={() => setFormOpen(false)} onSaved={(m) => { setFlash({ ok: true, msg: m }); reload(); }} />
      <ConfirmDialog
        open={!!toDelete}
        title="Servisi sil"
        message={toDelete ? `"${toDelete.name}" ve tüm geçmiş verisi kalıcı olarak silinecek. Emin misiniz?` : ""}
        loading={deleting}
        onConfirm={doDelete}
        onCancel={() => setToDelete(null)}
      />
    </div>
  );
}

function ListSkeleton() {
  return (
    <div className="space-y-4">
      <div className="flex gap-3">
        <Skeleton className="h-10 flex-1" />
        <Skeleton className="h-10 w-32" />
        <Skeleton className="h-10 w-32" />
      </div>
      <Card className="p-5 space-y-3">
        {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-10 w-full" />)}
      </Card>
    </div>
  );
}
