import { useCallback, useEffect, useState } from "react";
import { Search, ShieldCheck, CheckCircle2, XCircle } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Select } from "@/components/ui/input";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { getAudit, verifyAudit, type AuditRow } from "@/lib/admin";
import { cn } from "@/lib/utils";

const dt = (iso: string) => new Date(iso).toLocaleString();

export function Audit() {
  const [q, setQ] = useState("");
  const [act, setAct] = useState("");
  const [days, setDays] = useState(0);
  const [rows, setRows] = useState<AuditRow[] | null>(null);
  const [actions, setActions] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [verify, setVerify] = useState<{ ok: boolean; message: string } | null>(null);

  const load = useCallback(() => {
    const ctrl = new AbortController();
    setError(null);
    getAudit(q, act, days, 500, ctrl.signal)
      .then((d) => { setRows(d.rows); setActions(d.actions); })
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); });
    return () => ctrl.abort();
  }, [q, act, days]);

  // Arama yazarken 400ms bekle (debounce)
  useEffect(() => { const t = setTimeout(load, q ? 400 : 0); return () => clearTimeout(t); }, [load, q]);

  async function doVerify() {
    setVerifying(true); setVerify(null);
    try { setVerify(await verifyAudit()); }
    catch (e) { setVerify({ ok: false, message: (e as Error).message }); }
    finally { setVerifying(false); }
  }

  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!rows) return <AuditSkeleton />;

  return (
    <div className="space-y-4">
      {verify && (
        <div className={cn("flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm",
          verify.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
          {verify.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
          {verify.ok ? "Bütünlük doğrulandı: " : "Bütünlük ihlali: "}{verify.message}
        </div>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-[220px] flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Kullanıcı, hedef, detay veya IP ara…" className="pl-9" />
        </div>
        <Select value={act} onChange={(e) => setAct(e.target.value)} className="w-auto">
          <option value="">Tüm eylemler</option>
          {actions.map((a) => <option key={a} value={a}>{a}</option>)}
        </Select>
        <Select value={String(days)} onChange={(e) => setDays(Number(e.target.value))} className="w-auto">
          <option value="0">Tüm zamanlar</option>
          <option value="1">Son 1 gün</option>
          <option value="7">Son 7 gün</option>
          <option value="30">Son 30 gün</option>
          <option value="90">Son 90 gün</option>
        </Select>
        <Button variant="outline" size="sm" onClick={doVerify} disabled={verifying}>
          <ShieldCheck className={cn("h-4 w-4", verifying && "animate-pulse")} /> {verifying ? "Doğrulanıyor…" : "Bütünlüğü Doğrula"}
        </Button>
      </div>

      <Card>
        <CardContent className="px-0 py-0">
          {rows.length === 0 ? (
            <EmptyState title="Kayıt bulunamadı" hint="Filtreleri değiştirmeyi dene." />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                    <th className="px-4 py-3 font-semibold">Zaman</th>
                    <th className="px-4 py-3 font-semibold">Kullanıcı</th>
                    <th className="px-4 py-3 font-semibold">IP</th>
                    <th className="px-4 py-3 font-semibold">Eylem</th>
                    <th className="px-4 py-3 font-semibold">Hedef</th>
                    <th className="px-4 py-3 font-semibold">Detay</th>
                    <th className="px-4 py-3 font-semibold">Sonuç</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id} className="border-b border-border/60 transition-colors hover:bg-accent/40">
                      <td className="whitespace-nowrap px-4 py-2.5 tabular-nums text-muted-foreground">{dt(r.at)}</td>
                      <td className="px-4 py-2.5 font-medium">{r.user}</td>
                      <td className="px-4 py-2.5 font-mono text-xs text-muted-foreground">{r.ip ?? "—"}</td>
                      <td className="px-4 py-2.5"><span className="rounded bg-secondary px-1.5 py-0.5 font-mono text-xs">{r.action}</span></td>
                      <td className="max-w-[180px] truncate px-4 py-2.5 text-muted-foreground" title={r.target ?? ""}>{r.target ?? "—"}</td>
                      <td className="max-w-[340px] truncate px-4 py-2.5 text-muted-foreground" title={r.detail ?? ""}>{r.detail ?? "—"}</td>
                      <td className="px-4 py-2.5">
                        {r.success
                          ? <CheckCircle2 className="h-4 w-4 text-emerald-400" />
                          : <XCircle className="h-4 w-4 text-rose-400" />}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
      <div className="text-xs text-muted-foreground">{rows.length} kayıt (en yeni 500)</div>
    </div>
  );
}

function AuditSkeleton() {
  return (
    <div className="space-y-4">
      <div className="flex gap-3"><Skeleton className="h-10 flex-1" /><Skeleton className="h-10 w-40" /><Skeleton className="h-10 w-40" /></div>
      <Card className="p-5 space-y-2">{Array.from({ length: 10 }).map((_, i) => <Skeleton key={i} className="h-8 w-full" />)}</Card>
    </div>
  );
}
