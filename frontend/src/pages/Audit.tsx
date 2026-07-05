import { useCallback, useEffect, useState } from "react";
import { Search, ShieldCheck, CheckCircle2, XCircle, FileSpreadsheet, FileText } from "lucide-react";
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
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [exporting, setExporting] = useState(false);
  const [rows, setRows] = useState<AuditRow[] | null>(null);
  const [actions, setActions] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [verifying, setVerifying] = useState(false);
  const [verify, setVerify] = useState<{ ok: boolean; message: string } | null>(null);

  const load = useCallback(() => {
    const ctrl = new AbortController();
    setError(null);
    getAudit(q, act, days, 500, ctrl.signal, from, to)
      .then((d) => { setRows(d.rows); setActions(d.actions); })
      .catch((e) => { if ((e as Error).name !== "AbortError") setError((e as Error).message); });
    return () => ctrl.abort();
  }, [q, act, days, from, to]);

  const dtTr = (iso: string) => new Date(iso).toLocaleString();

  /** Aktif filtrelerle TÜM eşleşen kayıtları çek (dışa aktarım — 20.000'e kadar). */
  async function fetchAll(): Promise<AuditRow[]> {
    const d = await getAudit(q, act, days, 20000, undefined, from, to);
    return d.rows;
  }

  async function exportExcel() {
    setExporting(true);
    try {
      const all = await fetchAll();
      const esc = (v: unknown) => `"${String(v ?? "").replace(/"/g, '""')}"`;
      const lines = [
        "Zaman;Kullanici;IP;Eylem;Hedef;Detay;Sonuc",
        ...all.map((r) => [dtTr(r.at), esc(r.user), r.ip ?? "", r.action, esc(r.target), esc(r.detail), r.success ? "Basarili" : "Basarisiz"].join(";")),
      ];
      const blob = new Blob(["﻿" + lines.join("\r\n")], { type: "text/csv;charset=utf-8" });
      const a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = `vmon-denetim_${from || "baslangic"}_${to || "bugun"}.csv`;
      a.click();
      URL.revokeObjectURL(a.href);
    } finally { setExporting(false); }
  }

  async function exportPdf() {
    setExporting(true);
    try {
      const all = await fetchAll();
      const escH = (s: unknown) => String(s ?? "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
      const rowsHtml = all.map((r) =>
        `<tr><td>${dtTr(r.at)}</td><td>${escH(r.user)}</td><td>${escH(r.ip)}</td><td>${escH(r.action)}</td><td>${escH(r.target)}</td><td>${escH(r.detail)}</td><td>${r.success ? "✓" : "✗"}</td></tr>`).join("");
      const w = window.open("", "_blank");
      if (!w) return;
      w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>vMon Denetim Kaydı</title>
        <style>
          body{font-family:Segoe UI,Arial,sans-serif;font-size:11px;color:#111;margin:24px}
          h1{font-size:16px;margin:0 0 4px} .sub{color:#666;margin-bottom:12px}
          table{width:100%;border-collapse:collapse} th,td{border:1px solid #ccc;padding:4px 6px;text-align:left;vertical-align:top}
          th{background:#f1f1f1;font-size:10px;text-transform:uppercase} tr:nth-child(even) td{background:#fafafa}
          @media print { @page { size: A4 landscape; margin: 12mm } }
        </style></head><body>
        <h1>vMon — Denetim Kaydı</h1>
        <div class="sub">Aralık: ${from || "başlangıç"} → ${to || "bugün"} · ${all.length} kayıt · Oluşturma: ${new Date().toLocaleString()}</div>
        <table><thead><tr><th>Zaman</th><th>Kullanıcı</th><th>IP</th><th>Eylem</th><th>Hedef</th><th>Detay</th><th>Sonuç</th></tr></thead>
        <tbody>${rowsHtml}</tbody></table>
        <script>window.onload=function(){window.print()}</script></body></html>`);
      w.document.close();
    } finally { setExporting(false); }
  }

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
        <Input type="date" value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} className="w-auto" title="Başlangıç" />
        <Input type="date" value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} className="w-auto" title="Bitiş" />
        <Button variant="outline" size="sm" onClick={doVerify} disabled={verifying}>
          <ShieldCheck className={cn("h-4 w-4", verifying && "animate-pulse")} /> {verifying ? "Doğrulanıyor…" : "Bütünlüğü Doğrula"}
        </Button>
        <Button variant="outline" size="sm" onClick={exportExcel} disabled={exporting}>
          <FileSpreadsheet className="h-4 w-4" /> Excel
        </Button>
        <Button variant="outline" size="sm" onClick={exportPdf} disabled={exporting}>
          <FileText className="h-4 w-4" /> PDF
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
