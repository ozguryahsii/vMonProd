import { useCallback, useEffect, useRef, useState, type ChangeEvent } from "react";
import {
  Plus, Pencil, Trash2, FlaskConical, Database, Download, RotateCcw, Image, Upload,
  CheckCircle2, XCircle, Send, KeyRound,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Select, Textarea, Field, Switch } from "@/components/ui/input";
import { Drawer } from "@/components/ui/drawer";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Skeleton } from "@/components/ui/states";
import {
  getChannels, createChannel, updateChannel, deleteChannel, toggleChannel, testChannel,
  getBackups, backupNow, backupDelete, backupRestore, backupDownloadUrl,
  uploadLogo, removeLogo, KIND_LABELS,
  type ChannelRow, type ChannelInput, type BackupsData,
} from "@/lib/channels";
import { getLicense, applyLicense, type LicenseState } from "@/lib/settings";
import { cn } from "@/lib/utils";

type Flash = { ok: boolean; msg: string } | null;

/* ================= Bildirim Kanalları ================= */
export function ChannelsCard() {
  const [rows, setRows] = useState<ChannelRow[] | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<ChannelRow | null>(null);
  const [toDelete, setToDelete] = useState<ChannelRow | null>(null);
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState<Flash>(null);
  const [testId, setTestId] = useState<number | null>(null);
  const [testTo, setTestTo] = useState("");

  const load = useCallback(() => { getChannels().then(setRows).catch(() => setRows([])); }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 4000); return () => clearTimeout(t); } }, [flash]);

  async function doToggle(c: ChannelRow) {
    try { await toggleChannel(c.id); load(); } catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
  }
  async function doTest(c: ChannelRow) {
    if (!testTo.trim()) { setFlash({ ok: false, msg: "Test için alıcı girin." }); return; }
    setBusy(true);
    try { const r = await testChannel(c.id, testTo.trim()); setFlash({ ok: r.ok, msg: r.message }); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); setTestId(null); }
  }
  async function doDelete() {
    if (!toDelete) return;
    setBusy(true);
    try { await deleteChannel(toDelete.id); setFlash({ ok: true, msg: `${toDelete.name} silindi.` }); setToDelete(null); load(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); }
  }

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <div>
          <CardTitle className="text-base">Bildirim Kanalları</CardTitle>
          <CardDescription>Özel SMS / WhatsApp / Sesli-IVR entegrasyonları (şablonlu HTTP)</CardDescription>
        </div>
        <Button size="sm" onClick={() => { setEditing(null); setFormOpen(true); }}><Plus className="h-4 w-4" /> Ekle</Button>
      </CardHeader>
      <CardContent className="space-y-2">
        {flash && <FlashLine f={flash} />}
        {rows === null ? <Skeleton className="h-24 w-full" /> :
          rows.length === 0 ? <p className="py-4 text-center text-sm text-muted-foreground">Entegrasyon yok — Twilio yerleşik kanalları yukarıdaki bölümlerden yönetilir.</p> :
          rows.map((c) => (
            <div key={c.id} className={cn("rounded-lg border border-border/60 bg-muted/20 p-3", !c.enabled && "opacity-60")}>
              <div className="flex flex-wrap items-center gap-2">
                <span className={cn("rounded-md px-2 py-0.5 text-xs font-semibold",
                  c.kind === "Whatsapp" ? "bg-emerald-500/15 text-emerald-400" :
                  c.kind === "Ivr" ? "bg-rose-500/15 text-rose-400" : "bg-sky-500/15 text-sky-400")}>
                  {KIND_LABELS[c.kind] ?? c.kind}
                </span>
                <span className="font-medium">{c.name}</span>
                <span className="truncate font-mono text-xs text-muted-foreground">{c.method} {c.url}</span>
                <div className="ml-auto flex items-center gap-1">
                  <Switch checked={c.enabled} onChange={() => doToggle(c)} />
                  <Button variant="ghost" size="icon" className="h-8 w-8" title="Test" onClick={() => { setTestId(testId === c.id ? null : c.id); setTestTo(""); }}>
                    <FlaskConical className="h-4 w-4" />
                  </Button>
                  <Button variant="ghost" size="icon" className="h-8 w-8" title="Düzenle" onClick={() => { setEditing(c); setFormOpen(true); }}><Pencil className="h-4 w-4" /></Button>
                  <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive" title="Sil" onClick={() => setToDelete(c)}><Trash2 className="h-4 w-4" /></Button>
                </div>
              </div>
              {testId === c.id && (
                <div className="mt-2 flex items-center gap-2">
                  <Input value={testTo} onChange={(e) => setTestTo(e.target.value)} placeholder="+905xxxxxxxxx" className="h-9" />
                  <Button variant="outline" size="sm" disabled={busy} onClick={() => doTest(c)}><Send className="h-4 w-4" /> Gönder</Button>
                </div>
              )}
            </div>
          ))}
      </CardContent>

      <ChannelForm open={formOpen} channel={editing} onClose={() => setFormOpen(false)}
        onSaved={(m) => { setFlash({ ok: true, msg: m }); load(); }} />
      <ConfirmDialog open={!!toDelete} title="Entegrasyonu sil"
        message={toDelete ? `"${toDelete.name}" silinecek. Emin misiniz?` : ""}
        loading={busy} onConfirm={doDelete} onCancel={() => setToDelete(null)} />
    </Card>
  );
}

