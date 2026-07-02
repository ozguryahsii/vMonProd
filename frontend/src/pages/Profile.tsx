import { useEffect, useState } from "react";
import { UserCircle, Save, CheckCircle2, XCircle, KeyRound } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { Skeleton, ErrorState } from "@/components/ui/states";
import { apiGet, apiSend } from "@/lib/api";
import { cn } from "@/lib/utils";

interface ProfileData {
  sam: string; displayName: string | null; email: string | null; phone: string | null;
  isLocal: boolean; minPasswordLength: number; requireComplexity: boolean;
}

export function Profile() {
  const [p, setP] = useState<ProfileData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [cur, setCur] = useState("");
  const [pw1, setPw1] = useState("");
  const [pw2, setPw2] = useState("");
  const [saving, setSaving] = useState(false);
  const [flash, setFlash] = useState<{ ok: boolean; msg: string } | null>(null);

  useEffect(() => {
    apiGet<ProfileData>("/profile")
      .then((d) => { setP(d); setEmail(d.email ?? ""); setPhone(d.phone ?? ""); })
      .catch((e) => setError((e as Error).message));
  }, []);
  useEffect(() => { if (flash) { const t = setTimeout(() => setFlash(null), 4000); return () => clearTimeout(t); } }, [flash]);

  async function save() {
    setSaving(true);
    try {
      const r = await apiSend<{ ok: boolean; message: string }>("POST", "/profile", {
        email: email.trim() || null, phone: phone.trim() || null,
        currentPassword: cur || null, newPassword: pw1 || null, confirmPassword: pw2 || null,
      });
      setFlash({ ok: true, msg: r.message });
      setCur(""); setPw1(""); setPw2("");
    } catch (e) { setFlash({ ok: false, msg: (e as Error).message }); }
    finally { setSaving(false); }
  }

  if (error) return <ErrorState message={error} />;
  if (!p) return <Card className="mx-auto max-w-xl p-6 space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-10 w-full" />)}</Card>;

  return (
    <div className="mx-auto max-w-xl space-y-4">
      {flash && (
        <div className={cn("flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm",
          flash.ok ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-400" : "border-destructive/30 bg-destructive/10 text-destructive")}>
          {flash.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />} {flash.msg}
        </div>
      )}

      <Card>
        <CardHeader className="flex-row items-center gap-3 space-y-0">
          <span className="grid h-11 w-11 place-items-center rounded-xl bg-primary/15 text-primary"><UserCircle className="h-6 w-6" /></span>
          <div>
            <CardTitle>{p.displayName ?? p.sam}</CardTitle>
            <CardDescription>{p.sam} {p.isLocal ? "· yerel kullanıcı" : "· AD kullanıcısı"}</CardDescription>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <Field label="E-posta (OTP: e-posta kanalı)"><Input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="ad.soyad@firma.com" /></Field>
          <Field label="Telefon (OTP: SMS/WhatsApp kanalı, E.164)"><Input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+905xxxxxxxxx" /></Field>

          {p.isLocal && (
            <div className="space-y-3 rounded-lg border border-border/60 bg-muted/20 p-4">
              <p className="flex items-center gap-2 text-sm font-semibold"><KeyRound className="h-4 w-4 text-muted-foreground" /> Şifre Değiştir (yerel kullanıcı)</p>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <Input type="password" autoComplete="current-password" value={cur} onChange={(e) => setCur(e.target.value)} placeholder="Mevcut şifre" />
                <Input type="password" autoComplete="new-password" value={pw1} onChange={(e) => setPw1(e.target.value)} placeholder="Yeni şifre" />
                <Input type="password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} placeholder="Yeni şifre (tekrar)" />
              </div>
              <p className="text-xs text-muted-foreground">
                Boş bırakırsanız şifre değişmez. Politika: en az {p.minPasswordLength} karakter{p.requireComplexity ? " + büyük/küçük harf + rakam" : ""}.
              </p>
            </div>
          )}

          <div className="flex justify-end">
            <Button onClick={save} disabled={saving}><Save className="h-4 w-4" /> {saving ? "Kaydediliyor…" : "Kaydet"}</Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
