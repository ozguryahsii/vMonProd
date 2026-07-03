import { useEffect, useState } from "react";
import { Activity } from "lucide-react";
import { Card, CardHeader, CardTitle, CardDescription } from "@/components/ui/card";
import { Skeleton, ErrorState } from "@/components/ui/states";
import { useMe } from "@/hooks/useMe";

/** Klasik Hakkında içeriği (genel bilgiler + sürüm geçmişi + regülasyon uyumları) tek kaynaktan
 * çekilir ve YENİ tasarımın stiliyle (about-legacy CSS eşlemesi) sayfanın içinde render edilir.
 * iframe YOK; klasik arayüz emekli olduğunda içerik doğrudan buraya taşınacak. */
export function About() {
  const { me } = useMe();
  const [html, setHtml] = useState<string | null>(null);
  const [version, setVersion] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/Home/About", { credentials: "same-origin" })
      .then((r) => r.text())
      .then((raw) => {
        const doc = new DOMParser().parseFromString(raw, "text/html");
        const content = doc.querySelector("main .max-w-4xl, .max-w-4xl");
        if (!content) { setError("İçerik alınamadı."); return; }
        // Kendi başlık kartımız var → klasik hero'yu çıkar; script/Alpine kalıntılarını temizle
        const hero = content.querySelector("h1")?.closest("div.rounded-2xl");
        hero?.remove();
        content.querySelectorAll("script").forEach((s) => s.remove());
        // Alpine yok → x-show bölgelerini yerel <details> akordeonuna çevir (KAPALI başlar,
        // tıklayınca açılır — sürüm notları ve regülasyon uyumları böylece eskisi gibi katlanır)
        content.querySelectorAll("[x-cloak]").forEach((el) => el.removeAttribute("x-cloak"));
        content.querySelectorAll("[x-show]").forEach((el) => {
          const scope = el.closest("[x-data]") ?? el.parentElement;
          const btn = scope?.querySelector("button");
          const details = doc.createElement("details");
          const summary = doc.createElement("summary");
          summary.innerHTML = btn ? btn.innerHTML : "Detayları göster";
          btn?.remove();
          el.parentElement?.insertBefore(details, el);
          details.appendChild(summary);
          details.appendChild(el);
          el.removeAttribute("x-show");
        });
        const m = raw.match(/v\d+\.\d+\.\d+[^<\s"]*/);
        setVersion(m ? m[0] : null);
        setHtml(content.innerHTML);
      })
      .catch((e) => setError((e as Error).message));
  }, []);

  return (
    <div className="mx-auto max-w-6xl space-y-4">
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
      </Card>

      {error ? (
        <ErrorState message={error} />
      ) : html === null ? (
        <Card className="p-6 space-y-3">{Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-10 w-full" />)}</Card>
      ) : (
        <div className="about-legacy" dangerouslySetInnerHTML={{ __html: html }} />
      )}
    </div>
  );
}
