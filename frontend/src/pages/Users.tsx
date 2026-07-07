import { useCallback, useEffect, useState } from "react";
import { Pencil, Trash2, RefreshCw, CheckCircle2, XCircle, Star } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Drawer } from "@/components/ui/drawer";
import { Skeleton, ErrorState, EmptyState, LicenseLockNote } from "@/components/ui/states";
import { useMe } from "@/hooks/useMe";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  getUsers, updateUser, deleteUser, syncUsers,
  type UsersData, type UserRow,
} from "@/lib/admin";
import { cn } from "@/lib/utils";

const dt = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "—");

export function Users() {
  // Lisans: Basic 1 kullanıcı — LDAP senkron kilitli (grup üyeleri limiti aşar)
  const { me } = useMe();
  const syncLicensed = (me?.license?.maxUsers ?? Infinity) > 1;
  const [data, setData] = useState<UsersData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<UserRow | null>(null);
  const [toDelete, setToDelete] = useState<UserRow | null>(null);
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState<{ ok: boolean; msg: string } | null>(null);

  const load = useCallback(() => {
    getUsers().then(setData).catch((e) => setError((e as Error).message));
  }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 4000); return () => clearTimeout(t); } }, [flash]);

  const admins = new Set((data?.adminUsers ?? "").split(",").map((x) => x.trim().toLowerCase()).filter(Boolean));

  async function doSync() {
    setBusy(true);
    try { const r = await syncUsers(); setFlash({ ok: r.ok, msg: r.message }); if (r.ok) load(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); }
  }
  async function doDelete() {
    if (!toDelete) return;
    setBusy(true);
    try { await deleteUser(toDelete.id); setFlash({ ok: true, msg: `${toDelete.sam} silindi.` }); setToDelete(null); load(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); }
  }

  if (error) return <ErrorState message={error} onRetry={load} />;
  if (!data) return <UsersSkeleton />;

  return (
    <div className="space-y-4">
      {flash && (
        <div className={cn("flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm",
          flash.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
          {flash.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {flash.msg}
        </div>
      )}

      {!syncLicensed && (
        <LicenseLockNote>LDAP senkronizasyonu Standard ve Enterprise paketlerde kullanılabilir. Basic paket en fazla 1 kullanıcı destekler.</LicenseLockNote>
      )}

      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Yetkiler kullanıcının <b>bir sonraki girişinde</b> geçerli olur. <Star className="inline h-3 w-3 text-amber-400" /> = uygulama admini (Ayarlar'dan yönetilir).
        </p>
        <Button variant="outline" size="sm" onClick={doSync} disabled={busy || !syncLicensed}>
          <RefreshCw className={cn("h-4 w-4", busy && "animate-spin")} /> LDAP Senkronizasyonu
        </Button>
      </div>

      <Card>
        <CardContent className="px-0 py-0">
          {data.users.length === 0 ? <EmptyState title="Kullanıcı yok" /> : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-muted-foreground">
                    <th className="px-4 py-3 font-semibold">Kullanıcı</th>
                    <th className="px-4 py-3 font-semibold">E-posta / Telefon</th>
                    <th className="px-4 py-3 font-semibold">Yetkiler</th>
                    <th className="px-4 py-3 font-semibold">Son Giriş</th>
                    <th className="px-4 py-3 font-semibold">Durum</th>
                    <th className="px-4 py-3" />
                  </tr>
                </thead>
                <tbody>
                  {data.users.map((u) => {
                    const isAdmin = admins.has(u.sam.toLowerCase());
                    const permCount = u.permissionsCsv.split(",").filter(Boolean).length;
                    return (
                      <tr key={u.id} className={cn("border-b border-border/60 transition-colors hover:bg-accent/40", !u.isActive && "opacity-50")}>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-1.5 font-medium">
                            {u.displayName ?? u.sam}
                            {isAdmin && <Star className="h-3.5 w-3.5 text-amber-400" />}
                          </div>
                          <div className="text-xs text-muted-foreground">{u.sam}{u.isLocal ? " · yerel" : " · AD"}</div>
                        </td>
                        <td className="px-4 py-3 text-muted-foreground">
                          <div>{u.email ?? "—"}</div>
                          <div className="text-xs">{u.phone ?? ""}</div>
                        </td>
                        <td className="px-4 py-3">
                          {isAdmin
                            ? <span className="rounded bg-amber-500/15 px-1.5 py-0.5 text-xs font-semibold text-amber-400">tüm yetkiler</span>
                            : <span className="rounded bg-secondary px-1.5 py-0.5 text-xs">{permCount} yetki</span>}
                        </td>
                        <td className="whitespace-nowrap px-4 py-3 tabular-nums text-muted-foreground">{dt(u.lastLogin)}</td>
                        <td className="px-4 py-3">
                          <span className={cn("rounded-md px-2 py-0.5 text-xs font-semibold",
                            u.isActive ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400")}>
                            {u.isActive ? "Aktif" : "Pasif"}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-right">
                          <Button variant="ghost" size="icon" className="h-8 w-8" title="Yetkileri düzenle" onClick={() => setEditing(u)}><Pencil className="h-4 w-4" /></Button>
                          <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive" title="Sil" onClick={() => setToDelete(u)}><Trash2 className="h-4 w-4" /></Button>
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

      <UserEditDrawer user={editing} allPerms={data.allPerms} onClose={() => setEditing(null)}
        onSaved={(m) => { setFlash({ ok: true, msg: m }); load(); }} />
      <ConfirmDialog open={!!toDelete} title="Kullanıcıyı sil"
        message={toDelete ? `"${toDelete.sam}" kaydı silinecek. (LDAP kullanıcısıysa sonraki senkronda tekrar gelebilir.)` : ""}
        loading={busy} onConfirm={doDelete} onCancel={() => setToDelete(null)} />
    </div>
  );
}

function UserEditDrawer({ user, allPerms, onClose, onSaved }: {
  user: UserRow | null;
  allPerms: { key: string; label: string }[];
  onClose: () => void;
  onSaved: (msg: string) => void;
}) {
  const [perms, setPerms] = useState<Set<string>>(new Set());
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (user) {
      setPerms(new Set(user.permissionsCsv.split(",").map((x) => x.trim()).filter(Boolean)));
      setPhone(user.phone ?? ""); setEmail(user.email ?? ""); setErr(null);
    }
  }, [user]);

  const toggle = (k: string) =>
    setPerms((p) => { const n = new Set(p); if (n.has(k)) n.delete(k); else n.add(k); return n; });

  async function submit() {
    if (!user) return;
    setSaving(true); setErr(null);
    try {
      await updateUser(user.id, Array.from(perms), phone.trim() || null, email.trim() || null);
      onSaved(`${user.displayName ?? user.sam} güncellendi (sonraki girişte geçerli).`);
      onClose();
    } catch (e) { setErr((e as Error).message); }
    finally { setSaving(false); }
  }

  return (
    <Drawer open={!!user} onClose={onClose} title={user?.displayName ?? user?.sam ?? ""} description={user ? `${user.sam} — yetki düzenleme` : ""}
      footer={<>
        <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Vazgeç</Button>
        <Button size="sm" onClick={submit} disabled={saving}>{saving ? "Kaydediliyor…" : "Kaydet"}</Button>
      </>}>
      {err && <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{err}</div>}
      <div className="space-y-5">
        <div>
          <p className="mb-2 text-sm font-medium text-muted-foreground">Yetkiler</p>
          <div className="space-y-1 rounded-lg border border-border/60 p-2">
            {allPerms.map((p) => (
              <label key={p.key} className={cn("flex cursor-pointer items-start gap-2.5 rounded-md px-2 py-2 text-sm transition-colors hover:bg-accent/60", perms.has(p.key) && "bg-primary/10")}>
                <input type="checkbox" checked={perms.has(p.key)} onChange={() => toggle(p.key)}
                  className="mt-0.5 h-4 w-4 rounded border-border accent-[hsl(var(--primary))]" />
                <span>
                  <span className="block">{p.label}</span>
                  <span className="block font-mono text-[11px] text-muted-foreground">{p.key}</span>
                </span>
              </label>
            ))}
          </div>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Field label="E-posta (OTP)"><Input value={email} onChange={(e) => setEmail(e.target.value)} placeholder="ad.soyad@firma.com" /></Field>
          <Field label="Telefon (E.164)"><Input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+905xxxxxxxxx" /></Field>
        </div>
      </div>
    </Drawer>
  );
}

function UsersSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-8 w-72" />
      <Card className="p-5 space-y-2">{Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-10 w-full" />)}</Card>
    </div>
  );
}
