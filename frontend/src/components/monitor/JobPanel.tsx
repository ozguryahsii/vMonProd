import { useMemo, useState } from "react";
import { CalendarClock, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/input";
import { type StatusService, catOf } from "@/lib/monitor";
import { isJobType, parseJobStates, jobShortName, fmtJobRun, type JobBoxState } from "@/lib/services";
import { cn } from "@/lib/utils";

/** Zamanlanmış Görevler paneli (Veritabanı Sağlığı kartlarıyla aynı dil):
 *  her izleme (görev seti) BİR KART; içinde görev başına MİNİ KUTU (en çok 8).
 *  Mini kutuya tıkla → o görevin geçmişi; kart başlığına tıkla → tüm setin toplu görünümü.
 *  8'den fazlaysa "Tümünü göster" → sıralanabilir, kaydırmalı popup (tüm görevler kutu olarak). */
export function JobPanel({ services, onOpen }: {
  services: StatusService[];
  onOpen: (id: number, job?: string | null) => void;
}) {
  const jobs = services.filter((s) => isJobType(s.type));
  const [popupId, setPopupId] = useState<number | null>(null);
  if (jobs.length === 0) return null;
  const popupSvc = popupId != null ? jobs.find((s) => s.id === popupId) ?? null : null;

  return (
    <div>
      <div className="mb-2 flex items-center gap-2">
        <CalendarClock className="h-4 w-4 text-muted-foreground" />
        <h3 className="text-sm font-semibold">Zamanlanmış Görevler</h3>
        <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-semibold text-muted-foreground">{jobs.length} izleme</span>
      </div>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
        {jobs.map((s) => {
          const states = parseJobStates(s.lastJobStates);
          const cat = catOf(s);
          const dot = cat === "up" ? "bg-emerald-500" : cat === "down" ? "bg-rose-500" : cat === "error" ? "bg-orange-500" : "bg-amber-500";
          return (
            <div key={s.id} className="card-glow rounded-lg border border-border bg-gradient-to-b from-card to-card/60 p-3 shadow-[0_10px_30px_-14px_rgba(0,0,0,0.5)]">
              {/* Kart başlığı: izleme grubunun toplu görünümü (mevcut drawer) */}
              <button type="button" onClick={() => onOpen(s.id, null)}
                className="mb-2 flex w-full cursor-pointer items-center gap-2 rounded-md px-1 py-0.5 text-left transition-colors hover:bg-accent/40">
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-semibold hover:text-primary">{s.name}</span>
                  <span className="block truncate font-mono text-[10px] text-muted-foreground">{s.target}</span>
                </span>
                <span className={cn("h-2.5 w-2.5 shrink-0 rounded-full", dot)} />
              </button>

              {states.length === 0 ? (
                <p className="px-1 py-2 text-xs text-muted-foreground">Henüz kontrol edilmedi — görev durumları ilk kontrolde gelir.</p>
              ) : (
                <>
                  <div className="grid grid-cols-2 gap-1.5">
                    {states.slice(0, 8).map((j) => (
                      <JobBox key={j.name} j={j} onClick={() => onOpen(s.id, j.name)} />
                    ))}
                  </div>
                  {states.length > 8 && (
                    <Button type="button" variant="ghost" size="sm" className="mt-1.5 w-full text-xs"
                      onClick={() => setPopupId(s.id)}>
                      Tümünü göster ({states.length})
                    </Button>
                  )}
                </>
              )}
            </div>
          );
        })}
      </div>

      {popupSvc && (
        <JobPopup svc={popupSvc} onClose={() => setPopupId(null)}
          onPick={(job) => { setPopupId(null); onOpen(popupSvc.id, job); }} />
      )}
    </div>
  );
}

const ST_META: Record<JobBoxState["st"], { label: string | null; cls: string; box: string }> = {
  ok:   { label: null,          cls: "text-foreground",  box: "border-border/60 bg-muted/20 hover:border-primary/50" },
  fail: { label: "BAŞARISIZ",   cls: "text-rose-400",    box: "border-rose-500/40 bg-rose-500/10 hover:border-rose-400" },
  nf:   { label: "BULUNAMADI",  cls: "text-rose-400",    box: "border-rose-500/40 bg-rose-500/10 hover:border-rose-400" },
  dis:  { label: "DEVRE DIŞI",  cls: "text-amber-400",   box: "border-amber-500/40 bg-amber-500/10 hover:border-amber-400" },
  sil:  { label: "SESSİZ",      cls: "text-amber-400",   box: "border-amber-500/40 bg-amber-500/10 hover:border-amber-400" },
};

/** Mini kutu — TÜM tiplerde aynı içerik: durum (sorunluysa) + son koşu tarihi (gg.aa.yyyy ss:dd:ss)
 *  + süre (biliniyorsa "· N sn"; Windows Task/MySQL Event süre tutmaz → yalnız tarih). */
function JobBox({ j, onClick }: { j: JobBoxState; onClick: () => void }) {
  const m = ST_META[j.st];
  return (
    <button type="button" onClick={onClick} title={j.name}
      className={cn("cursor-pointer rounded-md border px-2 py-1.5 text-left transition-colors", m.box)}>
      {/* Kullanıcı isteği: görev adı KALIN, tarih/süre satırı İNCE — okunabilirlik */}
      <span className="block truncate text-[11px] font-bold uppercase tracking-wide">{jobShortName(j.name)}</span>
      {m.label && <span className={cn("block truncate text-xs font-semibold", m.cls)}>{m.label}</span>}
      <span className="block truncate text-[10px] font-normal tabular-nums text-muted-foreground">
        {fmtJobRun(j.lastRun)}{j.durSec != null ? ` · ${j.durSec} sn` : ""}
      </span>
    </button>
  );
}

const SEV: Record<JobBoxState["st"], number> = { fail: 0, nf: 1, dis: 2, sil: 3, ok: 4 };

/** Tüm görevler popup'ı: sıralanabilir (Ada göre / Duruma göre), kaydırmalı; kutuya tıkla → görev geçmişi. */
function JobPopup({ svc, onClose, onPick }: {
  svc: StatusService;
  onClose: () => void;
  onPick: (job: string) => void;
}) {
  const [sort, setSort] = useState<"az" | "status">("status");
  const states = useMemo(() => {
    const list = parseJobStates(svc.lastJobStates).slice();
    list.sort((a, b) => sort === "az"
      ? a.name.localeCompare(b.name, "tr")
      : (SEV[a.st] - SEV[b.st]) || a.name.localeCompare(b.name, "tr"));
    return list;
  }, [svc.lastJobStates, sort]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" onClick={onClose}>
      <div className="flex max-h-[85vh] w-full max-w-4xl flex-col rounded-xl border border-border bg-card shadow-2xl"
        onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center gap-3 border-b border-border px-4 py-3">
          <div className="min-w-0 flex-1">
            <h3 className="truncate text-sm font-semibold">{svc.name}</h3>
            <p className="truncate text-xs text-muted-foreground">{states.length}{" görev"} · {svc.target}</p>
          </div>
          <Select value={sort} onChange={(e) => setSort(e.target.value as "az" | "status")} className="w-auto">
            <option value="status">Duruma göre</option>
            <option value="az">Ada göre (A-Z)</option>
          </Select>
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={onClose} title="Kapat">
            <X className="h-4 w-4" />
          </Button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto p-4">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
            {states.map((j) => (
              <JobBox key={j.name} j={j} onClick={() => onPick(j.name)} />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