const emptyChannel: ChannelInput = {
  name: "", kind: "Sms", recipients: null, templateSid: null,
  method: "POST", url: "", contentType: "form", body: null, headers: null,
  authType: "none", username: "", sender: "", successContains: null,
  enabled: true, newPassword: null, newApiKey: null,
};

function ChannelForm({ open, channel, onClose, onSaved }: {
  open: boolean; channel: ChannelRow | null; onClose: () => void; onSaved: (m: string) => void;
}) {
  const [f, setF] = useState<ChannelInput>(emptyChannel);
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setF(channel ? {
        name: channel.name, kind: channel.kind, recipients: channel.recipients, templateSid: channel.templateSid,
        method: channel.method, url: channel.url, contentType: channel.contentType, body: channel.body,
        headers: channel.headers, authType: channel.authType, username: channel.username, sender: channel.sender,
        successContains: channel.successContains, enabled: channel.enabled, newPassword: null, newApiKey: null,
      } : emptyChannel);
      setErr(null);
    }
  }, [open, channel]);

  const set = <K extends keyof ChannelInput>(k: K, v: ChannelInput[K]) => setF((p) => ({ ...p, [k]: v }));

  async function submit() {
    setSaving(true); setErr(null);
    try {
      if (channel) { await updateChannel(channel.id, f); onSaved("Entegrasyon güncellendi."); }
      else { await createChannel(f); onSaved("Entegrasyon eklendi."); }
      onClose();
    } catch (e) { setErr((e as Error).message); }
    finally { setSaving(false); }
  }

  return (
    <Drawer open={open} onClose={onClose} title={channel ? "Entegrasyonu Düzenle" : "Yeni Bildirim Kanalı"}
      description="Şablon değişkenleri: {to} {message} {from} {user} {password} {apikey}"
      footer={<>
        <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Vazgeç</Button>
        <Button size="sm" onClick={submit} disabled={saving}>{saving ? "Kaydediliyor…" : "Kaydet"}</Button>
      </>}>
      {err && <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{err}</div>}
      <div className="space-y-4">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Kanal">
            <Select value={f.kind} onChange={(e) => set("kind", e.target.value)}>
              <option value="Sms">SMS</option>
              <option value="Whatsapp">WhatsApp</option>
              <option value="Ivr">Sesli/IVR</option>
            </Select>
          </Field>
          <div className="col-span-2"><Field label="Ad" hint="'Twilio' kullanılamaz"><Input value={f.name} onChange={(e) => set("name", e.target.value)} /></Field></div>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Gönderen / Başlık ({from})"><Input value={f.sender} onChange={(e) => set("sender", e.target.value)} /></Field>
          <Field label="Alıcılar (opsiyonel)" hint="boşsa global alıcılar"><Input value={f.recipients ?? ""} onChange={(e) => set("recipients", e.target.value || null)} /></Field>
        </div>
        {f.kind === "Whatsapp" && (
          <Field label="Alarm Şablonu — Content SID" hint="doluysa alarm butonlu şablonla gider">
            <Input value={f.templateSid ?? ""} onChange={(e) => set("templateSid", e.target.value || null)} placeholder="HX..." />
          </Field>
        )}
        <Field label="URL"><Input value={f.url} onChange={(e) => set("url", e.target.value)} placeholder="https://api.saglayici.com/send" className="font-mono text-xs" /></Field>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Metot"><Select value={f.method} onChange={(e) => set("method", e.target.value)}><option>GET</option><option>POST</option></Select></Field>
          <div className="col-span-2">
            <Field label="POST Gövde Türü">
              <Select value={f.contentType} onChange={(e) => set("contentType", e.target.value)}>
                <option value="form">form (x-www-form-urlencoded)</option>
                <option value="json">json</option>
              </Select>
            </Field>
          </div>
        </div>
        <Field label="POST Gövdesi (şablon)"><Input value={f.body ?? ""} onChange={(e) => set("body", e.target.value || null)} className="font-mono text-xs" placeholder='To={to}&Body={message}  |  {"to":"{to}","text":"{message}"}' /></Field>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Kimlik Doğrulama">
            <Select value={f.authType} onChange={(e) => set("authType", e.target.value)}>
              <option value="none">Yok</option>
              <option value="basic">Basic (user:password)</option>
              <option value="bearer">Bearer (apikey)</option>
            </Select>
          </Field>
          <Field label="Kullanıcı Adı"><Input value={f.username} onChange={(e) => set("username", e.target.value)} className="font-mono text-xs" /></Field>
          <Field label="Şifre ({password})" hint={channel?.hasPassword ? "kayıtlı" : ""}>
            <Input type="password" autoComplete="new-password" value={f.newPassword ?? ""} onChange={(e) => set("newPassword", e.target.value || null)}
              placeholder={channel?.hasPassword ? "•••• (değiştirmek için)" : ""} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="API Key / Token ({apikey})" hint={channel?.hasApiKey ? "kayıtlı" : ""}>
            <Input type="password" autoComplete="new-password" value={f.newApiKey ?? ""} onChange={(e) => set("newApiKey", e.target.value || null)}
              placeholder={channel?.hasApiKey ? "•••• (değiştirmek için)" : ""} />
          </Field>
          <Field label="Başarı Metni (opsiyonel)" hint="boşsa HTTP 2xx başarı sayılır"><Input value={f.successContains ?? ""} onChange={(e) => set("successContains", e.target.value || null)} /></Field>
        </div>
        <Field label='Ek Başlıklar (her satır "Anahtar: Değer")'><Textarea value={f.headers ?? ""} onChange={(e) => set("headers", e.target.value || null)} className="font-mono text-xs" placeholder="X-Api-Key: {apikey}" /></Field>
        <Switch checked={f.enabled} onChange={(v) => set("enabled", v)} label="Aktif" />
      </div>
    </Drawer>
  );
}

