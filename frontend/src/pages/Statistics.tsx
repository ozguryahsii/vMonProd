import { useEffect, useMemo, useState } from "react";
import { Lock, LockOpen, Plus, RotateCcw, Save, CheckCircle2, XCircle, FileText, Maximize2, Download } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/input";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { WidgetBoard } from "@/components/stats/WidgetBoard";
import { WIDGET_CATALOG } from "@/components/stats/widgets";
import { StatDetailDrawer, type Drill } from "@/components/stats/StatDetailDrawer";
import { useAsync } from "@/hooks/useAsync";
import {
  getStats, getStatWidgets, saveStatLayout, resetStatLayout, type StatWidgetDef,
} from "@/lib/stats";

let tempId = -1; // yeni widget'lar için geçici negatif id (SaveLayout id<=0'ı yeni sayar)

export function Statistics() {
  const { data, loading, error, reload } = useAsync(getStats, 30000);
  const [widgets, setWidgets] = useState<StatWidgetDef[] | null>(null);
  const [canEdit, setCanEdit] = useState(false);
  const [editing, setEditing] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [palette, setPalette] = useState("");
  const [flash, setFlash] = useState<{ ok: boolean; msg: string } | null>(null);
  const [werr, setWerr] = useState<string | null>(null);
  const [drill, setDrill] = useState<Drill | null>(null);

  useEffect(() => {
    getStatWidgets()
      .then((r) => { setWidgets(r.widgets); setCanEdit(r.canEdit); })
      .catch((e) => setWerr((e as Error).message));
  }, []);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 3000); return () => clearTimeout(t); } }, [flash]);

  const groups = useMemo(() => {
    const g = new Map<string, typeof WIDGET_CATALOG>();
    for (const c of WIDGET_CATALOG) { if (!g.has(c.group)) g.set(c.group, []); g.get(c.group)!.push(c); }
    return g;
  }, []);

  function change(w: StatWidgetDef[]) { setWidgets(w); setDirty(true); }
  function remove(id: number) { change((widgets ?? []).filter((w) => w.id !== id)); }
  function add() {
    if (!palette || !widgets) return;
    const [type, source] = palette.split("|");
    const c = WIDGET_CATALOG.find((x) => x.type === type && x.source === source);
    if (!c) return;
    const maxY = Math.max(0, ...widgets.map((w) => w.y + w.h));
    change([...widgets, { id: tempId--, type, source, title: null, configJson: null, x: 0, y: maxY, w: c.w, h: c.h }]);
  }
  async function save() {
    if (!widgets) return;
    setSaving(true);
    try {
      await saveStatLayout(widgets.map((w) => ({ ...w, id: w.id < 0 ? 0 : w.id })));
      const r = await getStatWidgets();      // gerçek id'leri geri al
      setWidgets(r.widgets);
      setDirty(false); setEditing(false);
      setFlash({ ok: true, msg: "Düzen kaydedildi." });
    } catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setSaving(false); }
  }
  async function doReset() {
    setSaving(true);
    try {
      await resetStatLayout();
      const r = await getStatWidgets();
      setWidgets(r.widgets);
      setDirty(false); setResetOpen(false);
      setFlash({ ok: true, msg: "Varsayılan düzene dönüldü." });
    } catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setSaving(false); }
  }

  if ((loading && !data) || (!widgets && !werr)) return <StatsSkeleton />;
  if (error) return <ErrorState message={error} onRetry={reload} />;
  if (werr) return <ErrorState message={werr} />;
  if (!data || !widgets) return null;
  if (data.counts.total === 0)
    return <EmptyState title="Sağlık verisi olan sunucu yok" hint="İstatistikler yalnız Windows/Linux Sağlık tipiyle izlenen sunuculardan gelir." />;

  return (
    <div className="space-y-4">
      {flash && (
        <div className={`flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm ${flash.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive"}`}>
          {flash.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {flash.msg}
        </div>
      )}

      {/* Sayfa araçları (eski 2.15/2.16 paritesi): PDF raporu, tam ekran (NOC), özet CSV, son güncelleme */}
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span>Son güncelleme: {new Date(data.lastUpdated).toLocaleTimeString()}</span>
        <div className="ml-auto flex gap-2">
          <Button variant="outline" size="sm" onClick={() => exportSummaryCsv(data)}><Download className="h-4 w-4" /> Özet CSV</Button>
          <a href="/Statistics/Report" target="_blank" rel="noreferrer">
            <Button variant="outline" size="sm"><FileText className="h-4 w-4" /> PDF Raporu</Button>
          </a>
          <Button variant="outline" size="sm" onClick={() => {
            if (document.fullscreenElement) document.exitFullscreen();
            else document.documentElement.requestFullscreen().catch(() => {});
          }}><Maximize2 className="h-4 w-4" /> Tam Ekran</Button>
        </div>
      </div>

      {canEdit && (
        <div className="flex flex-wrap items-center gap-2">
          {editing && (
            <>
              <Select value={palette} onChange={(e) => setPalette(e.target.value)} className="h-9 w-auto min-w-[220px]">
                <option value="">Widget seç…</option>
                {Array.from(groups.entries()).map(([g, items]) => (
                  <optgroup key={g} label={g}>
                    {items.map((c) => <option key={`${c.type}|${c.source}`} value={`${c.type}|${c.source}`}>{c.label}</option>)}
                  </optgroup>
                ))}
              </Select>
              <Button variant="outline" size="sm" onClick={add} disabled={!palette}><Plus className="h-4 w-4" /> Ekle</Button>
              <Button variant="outline" size="sm" onClick={() => setResetOpen(true)} disabled={saving}><RotateCcw className="h-4 w-4" /> Varsayılana dön</Button>
              <Button size="sm" onClick={save} disabled={saving || !dirty}><Save className="h-4 w-4" /> {saving ? "Kaydediliyor…" : "Kaydet"}</Button>
            </>
          )}
          <div className="ml-auto">
            <Button variant={editing ? "default" : "outline"} size="sm" onClick={() => setEditing((e) => !e)}>
              {editing ? <LockOpen className="h-4 w-4" /> : <Lock className="h-4 w-4" />} {editing ? "Düzenleme açık" : "Düzenle"}
            </Button>
          </div>
        </div>
      )}

      <WidgetBoard widgets={widgets} data={data} editing={editing} onChange={change} onRemove={remove}
        onDrill={(d) => { if (!editing) setDrill(d); }} />

      <StatDetailDrawer drill={drill} onClose={() => setDrill(null)} />

      <ConfirmDialog
        open={resetOpen}
        title="Varsayılana dön"
        message="Tüm widget düzeni silinip varsayılan yerleşim yüklenecek. Emin misiniz?"
        confirmLabel="Sıfırla"
        loading={saving}
        onConfirm={doReset}
        onCancel={() => setResetOpen(false)}
      />
    </div>
  );
}

