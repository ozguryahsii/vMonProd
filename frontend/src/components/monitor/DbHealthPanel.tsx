import { useMemo } from "react";
import { motion } from "framer-motion";
import { Database, Clock3, Users, Lock, Timer, HardDrive, Gauge, GitBranch } from "lucide-react";
import { type StatusService, catOf } from "@/lib/monitor";
import { DB_METRIC_META, DB_PLATFORM_CLS, fmtDbValue, type DbPlatform } from "@/lib/services";
import { cn } from "@/lib/utils";

/* ================= DB İzleme Fazı: Dashboard "Veritabanı Sağlığı" paneli =================
   Aynı sunucuya (platform + host:port) bağlı DB metrik izlemeleri TEK enstans kartında toplanır:
   başlıkta platform + host + genel durum, içeride metrik kutucukları (tıkla → detay drawer). */

const METRIC_ORDER = ["clock", "active", "blocked", "long", "status", "usage", "repl"] as const;

const METRIC_ICON: Record<string, typeof Clock3> = {
  clock: Clock3, active: Users, blocked: Lock, long: Timer, status: HardDrive, usage: Gauge, repl: GitBranch,
};

const PLATFORM_BADGE: Record<DbPlatform, string> = {
  Oracle: "bg-red-500/10 ring-red-500/30",
  MSSQL: "bg-sky-500/10 ring-sky-500/30",
  MySQL: "bg-teal-500/10 ring-teal-500/30",
};

/** Checker hata mesajından sapma saniyesini ayıklar: "DB saati sapması: 123 sn (...)" */
export function parseDrift(err: string | null): number | null {
  const m = err?.match(/sapması:\s*(\d+)\s*sn/);
  return m ? Number(m[1]) : null;
}

const catText: Record<string, string> = {
  up: "text-emerald-400", slow: "text-amber-400", down: "text-rose-400", error: "text-orange-400",
};
const catDot: Record<string, string> = {
  up: "bg-emerald-500", slow: "bg-amber-500", down: "bg-rose-500", error: "bg-orange-500",
};

interface Instance {
  key: string;
  platform: DbPlatform;
  host: string;
  services: StatusService[];
}

export function DbHealthPanel({ services, onOpen }: { services: StatusService[]; onOpen: (id: number) => void }) {
  const instances = useMemo<Instance[]>(() => {
    const map = new Map<string, Instance>();
    for (const s of services) {
      const meta = DB_METRIC_META[s.type];
      if (!meta) continue;
      const host = `${s.target}${s.port ? `:${s.port}` : ""}`;
      const key = `${meta.platform}|${host}`;
      let inst = map.get(key);
      if (!inst) { inst = { key, platform: meta.platform, host, services: [] }; map.set(key, inst); }
      inst.services.push(s);
    }
    // Metrikler sabit sırada (saat → aktif → bloklu → uzun → durum → doluluk → replikasyon)
    for (const inst of map.values())
      inst.services.sort((a, b) =>
        METRIC_ORDER.indexOf(DB_METRIC_META[a.type].metric) - METRIC_ORDER.indexOf(DB_METRIC_META[b.type].metric));
    return Array.from(map.values()).sort((a, b) => a.platform.localeCompare(b.platform) || a.host.localeCompare(b.host));
  }, [services]);

  if (instances.length === 0) return null;

  return (
    <div>
      <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-muted-foreground">
        <Database className="h-4 w-4" /> Veritabanı Sağlığı
        <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] tabular-nums">{instances.length} sunucu</span>
      </div>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-3">
        {instances.map((inst, i) => {
          // Enstansın genel durumu = en kötü metrik durumu (down > error > slow > up)
          const cats = inst.services.map(catOf);
          const worst = cats.includes("down") ? "down" : cats.includes("error") ? "error" : cats.includes("slow") ? "slow" : "up";
          return (
            <motion.div
              key={inst.key}
              initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.4, delay: i * 0.05, ease: [0.16, 1, 0.3, 1] }}
              className="card-glow rounded-lg border border-border bg-gradient-to-b from-card to-card/60 p-4 shadow-[0_10px_30px_-14px_rgba(0,0,0,0.5)]"
            >
              <div className="flex items-center gap-2.5">
                <span className={cn("flex h-8 w-8 items-center justify-center rounded-lg ring-1", PLATFORM_BADGE[inst.platform])}>
                  <Database className={cn("h-4 w-4", DB_PLATFORM_CLS[inst.platform])} />
                </span>
                <div className="min-w-0">
                  <div className={cn("text-sm font-semibold", DB_PLATFORM_CLS[inst.platform])}>{inst.platform}</div>
                  <div className="truncate font-mono text-xs text-muted-foreground">{inst.host}</div>
                </div>
                <span className={cn("ml-auto h-2.5 w-2.5 shrink-0 rounded-full", catDot[worst], worst !== "up" && "animate-pulse")} />
              </div>

              <div className="mt-3 grid grid-cols-2 gap-2">
                {inst.services.map((s) => {
                  const meta = DB_METRIC_META[s.type];
                  const c = catOf(s);
                  const Icon = METRIC_ICON[meta.metric] ?? Database;
                  const v = s.lastResponseTimeMs;
                  const pct = meta.unit === "%" ? Math.max(0, Math.min(100, v ?? 0)) : null;
                  const drift = meta.metric === "clock" ? parseDrift(s.lastError) : null;
                  return (
                    <button
                      key={s.id}
                      onClick={() => onOpen(s.id)}
                      title={s.lastError ?? s.name}
                      className={cn(
                        "rounded-md border border-border/70 bg-background/40 p-2 text-left transition-all hover:-translate-y-0.5 hover:border-border",
                        c === "down" && "border-rose-500/40 bg-rose-500/5",
                        c === "error" && "border-orange-500/40 bg-orange-500/5",
                        c === "slow" && "border-amber-500/40 bg-amber-500/5",
                      )}
                    >
                      <div className="flex items-center gap-1 text-[10px] uppercase tracking-wide text-muted-foreground">
                        <Icon className="h-3 w-3 shrink-0" />
                        <span className="truncate">{meta.short}</span>
                      </div>
                      {meta.metric === "clock" ? (
                        // Saat kutusu: DURUM büyük punto, yanıt süresi altta küçük gri (+ sapma oranı)
                        <>
                          <div className={cn("mt-1 text-sm font-extrabold uppercase leading-none tracking-wide",
                            c === "down" ? "text-rose-400" : c === "error" ? "text-orange-400" : "text-emerald-400")}>
                            {c === "down" ? "KAPALI" : c === "error" ? "saat sapması!" : "saat senkron"}
                          </div>
                          <div className="mt-1 text-[10px] tabular-nums text-muted-foreground">
                            {c === "down" ? "—" : `${v ?? "—"} ms${drift != null ? ` · sapma: ${drift} sn` : ""}`}
                          </div>
                        </>
                      ) : (
                        <div className={cn("mt-1 text-lg font-bold leading-none tabular-nums", c === "up" ? "text-foreground" : catText[c])}>
                          {c === "down" ? "KAPALI" : fmtDbValue(s.type, v)}
                        </div>
                      )}
                      {pct != null && c !== "down" && (
                        <div className="mt-1.5 h-1 overflow-hidden rounded-full bg-muted">
                          <div
                            className={cn("h-full rounded-full transition-all", pct >= 90 ? "bg-rose-500" : pct >= 70 ? "bg-amber-500" : "bg-emerald-500")}
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      )}
                    </button>
                  );
                })}
              </div>
            </motion.div>
          );
        })}
      </div>
    </div>
  );
}