/* ================= Yedekler ================= */
export function BackupsCard() {
  const [data, setData] = useState<BackupsData | null>(null);
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState<Flash>(null);
  const [toRestore, setToRestore] = useState<string | null>(null);
  const [toDelete, setToDelete] = useState<string | null>(null);

  const load = useCallback(() => { getBackups().then(setData).catch(() => setData(null)); }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 5000); return () => clearTimeout(t); } }, [flash]);

  async function doNow() {
    setBusy(true);
    try { const r = await backupNow(); setFlash({ ok: r.ok, msg: r.message }); load(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); }
  }
  async function doRestore() {
    if (!toRestore) return;
    setBusy(true);
    try { const r = await backupRestore(toRestore); setFlash({ ok: r.ok, msg: r.message }); setToRestore(null); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); }
  }
  async function doDelete() {
    if (!toDelete) return;
    setBusy(true);
    try { await backupDelete(toDelete); setFlash({ ok: true, msg: "Yedek silindi." }); setToDelete(null); load(); }
    catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setBusy(false); }
  }

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <div>
          <CardTitle className="flex items-center gap-2 text-base"><Database className="h-4 w-4" /> Yedekler</CardTitle>
          <CardDescription>{data?.path ? `Klasör: ${data.path}` : "Yedek klasörünü yukarıdan ayarlayıp kaydedin"}</CardDescription>
        </div>
        <Button size="sm" onClick={doNow} disabled={busy || !data?.isSqlite}><Database className="h-4 w-4" /> Şimdi Yedek Al</Button>
      </CardHeader>
      <CardContent className="space-y-2">
        {flash && <FlashLine f={flash} />}
        {data && !data.isSqlite && <p className="text-sm text-amber-400">Uygulama içi yedekleme yalnız SQLite'ta desteklenir. Bu kurulum harici DB kullanıyor — yedekleme DB tarafında yapılmalı.</p>}
        {data === null ? <Skeleton className="h-20 w-full" /> :
          data.files.length === 0 ? <p className="py-3 text-center text-sm text-muted-foreground">Henüz yedek yok.</p> :
          data.files.map((f) => (
            <div key={f.name} className="flex flex-wrap items-center gap-2 rounded-lg border border-border/60 bg-muted/20 px-3 py-2 text-sm">
              <span className="truncate font-mono text-xs">{f.name}</span>
              <span className="text-xs text-muted-foreground">{f.sizeMb} MB · {new Date(f.modifiedUtc).toLocaleString()}</span>
              <div className="ml-auto flex gap-1">
                <a href={backupDownloadUrl(f.name)}><Button variant="ghost" size="icon" className="h-8 w-8" title="İndir"><Download className="h-4 w-4" /></Button></a>
                <Button variant="ghost" size="icon" className="h-8 w-8 text-amber-400" title="Geri yükle" onClick={() => setToRestore(f.name)}><RotateCcw className="h-4 w-4" /></Button>
                <Button variant="ghost" size="icon" className="h-8 w-8 text-destructive" title="Sil" onClick={() => setToDelete(f.name)}><Trash2 className="h-4 w-4" /></Button>
              </div>
            </div>
          ))}
      </CardContent>

      <ConfirmDialog open={!!toRestore} title="Yedeği geri yükle"
        message={toRestore ? `"${toRestore}" aktif veritabanının ÜZERİNE yazılacak ve uygulama yeniden başlatılacak. Emin misiniz?` : ""}
        confirmLabel="Geri Yükle" loading={busy} onConfirm={doRestore} onCancel={() => setToRestore(null)} />
      <ConfirmDialog open={!!toDelete} title="Yedeği sil"
        message={toDelete ? `"${toDelete}" kalıcı olarak silinecek.` : ""}
        loading={busy} onConfirm={doDelete} onCancel={() => setToDelete(null)} />
    </Card>
  );
}

