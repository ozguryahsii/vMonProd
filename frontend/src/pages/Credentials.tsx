import { useCallback, useEffect, useState } from "react";
import { Plus, Pencil, Trash2, KeyRound, Vault, FlaskConical, CheckCircle2, XCircle } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Select, Textarea, Field } from "@/components/ui/input";
import { Drawer } from "@/components/ui/drawer";
import { Skeleton, ErrorState, EmptyState } from "@/components/ui/states";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  getCredentials, createCredential, updateCredential, deleteCredential, vaultTest,
  type CredentialRow, type CredentialInput,
} from "@/lib/admin";
import { cn } from "@/lib/utils";

export function Credentials() {
  const [rows, setRows] = useState<CredentialRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<CredentialRow | null>(null);
  const [toDelete, setToDelete] = useState<CredentialRow | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [flash, setFlash] = useState<{ ok: boolean; msg: string } | null>(null);

  const load = useCallback(() => {
    getCredentials().then(setRows).catch((e) => setError((e as Error).message));
  }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 4000); return () => clearTimeout(t); } }, [flash]);

  async function doVaultTest(c: CredentialRow) {
    setBusyId(c.id);
    try { const r = await vaultTest(c.id); setFlash({ ok: r.ok, msg: r.message }); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusyId(null); }
  }
  async function doDelete() {
    if (!toDelete) return;
    setDeleting(true);
    try { await deleteCredential(toDelete.id); setFlash({ ok: true, msg: `${toDelete.name} silindi.` }); setToDelete(null); load(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setDeleting(false); }
  }

  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!rows) return <CredSkeleton />;

  return (
    <div className="space-y-4">
      {flash && (
        <div className={cn("flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm",
          flash.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
          {flash.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {flash.msg}
        </div>
      )}

      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">Servis izleme/kontrolünde kullanılan hesaplar. Şifreler şifreli saklanır, asla geri gösterilmez.</p>
        <Button size="sm" onClick={() => { setEditing(null); setFormOpen(true); }}><Plus className="h-4 w-4" /> Yeni Kimlik</Button>
      </div>

      <Card>
        <CardContent className="px-0 py-0">
          {rows.length === 0 ? <EmptyState title="Kimlik bilgisi yok" hint="Yeni Kimlik ile ekle." /> : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                    <th className="px-4 py-3 font-semibold">Ad</th>
                    <th className="px-4 py-3 font-semibold">Kullanıcı</th>
                    <th className="px-4 py-3 font-semibold">Alan (Domain)</th>
                    <th className="px-4 py-3 font-semibold">Kaynak</th>
                    <th className="px-4 py-3 font-semibold">Açıklama</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((c) => {
                    const isVault = c.sourceType.toLowerCase() === "vault";
                    return (
                      <tr key={c.id} className="border-b border-border/60 transition-colors hover:bg-accent/40">
                        <td className="px-4 py-3 font-medium">{c.name}</td>
                        <td className="px-4 py-3 font-mono text-xs text-muted-foreground">{c.username || "—"}</td>
                        <td className="px-4 py-3 text-muted-foreground">{c.domain ?? "—"}</td>
                        <td className="px-4 py-3">
                          <span className={cn("inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-semibold",
                            isVault ? "bg-violet-500/15 text-violet-400" : "bg-sky-500/15 text-sky-400")}>
                            {isVault ? <Vault className="h-3 w-3" /> : <KeyRound className="h-3 w-3" />}
                            {isVault ? "Vault" : "Elle"}
                          </span>
                        </td>
                        <td className="max-w-[280px] truncate px-4 py-3 text-muted-foreground" title={c.description ?? ""}>{c.description ?? "—"}</td>
                        <td className="px-4 py-3 text-right">
                          {isVault && (
                            <Button variant="ghost" size="icon" className="h-8 w-8 text-violet-400" title="Vault erişim testi"
                              disabled={busyId === c.id} onClick={() => doVaultTest(c)}>
                              <FlaskConical className={cn("h-4 w-4", busyId === c.id && "animate-pulse")} />
                            </Button>
                          )}
                          <Button variant="ghost" size="icon" className="h-8 w-8" title="Düzenle" onClick={() => { setEditing(c); setFormOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                          <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive" title="Sil" onClick={() => setToDelete(c)}><Trash2 className="h-4 w-4" /></Button>
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

      <CredentialForm open={formOpen} cred={editing} onClose={() => setFormOpen(false)}
        onSaved={(m) => { setFlash({ ok: true, msg: m }); load(); }} />
      <ConfirmDialog open={!!toDelete} title="Kimlik bilgisini sil"
        message={toDelete ? `"${toDelete.name}" silinecek. Bunu kullanan servisler kimliksiz kalır.` : ""}
        loading={deleting} onConfirm={doDelete} onCancel={() => setToDelete(null)} />
    </div>
  );
}

const empty: CredentialInput = {
  name: "", username: "", domain: null, description: null, sourceType: "Manual",
  newPassword: null, vaultUrl: null, newVaultToken: null, vaultKey: null, vaultUserKey: null,
};

function CredentialForm({ open, cred, onClose, onSaved }: {
  open: boolean;
  cred: CredentialRow | null;
  onClose: () => void;
  onSaved: (msg: string) => void;
}) {
  const [form, setForm] = useState<CredentialInput>(empty);
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setForm(cred ? {
        name: cred.name, username: cred.username, domain: cred.domain, description: cred.description,
        sourceType: cred.sourceType, newPassword: null,
        vaultUrl: cred.vaultUrl, newVaultToken: null, vaultKey: cred.vaultKey, vaultUserKey: cred.vaultUserKey,
      } : empty);
      setErr(null);
    }
  }, [open, cred]);

  const set = <K extends keyof CredentialInput>(k: K, v: CredentialInput[K]) => setForm((f) => ({ ...f, [k]: v }));
  const isVault = form.sourceType.toLowerCase() === "vault";

  async function submit() {
    setSaving(true); setErr(null);
    try {
      if (cred) { await updateCredential(cred.id, form); onSaved("Kimlik bilgisi güncellendi."); }
      else { await createCredential(form); onSaved("Kimlik bilgisi eklendi."); }
      onClose();
    } catch (e) { setErr((e as Error).message); }
    finally { setSaving(false); }
  }

  return (
    <Drawer open={open} onClose={onClose} title={cred ? "Kimliği Düzenle" : "Yeni Kimlik Bilgisi"}
      description={cred ? cred.name : "İzleme/kontrol için hesap tanımla"}
      footer={<>
        <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Vazgeç</Button>
        <Button size="sm" onClick={submit} disabled={saving}>{saving ? "Kaydediliyor…" : "Kaydet"}</Button>
      </>}>
      {err && <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{err}</div>}
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <Field label="Ad"><Input value={form.name} onChange={(e) => set("name", e.target.value)} placeholder="örn. AD Bind Hesabı" /></Field>
          <Field label="Kaynak">
            <Select value={form.sourceType} onChange={(e) => set("sourceType", e.target.value)}>
              <option value="Manual">Elle (şifre burada saklanır)</option>
              <option value="Vault">HashiCorp Vault</option>
            </Select>
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Field label="Kullanıcı adı"><Input value={form.username} onChange={(e) => set("username", e.target.value)} placeholder="svc_monitor" /></Field>
          <Field label="Alan (Domain)"><Input value={form.domain ?? ""} onChange={(e) => set("domain", e.target.value || null)} placeholder="FIRMA (opsiyonel)" /></Field>
        </div>

        {!isVault ? (
          <Field label="Şifre" hint={cred?.hasPassword ? "kayıtlı — değiştirmek için yeni şifre girin" : ""}>
            <Input type="password" autoComplete="new-password" value={form.newPassword ?? ""}
              onChange={(e) => set("newPassword", e.target.value || null)}
              placeholder={cred?.hasPassword ? "•••••• (değiştirmek için girin)" : "şifre"} />
          </Field>
        ) : (
          <div className="space-y-4 rounded-lg border border-violet-500/30 bg-violet-500/5 p-4">
            <Field label="Vault Secret URL"><Input value={form.vaultUrl ?? ""} onChange={(e) => set("vaultUrl", e.target.value || null)} placeholder="vault.firma.local/v1/secret/data/monitor" /></Field>
            <Field label="Vault Token" hint={cred?.hasToken ? "kayıtlı — değiştirmek için yeni token girin" : ""}>
              <Input type="password" autoComplete="new-password" value={form.newVaultToken ?? ""}
                onChange={(e) => set("newVaultToken", e.target.value || null)}
                placeholder={cred?.hasToken ? "•••••• (değiştirmek için girin)" : "hvs...."} />
            </Field>
            <div className="grid grid-cols-2 gap-4">
              <Field label="Kullanıcı adı anahtarı"><Input value={form.vaultUserKey ?? ""} onChange={(e) => set("vaultUserKey", e.target.value || null)} placeholder="username" /></Field>
              <Field label="Şifre anahtarı"><Input value={form.vaultKey ?? ""} onChange={(e) => set("vaultKey", e.target.value || null)} placeholder="password" /></Field>
            </div>
          </div>
        )}

        <Field label="Açıklama"><Textarea value={form.description ?? ""} onChange={(e) => set("description", e.target.value || null)} /></Field>
      </div>
    </Drawer>
  );
}

function CredSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-8 w-80" />
      <Card className="p-5 space-y-2">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-10 w-full" />)}</Card>
    </div>
  );
}
