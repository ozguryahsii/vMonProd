import { useEffect, useState } from "react";
import { Activity, ExternalLink } from "lucide-react";
import { Card, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
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
    <div className="mx-auto max-w-6xl space-y-4">
      <Card>
        <CardHeader className="flex-row items-center justify-between space-y-0">
          <div className="flex items-center gap-3">
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
          </div>
          <a href="/Home/About" target="_blank" rel="noreferrer">
            <Button variant="outline" size="sm"><ExternalLink className="h-4 w-4" /> Yeni sekmede aç</Button>
          </a>
        </CardHeader>
      </Card>

      {/* Klasik Hakkında içeriği (genel bilgiler, sürüm geçmişi, regülasyon uyumları) — tek kaynak, gömülü */}
      <div className="overflow-hidden rounded-lg border border-border bg-card/40">
        <iframe
          src="/Home/About?embed=1"
          title="Hakkında"
          className="h-[78vh] w-full"
          style={{ border: "none" }}
        />
      </div>
    </div>
  );
}
