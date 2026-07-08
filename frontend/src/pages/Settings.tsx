import { useEffect, useRef, useState, type ChangeEvent, type ReactNode } from "react";
import {
  Save, CheckCircle2, XCircle, Mail, ShieldCheck,
  Radio, CalendarClock, Database, Building2, KeyRound, Timer, FlaskConical, Globe,
} from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Select, Field, Switch } from "@/components/ui/input";
import { Skeleton, ErrorState, LicenseLockNote } from "@/components/ui/states";
import {
  getSettings, saveSettings, testEmail, testLdap, testSyslog, eolSyncNow,
  type AppSettings,
} from "@/lib/settings";
import { getCredentials, type CredentialRow } from "@/lib/admin";
import { ChannelsCard, BackupsCard, LogoCard, LicenseCard, UpdateCard } from "@/components/settings/ManagementSections";
import { useMe } from "@/hooks/useMe";
import { cn } from "@/lib/utils";

export function Settings() {
  const { me, reloadMe } = useMe();
  // Paket kilitleri: Basic'te SIEM kapalı (arayüzde de kilitli — kullanıcı denemeye uğraşmasın)
  const siemLicensed = me?.license?.siem !== false;
  const [s, setS] = useState<AppSettings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creds, setCreds] = useState<CredentialRow[]>([]);
  const [newSms, setNewSms] = useState("");
  const [newWa, setNewWa] = useState("");
  const [newBk, setNewBk] = useState("");
  const [newSmtp, setNewSmtp] = useState("");
  const [saving, setSaving] = useState(false);
  const [flash, setFlash] = useState<{ ok: boolean; msg: string } | null>(null);
  const [testMsg, setTestMsg] = useState<Record<string, { ok: boolean; msg: string }>>({});
  const [ldapUser, setLdapUser] = useState("");
  const [ldapPass, setLdapPass] = useState("");
  const eolFileRef = useRef<HTMLInputElement>(null);

  async function onEolImport(e: ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0];
    e.target.value = "";
    if (!f) return;
    setTestMsg((m) => ({ ...m, eol: { ok: true, msg: "İçe aktarılıyor…" } }));
    try {
      const token = await (await fetch("/api/antiforgery", { credentials: "same-origin" })).json();
      const fd = new FormData();
      fd.append("eolFile", f);
      const res = await fetch("/api/eol-import", { method: "POST", credentials: "same-origin", headers: { "X-CSRF-TOKEN": token.token }, body: fd });
      const r = await res.json();
      setTestMsg((m) => ({ ...m, eol: { ok: r.ok, msg: r.message } }));
    } catch (err) {
      setTestMsg((m) => ({ ...m, eol: { ok: false, msg: (err as Error).message } }));
    }
  }

  useEffect(() => {
    getSettings().then(setS).catch((e) => setError((e as Error).message));
    getCredentials().then(setCreds).catch(() => {});
  }, []);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 4000); return () => clearTimeout(t); } }, [flash]);

  const set = <K extends keyof AppSettings>(k: K, v: AppSettings[K]) => setS((p) => (p ? { ...p, [k]: v } : p));

  async function doSave() {
    if (!s) return;
    setSaving(true);
    try {
      await saveSettings(s, newSms, newWa, newBk, newSmtp);
      setNewSms(""); setNewWa(""); setNewBk(""); setNewSmtp("");
      setFlash({ ok: true, msg: "Ayarlar kaydedildi." });
      getSettings().then(setS).catch(() => {});
    } catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setSaving(false); }
  }

  async function runTest(key: string, fn: () => Promise<{ ok: boolean; message?: string }>) {
    setTestMsg((m) => ({ ...m, [key]: { ok: true, msg: "Çalışıyor…" } }));
    try {
      const r = await fn();
      setTestMsg((m) => ({ ...m, [key]: { ok: r.ok !== false, msg: r.message ?? (r.ok !== false ? "Başarılı ✅" : "Başarısız") } }));
    } catch (e) {
      setTestMsg((m) => ({ ...m, [key]: { ok: false, msg: (e as Error).message } }));
    }
  }

  if (error) return <ErrorState message={error} />;
  if (!s) return <SettingsSkeleton />;

  const num = (v: string, d = 0) => (v.trim() === "" ? d : Number(v));

  return (
    <div className="space-y-4 pb-20">
      {flash && (
        <div className={cn("sticky top-16 z-20 flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm backdrop-blur",
          flash.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
          {flash.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {flash.msg}
        </div>
      )}

      {/* TEK SÜTUN: her kart tam genişlik (Güncelleme kartı gibi), alanlar satır içine yayılır.
          Sıralama ÖNEM sırasına göre: izleme çekirdeği → bildirim → erişim/güvenlik → yayın/veri → yönetim. */}

      {/* 1. İzleme — uygulamanın çekirdeği */}
      <Section icon={<Timer className="h-4 w-4" />} title="İzleme">
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Aralık (dk)"><Input type="number" value={s.checkIntervalMinutes} onChange={(e) => set("checkIntervalMinutes", num(e.target.value, 5))} /></Field>
          <Field label="Hata eşiği" hint="ardışık hata → alarm"><Input type="number" value={s.failureThreshold} onChange={(e) => set("failureThreshold", num(e.target.value, 2))} /></Field>
          <Field label="Geçmiş (gün)"><Input type="number" value={s.historyRetentionDays} onChange={(e) => set("historyRetentionDays", num(e.target.value, 365))} /></Field>
        </div>
      </Section>

      {/* 2. E-posta — birincil alarm kanalı */}
      <Section icon={<Mail className="h-4 w-4" />} title="E-posta (SMTP)">
        <Switch checked={s.emailEnabled} onChange={(v) => set("emailEnabled", v)} label="E-posta alarmları açık" />
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
          <div className="xl:col-span-2"><Field label="SMTP sunucusu"><Input value={s.smtpHost} onChange={(e) => set("smtpHost", e.target.value)} placeholder="mail.firma.com" /></Field></div>
          <Field label="Bağlantı güvenliği" hint="portu önerir">
            <Select value={s.smtpSecurity}
              onChange={(e) => {
                const sec = e.target.value as AppSettings["smtpSecurity"];
                // Güvenlik değişince standart portu otomatik doldur (kullanıcı yine de değiştirebilir)
                const port = sec === "starttls" ? 587 : sec === "ssl" ? 465 : 25;
                setS((p) => (p ? { ...p, smtpSecurity: sec, smtpPort: port } : p));
              }}>
              <option value="none">Yok (şifresiz relay)</option>
              <option value="starttls">STARTTLS</option>
              <option value="ssl">SSL/TLS</option>
            </Select>
          </Field>
          <Field label="Port"><Input type="number" value={s.smtpPort} onChange={(e) => set("smtpPort", num(e.target.value, 25))} /></Field>
          <div className="xl:col-span-2"><Field label="Gönderen adres"><Input value={s.mailFrom} onChange={(e) => set("mailFrom", e.target.value)} /></Field></div>
        </div>
        <div className="grid gap-3 xl:grid-cols-2">
          <Field label="Varsayılan alıcılar" hint="virgülle ayır"><Input value={s.mailRecipients} onChange={(e) => set("mailRecipients", e.target.value)} /></Field>
          {/* Kimlik doğrulama (opsiyonel) — sağlayıcı kullanıcı adı/şifre istiyorsa */}
          <div className="rounded-lg border border-border/60 bg-muted/20 p-3">
            <Switch checked={s.smtpUseAuth} onChange={(v) => set("smtpUseAuth", v)} label="Kimlik doğrulama gerektiriyor (kullanıcı adı / şifre)" />
            {s.smtpUseAuth && (
              <div className="mt-3 grid grid-cols-2 gap-3">
                <Field label="Kullanıcı adı"><Input value={s.smtpUsername} onChange={(e) => set("smtpUsername", e.target.value)} placeholder="ornek@firma.com" autoComplete="off" /></Field>
                <Field label="Şifre" hint={s.hasSmtpPassword ? "kayıtlı — değiştirmek için yaz" : "gizli, şifreli saklanır"}>
                  <Input type="password" value={newSmtp} onChange={(e) => setNewSmtp(e.target.value)} placeholder={s.hasSmtpPassword ? "••••••••" : ""} autoComplete="new-password" />
                </Field>
              </div>
            )}
          </div>
        </div>
        <p className="text-xs text-muted-foreground">
          Kurum içi açık relay için "Yok" + port 25 seçin. Office365/Gmail gibi sağlayıcılar için STARTTLS (587) veya SSL/TLS (465) + kimlik doğrulama kullanın.
        </p>
        <TestRow k="email" msg={testMsg}>
          <Button variant="outline" size="sm" onClick={() => runTest("email", async () => { await testEmail(); return { ok: true, message: "Test maili gönderildi ✅" }; })}>
            <Mail className="h-4 w-4" /> Test Maili Gönder
          </Button>
        </TestRow>
      </Section>

      {/* 3. Bildirim Kanalları (SMS/WhatsApp/IVR — anında uygulanır) */}
      <ChannelsCard />

      {/* SMS ve WhatsApp Twilio ayarları KALDIRILDI (kullanıcı isteği):
          özel kanallar yukarıdaki Bildirim Kanalları'ndan yönetilir. Alanlar DB'de duruyor,
          klasik arayüzden hâlâ erişilebilir (OTP SMS/WhatsApp kanalı onları kullanır). */}

      {/* 4. Genel */}
      <Section icon={<Building2 className="h-4 w-4" />} title="Genel">
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Field label="Şirket adı"><Input value={s.companyName} onChange={(e) => set("companyName", e.target.value)} /></Field>
          <Field label="Admin kullanıcıları" hint="virgülle ayrılmış sAMAccountName — bu kullanıcılar tüm yetkilere sahiptir">
            <Input value={s.adminUsers} onChange={(e) => set("adminUsers", e.target.value)} placeholder="admin, ozgur.yahsi" />
          </Field>
          <div className="flex items-center">
            <Switch checked={s.trustInternalTlsCertificates} onChange={(v) => set("trustInternalTlsCertificates", v)} label="Kurum içi TLS sertifikalarına güven (Vault vb.)" />
          </div>
        </div>
      </Section>

      {/* 5. LDAP / Active Directory */}
      <Section icon={<KeyRound className="h-4 w-4" />} title="Oturum Açma (LDAP / Active Directory)">
        <div className="flex flex-wrap items-center gap-6">
          <Switch checked={s.authEnabled} onChange={(v) => set("authEnabled", v)} label="Giriş zorunlu (LDAP + yerel)" />
          <Switch checked={s.ldapAuthUseSsl} onChange={(v) => set("ldapAuthUseSsl", v)} label="LDAPS (SSL)" />
        </div>
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Field label="Sunucu"><Input value={s.ldapAuthHost} onChange={(e) => set("ldapAuthHost", e.target.value)} placeholder="dc01.firma.local" /></Field>
          <Field label="Port"><Input type="number" value={s.ldapAuthPort} onChange={(e) => set("ldapAuthPort", num(e.target.value, 389))} /></Field>
          <Field label="Domain"><Input value={s.ldapAuthDomain} onChange={(e) => set("ldapAuthDomain", e.target.value)} placeholder="FIRMA" /></Field>
          <Field label="Base DN"><Input value={s.ldapAuthBaseDn} onChange={(e) => set("ldapAuthBaseDn", e.target.value)} placeholder="DC=firma,DC=local" /></Field>
        </div>
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <div className="xl:col-span-2">
            <Field label="Yetkili grup DN" hint="yalnız bu grubun üyeleri girebilir">
              <Input value={s.ldapAuthGroupDn} onChange={(e) => set("ldapAuthGroupDn", e.target.value)} placeholder="CN=vmon-users,OU=Groups,DC=firma,DC=local" />
            </Field>
          </div>
          <Field label="Senkron kimliği" hint="grup üyelerini listelemek için servis hesabı">
            <Select value={s.ldapSyncCredentialId ?? ""} onChange={(e) => set("ldapSyncCredentialId", e.target.value ? Number(e.target.value) : null)}>
              <option value="">— Yok —</option>
              {creds.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Maks. giriş denemesi"><Input type="number" value={s.maxLoginAttempts} onChange={(e) => set("maxLoginAttempts", num(e.target.value, 10))} /></Field>
            <Field label="Kilit süresi (dk)"><Input type="number" value={s.lockoutMinutes} onChange={(e) => set("lockoutMinutes", num(e.target.value, 30))} /></Field>
          </div>
        </div>
        <TestRow k="ldap" msg={testMsg}>
          <Input value={ldapUser} onChange={(e) => setLdapUser(e.target.value)} placeholder="kullanıcı" className="h-9 max-w-[220px]" />
          <Input type="password" value={ldapPass} onChange={(e) => setLdapPass(e.target.value)} placeholder="şifre" className="h-9 max-w-[220px]" />
          <Button variant="outline" size="sm" onClick={() => runTest("ldap", () => testLdap(ldapUser, ldapPass))}><FlaskConical className="h-4 w-4" /> Test Girişi</Button>
        </TestRow>
      </Section>

      {/* 6. OTP */}
      <Section icon={<ShieldCheck className="h-4 w-4" />} title="İki Adımlı Doğrulama (OTP)">
        <div className="grid items-center gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Switch checked={s.otpEnabled} onChange={(v) => set("otpEnabled", v)} label="Girişte OTP zorunlu" />
          <Field label="Kanal">
            <Select value={s.otpChannel} onChange={(e) => set("otpChannel", e.target.value)}>
              <option value="Email">E-posta</option>
              <option value="Sms">SMS</option>
              <option value="Whatsapp">WhatsApp</option>
            </Select>
          </Field>
          <p className="text-xs text-muted-foreground">Seçili kanal için admin kullanıcıların iletişim bilgisi (e-posta/telefon) tanımlı olmalıdır; yoksa kaydetme engellenir (kilitlenme koruması).</p>
        </div>
      </Section>

      {/* 7. Güvenlik ve Uyumluluk */}
      <Section icon={<ShieldCheck className="h-4 w-4" />} title="Güvenlik ve Uyumluluk">
        <div className="grid items-end gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Field label="Min. şifre uzunluğu"><Input type="number" value={s.minPasswordLength} onChange={(e) => set("minPasswordLength", num(e.target.value, 12))} /></Field>
          <Field label="Şifre geçmişi" hint="tekrar kullanılamaz"><Input type="number" value={s.passwordHistoryCount} onChange={(e) => set("passwordHistoryCount", num(e.target.value, 4))} /></Field>
          <Field label="Denetim kaydı saklama (gün)" hint="min 365 — PCI 10.5.1"><Input type="number" value={s.auditRetentionDays} onChange={(e) => set("auditRetentionDays", num(e.target.value, 365))} /></Field>
          <div className="flex items-center pb-1.5">
            <Switch checked={s.requirePasswordComplexity} onChange={(v) => set("requirePasswordComplexity", v)} label="Karmaşıklık zorunlu (büyük/küçük + rakam)" />
          </div>
        </div>
      </Section>

      {/* 8. SIEM — Basic pakette kilitli */}
      <Section icon={<Radio className="h-4 w-4" />} title="SIEM / Syslog Aktarımı"
        action={<Button variant="outline" size="sm" disabled={!siemLicensed} onClick={() => runTest("syslog", testSyslog)}><Radio className="h-4 w-4" /> Test</Button>}>
        {!siemLicensed && (
          <LicenseLockNote>SIEM/Syslog log aktarımı Standard ve Enterprise paketlerde kullanılabilir. Basic pakette açılamaz.</LicenseLockNote>
        )}
        <div className={cn(!siemLicensed && "pointer-events-none opacity-50")}>
          <div className="grid items-center gap-3 md:grid-cols-2 xl:grid-cols-4">
            <Switch checked={s.syslogEnabled && siemLicensed} onChange={(v) => { if (siemLicensed) set("syslogEnabled", v); }} label="Denetim kayıtlarını syslog'a ilet" />
            <Field label="Sunucu"><Input value={s.syslogHost} onChange={(e) => set("syslogHost", e.target.value)} placeholder="siem.firma.local" /></Field>
            <Field label="Port"><Input type="number" value={s.syslogPort} onChange={(e) => set("syslogPort", num(e.target.value, 514))} /></Field>
            <Switch checked={s.syslogTcp} onChange={(v) => set("syslogTcp", v)} label="TCP kullan (varsayılan UDP)" />
          </div>
        </div>
        <TestNote k="syslog" msg={testMsg} />
      </Section>

      {/* 9. Durum sayfası (herkese açık) */}
      <Section icon={<Globe className="h-4 w-4" />} title="Durum Sayfası (Herkese Açık)">
        <div className="grid items-center gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Switch checked={s.statusPageEnabled} onChange={(v) => set("statusPageEnabled", v)} label="Durum sayfası yayında (giriş gerektirmez)" />
          <Field label="Sayfa başlığı" hint="boş bırakılırsa şirket adı gösterilir">
            <Input value={s.statusPageTitle} onChange={(e) => set("statusPageTitle", e.target.value)} placeholder="Örn. Şirket Sistem Durumu" />
          </Field>
          <div className="flex flex-wrap items-center gap-2">
            <code className="rounded bg-muted px-2 py-1 text-xs">{window.location.origin + "/durum"}</code>
            <Button variant="ghost" size="sm" onClick={() => navigator.clipboard.writeText(window.location.origin + "/durum")}>Kopyala</Button>
            <Button variant="ghost" size="sm" onClick={() => window.open("/durum", "_blank")}>Sayfayı aç</Button>
          </div>
        </div>
        <p className="text-xs text-muted-foreground">
          {'İzleme formunda "Durum sayfasında göster" işaretlenen izlemeler bu sayfada listelenir. Sayfada yalnız izleme adı ve durum görünür; sunucu, adres ve hata detayı asla gösterilmez.'}
        </p>
      </Section>

      {/* 10. EOL */}
      <Section icon={<CalendarClock className="h-4 w-4" />} title="Destek Sonu (EOL)"
        action={<Button variant="outline" size="sm" onClick={() => runTest("eol", eolSyncNow)}><CalendarClock className="h-4 w-4" /> Senkronize Et</Button>}>
        <div className="grid items-center gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Switch checked={s.eolEnabled} onChange={(v) => set("eolEnabled", v)} label="endoflife.date verisiyle EOL takibi" />
          <Field label="Uyarı eşiği (gün)"><Input type="number" value={s.eolWarnDays} onChange={(e) => set("eolWarnDays", num(e.target.value, 90))} /></Field>
          <Field label="Proxy URL" hint="kapalı ağda opsiyonel"><Input value={s.eolProxyUrl} onChange={(e) => set("eolProxyUrl", e.target.value)} /></Field>
          <div className="flex items-center gap-2">
            <input ref={eolFileRef} type="file" accept=".json" className="hidden" onChange={onEolImport} />
            <Button variant="ghost" size="sm" onClick={() => eolFileRef.current?.click()}>JSON içe aktar (kapalı ağ)</Button>
          </div>
        </div>
        <TestNote k="eol" msg={testMsg} />
      </Section>

      {/* 11. Yedekleme (zamanlanmış) */}
      <Section icon={<Database className="h-4 w-4" />} title="Yedekleme (zamanlanmış)">
        <div className="flex flex-wrap items-center gap-6">
          <Switch checked={s.backupEnabled} onChange={(v) => set("backupEnabled", v)} label="Günlük otomatik yedek (yalnız SQLite)" />
          <Switch checked={s.backupEncrypt} onChange={(v) => set("backupEncrypt", v)} label="Yedekleri şifrele (AES-256)" />
        </div>
        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
          <div className="xl:col-span-2"><Field label="Yedek klasörü"><Input value={s.backupPath} onChange={(e) => set("backupPath", e.target.value)} placeholder="D:\vmon-backup" /></Field></div>
          <Field label="Saat"><Input type="number" value={s.backupHour} onChange={(e) => set("backupHour", num(e.target.value, 2))} /></Field>
          <Field label="Dakika"><Input type="number" value={s.backupMinute} onChange={(e) => set("backupMinute", num(e.target.value, 0))} /></Field>
          <Field label="Saklanan"><Input type="number" value={s.backupRetentionCount} onChange={(e) => set("backupRetentionCount", num(e.target.value, 14))} /></Field>
          <Field label="Şifreleme parolası" hint={s.hasBackupPassword ? "kayıtlı — değiştirmek için yeni girin. Parola kaybolursa yedek açılamaz!" : "Parola kaybolursa yedek açılamaz!"}>
            <Input type="password" autoComplete="new-password" value={newBk} onChange={(e) => setNewBk(e.target.value)}
              placeholder={s.hasBackupPassword ? "•••••• (değiştirmek için girin)" : "parola"} />
          </Field>
        </div>
      </Section>

      {/* 12. Yedekler listesi (anında uygulanır) */}
      <BackupsCard />

      {/* 13. Mutabakat */}
      <Section icon={<Building2 className="h-4 w-4" />} title="Mutabakat (Envanter Karşılaştırma)">
        <div className="grid items-center gap-3 md:grid-cols-2 xl:grid-cols-3">
          <Switch checked={s.mutabakatEnabled} onChange={(v) => set("mutabakatEnabled", v)} label="Mutabakat ekranı açık" />
          <Field label="Bizim şirket"><Input value={s.mutabakatOwnCompany} onChange={(e) => set("mutabakatOwnCompany", e.target.value)} /></Field>
          <Field label="Hizmet sağlayıcı"><Input value={s.mutabakatVendorCompany} onChange={(e) => set("mutabakatVendorCompany", e.target.value)} /></Field>
        </div>
      </Section>

      {/* 14. Logo (anında uygulanır) */}
      <LogoCard current={s.loginLogoFile} onChanged={() => getSettings().then(setS).catch(() => {})} />

      {/* 15. Lisans (anında uygulanır) */}
      <LicenseCard onChanged={reloadMe} />

      {/* 16. Güncelleme: en altta tam genişlik */}
      <UpdateCard />

      {/* Kaydet çubuğu */}
      <div className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-background/80 px-5 py-3 backdrop-blur lg:left-44">
        <div className="mx-auto flex max-w-[1500px] items-center justify-end gap-3">
          <span className="hidden text-xs text-muted-foreground sm:inline">Üstteki ayarlar tek seferde kaydedilir; kanallar / yedekler / logo anında uygulanır.</span>
          <Button onClick={doSave} disabled={saving}><Save className="h-4 w-4" /> {saving ? "Kaydediliyor…" : "Ayarları Kaydet"}</Button>
        </div>
      </div>
    </div>
  );
}

function Section({ icon, title, action, children }: { icon: ReactNode; title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="flex items-center gap-2 text-base">{icon} {title}</CardTitle>
        {action}
      </CardHeader>
      <CardContent className="space-y-3">{children}</CardContent>
    </Card>
  );
}

function TestRow({ k, msg, children }: { k: string; msg: Record<string, { ok: boolean; msg: string }>; children: ReactNode }) {
  const r = msg[k];
  return (
    <div className="rounded-lg border border-border/60 bg-muted/20 p-3">
      <div className="flex flex-wrap items-center gap-2">{children}</div>
      {r && <p className={cn("mt-2 text-xs", r.ok ? "text-emerald-400" : "text-rose-400")}>{r.msg}</p>}
    </div>
  );
}
function TestNote({ k, msg }: { k: string; msg: Record<string, { ok: boolean; msg: string }> }) {
  const r = msg[k];
  if (!r) return null;
  return <p className={cn("text-xs", r.ok ? "text-emerald-400" : "text-rose-400")}>{r.msg}</p>;
}

function SettingsSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 6 }).map((_, i) => (
        <Card key={i} className="p-5 space-y-3">
          <Skeleton className="h-4 w-40" />
          <div className="grid gap-3 md:grid-cols-3">
            {Array.from({ length: 3 }).map((_, j) => <Skeleton key={j} className="h-9 w-full" />)}
          </div>
        </Card>
      ))}
    </div>
  );
}