/* ================= Logo ================= */
export function LogoCard({ current, onChanged }: { current: string; onChanged: () => void }) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState<Flash>(null);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 4000); return () => clearTimeout(t); } }, [flash]);

  async function onFile(e: ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0];
    e.target.value = "";
    if (!f) return;
    setBusy(true);
    try { await uploadLogo(f); setFlash({ ok: true, msg: "Logo güncellendi." }); onChanged(); }
    catch (err) { setFlash({ ok: false, msg: (err as Error).message }); }
    finally { setBusy(false); }
  }
  async function doRemove() {
    setBusy(true);
    try { await removeLogo(); setFlash({ ok: true, msg: "Logo kaldırıldı." }); onChanged(); }
    catch (err) { setFlash({ ok: false, msg: (err as Error).message }); }
    finally { setBusy(false); }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base"><Image className="h-4 w-4" /> Giriş Ekranı Logosu</CardTitle>
        <CardDescription>PNG/JPG/GIF/WEBP, en fazla 2 MB (SVG kabul edilmez)</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {flash && <FlashLine f={flash} />}
        <div className="flex items-center gap-3">
          {current
            ? <span className="rounded-md bg-emerald-500/15 px-2 py-1 text-xs font-semibold text-emerald-400">tanımlı: {current}</span>
            : <span className="text-sm text-muted-foreground">Logo tanımlı değil.</span>}
          <div className="ml-auto flex gap-2">
            <input ref={fileRef} type="file" accept=".png,.jpg,.jpeg,.gif,.webp" className="hidden" onChange={onFile} />
            <Button variant="outline" size="sm" disabled={busy} onClick={() => fileRef.current?.click()}><Upload className="h-4 w-4" /> Yükle</Button>
            {current && <Button variant="ghost" size="sm" disabled={busy} onClick={doRemove}><Trash2 className="h-4 w-4" /> Kaldır</Button>}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

/* ================= Lisans (paket yükseltme / düşürme) ================= */
const EDITION_BADGE: Record<string, string> = {
  Basic: "bg-primary/15 text-primary",
  Standard: "bg-sky-500/15 text-sky-400",
  Enterprise: "bg-amber-500/15 text-amber-400",
};

export function LicenseCard({ onChanged }: { onChanged?: () => void }) {
  const [state, setState] = useState<LicenseState | null>(null);
  const [key, setKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [flash, setFlash] = useState<Flash>(null);
  const [copied, setCopied] = useState(false);

  const load = useCallback(() => { getLicense().then(setState).catch(() => {}); }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 6000); return () => clearTimeout(t); } }, [flash]);

  async function doApply() {
    if (!key.trim()) { setFlash({ ok: false, msg: "Lisans key girin." }); return; }
    setBusy(true);
    try {
      const r = await applyLicense(key.trim());
      setFlash({ ok: true, msg: r.warn ? `${r.message} ${r.warn}` : r.message });
      setKey("");
      load();
      onChanged?.();  // sol üst rozet + Hakkında güncellensin (me yeniden yüklenir)
    } catch (e) {
      setFlash({ ok: false, msg: (e as Error).message });
    } finally { setBusy(false); }
  }

  const lic = state?.license;
  const limit = (n: number | null) => (n == null ? "sınırsız" : String(n));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Lisans</CardTitle>
        <CardDescription>Paket yükseltme/düşürme — yeni lisans key'i buradan uygulanır (Basic / Standard / Enterprise)</CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {flash && <FlashLine f={flash} />}

        {lic ? (
          <div className="rounded-lg border border-border/60 bg-muted/20 p-3 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <span className={cn("rounded-md px-2 py-0.5 text-xs font-semibold uppercase", EDITION_BADGE[lic.edition] ?? "")}>{lic.edition}</span>
              <span className="font-medium">{lic.company}</span>
              <span className={cn("ml-auto text-xs font-semibold", lic.daysLeft <= 30 ? "text-rose-400" : "text-muted-foreground")}>
                bitiş {lic.expires} · {lic.daysLeft} gün kaldı
              </span>
            </div>
            <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground sm:grid-cols-3">
              <span>İzleme: <b className="text-foreground">{limit(lic.maxMonitors)}</b></span>
              <span>Kullanıcı: <b className="text-foreground">{limit(lic.maxUsers)}</b></span>
              <span>Dashboard: <b className="text-foreground">{limit(lic.maxDashboards)}</b></span>
              <span>Veritabanı: <b className="text-foreground">{lic.sqliteOnly ? "yalnız SQLite" : "tümü"}</b></span>
              <span>Bildirim: <b className="text-foreground">{lic.emailOnly ? "yalnız e-posta" : "tüm kanallar"}</b></span>
              <span>SIEM: <b className="text-foreground">{lic.siem ? "açık" : "kapalı"}</b></span>
            </div>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Lisans bilgisi yükleniyor…</p>
        )}

        {state && (
          <div className="rounded-lg border border-border/60 bg-muted/10 px-3 py-2 text-xs">
            <span className="text-muted-foreground">Makine Kodu (yeni key isterken satıcıya iletin): </span>
            <code className="font-semibold tracking-wider">{state.machineCode}</code>
            <button type="button"
              onClick={() => { navigator.clipboard.writeText(state.machineCode); setCopied(true); setTimeout(() => setCopied(false), 1200); }}
              className="ml-2 rounded border border-border px-1.5 py-0.5 text-muted-foreground hover:bg-accent/60">
              {copied ? "kopyalandı" : "kopyala"}
            </button>
          </div>
        )}

        <Field label="Yeni Lisans Key" hint="Paket değiştirmek için yeni key'i yapıştırıp uygulayın. Satır kırılması sorun değil.">
          <Textarea rows={3} value={key} onChange={(e) => setKey(e.target.value)} spellCheck={false}
            placeholder="VMON1.xxxxx.xxxxx" className="font-mono text-xs" />
        </Field>
        <Button size="sm" disabled={busy} onClick={doApply}>
          <KeyRound className="h-4 w-4" /> {busy ? "Uygulanıyor…" : "Lisansı Uygula"}
        </Button>
      </CardContent>
    </Card>
  );
}

function FlashLine({ f }: { f: NonNullable<Flash> }) {
  return (
    <div className={cn("flex items-center gap-2 rounded-lg border px-3 py-2 text-sm",
      f.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
      {f.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {f.msg}
    </div>
  );
}