/** Özet CSV (eski 2.15.0 'summary CSV export' paritesi) — mevcut istatistik verisinden. */
function exportSummaryCsv(d: import("@/lib/stats").StatsData) {
  const esc = (v: unknown) => `"${String(v ?? "").replace(/"/g, '""')}"`;
  const L: string[] = [];
  L.push("Bolum;Anahtar;Deger");
  L.push(`Sayac;Toplam;${d.counts.total}`); L.push(`Sayac;Calisan;${d.counts.up}`);
  L.push(`Sayac;Kapali;${d.counts.down}`); L.push(`Sayac;Hata;${d.counts.error}`);
  L.push(`Kaynak;CPU kullanilan/atanan;${d.cpu.used}/${d.cpu.alloc} ${d.cpu.unit}`);
  L.push(`Kaynak;RAM kullanilan/atanan;${d.ram.used}/${d.ram.alloc} ${d.ram.unit}`);
  L.push(`Kaynak;Disk kullanilan/atanan;${d.disk.used}/${d.disk.alloc} ${d.disk.unit}`);
  L.push(`Ortalama;CPU %;${d.avg.cpu ?? ""}`); L.push(`Ortalama;RAM %;${d.avg.ram ?? ""}`); L.push(`Ortalama;Disk %;${d.avg.disk ?? ""}`);
  L.push(`Uptime;24 saat %;${d.uptime.h24}`); L.push(`Uptime;7 gun %;${d.uptime.d7}`);
  L.push(`Kesinti;Adet (7g);${d.outages.count}`); L.push(`Kesinti;Dakika (7g);${d.outages.minutes}`);
  for (const x of d.osKind) L.push(`OS;${esc(x.name)};${x.value}`);
  for (const x of d.top.cpu) L.push(`TopCPU;${esc(x.name)};${x.value}`);
  for (const x of d.top.ram) L.push(`TopRAM;${esc(x.name)};${x.value}`);
  for (const x of d.top.disk) L.push(`TopDisk;${esc(x.name)};${x.value}`);
  const blob = new Blob(["﻿" + L.join("\r\n")], { type: "text/csv;charset=utf-8" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = `vmon-istatistik-ozet_${new Date().toISOString().slice(0, 10)}.csv`;
  a.click();
  URL.revokeObjectURL(a.href);
}

function StatsSkeleton() {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => <Card key={i} className="p-5"><Skeleton className="mx-auto h-9 w-16" /></Card>)}
      </div>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        {Array.from({ length: 3 }).map((_, i) => <Card key={i} className="p-5"><Skeleton className="h-16 w-full" /></Card>)}
      </div>
      <Card className="p-5"><Skeleton className="h-[300px] w-full" /></Card>
    </div>
  );
}
