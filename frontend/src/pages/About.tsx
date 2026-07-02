import { useEffect, useState } from "react";
import { Activity, ExternalLink, ShieldCheck, GitBranch } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { useMe } from "@/hooks/useMe";

export function About() {
  const { me } = useMe();
  const [version, setVersion] = useState<string | null>(null);

  useEffect(() => {
    // Sürüm rozeti klasik Hakkında sayfasından okunur (tek kaynak — çifte bakım olmasın)
    fetch("/Home/About", { credentials: "same-origin" })
      .then((r) => r.text())
      .then((html) => {
        const m = html.match(/v\d+\.\d+\.\d+[^<\s"]*/);
        setVersion(m ? m[0] : null);
      })
      .catch(() => setVersion(null));
  }, []);

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Card>
        <CardHeader className="flex-row items-center gap-3 space-y-0">
          <span className="grid h-12 w-12 place-items-center rounded-xl bg-gradient-to-br from-primary to-primary/70 shadow-lg shadow-primary/30">
            <Activity className="h-6 w-6 text-white" />
          </span>
          <div>
            <CardTitle className="flex items-center gap-2">
              vMon
              {version && <span className="rounded-md bg-primary/15 px-2 py-0.5 text-xs font-semibold text-primary">{version}</span>}
            </CardTitle>
            <CardDescription>Servis izleme ve alarm platformu {me?.companyName ? `— ${me.companyName}` : ""}</CardDescription>
          </div>
        </CardHeader>
        <CardContent className="space-y-3 text-sm text-muted-foreground">
          <p>
            HTTP/TCP/Ping/DNS/SMTP/IMAP/LDAP/SFTP/SQL sunucuları, Windows &amp; Linux servisleri ve sunucu sağlığı
            (CPU/RAM/Disk) tek panodan izlenir; e-posta, SMS, WhatsApp ve sesli arama ile alarm üretir.
          </p>
          <div className="flex items-center gap-2 rounded-lg border border-border/60 bg-muted/20 px-3 py-2">
            <ShieldCheck className="h-4 w-4 text-emerald-400" />
            Hash-zincirli değiştirilemez denetim kaydı, CSP/güvenlik başlıkları, parola politikası,
            SIEM aktarımı, şifreli yedekler — PCI DSS / ISO 27001 / NIST kontrolleriyle eşlenmiş.
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base"><GitBranch className="h-4 w-4" /> Sürüm Geçmişi ve Uyumluluk Tablosu</CardTitle>
          <CardDescription>Tam değişiklik listesi ve regülasyon eşlemeleri klasik görünümde</CardDescription>
        </CardHeader>
        <CardContent>
          <a href="/Home/About" target="_blank" rel="noreferrer">
            <Button variant="outline" size="sm"><ExternalLink className="h-4 w-4" /> Klasik Hakkında sayfasını aç</Button>
          </a>
        </CardContent>
      </Card>
    </div>
  );
}
