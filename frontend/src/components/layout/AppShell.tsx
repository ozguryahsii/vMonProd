import { useEffect, type ReactNode } from "react";
import { motion } from "framer-motion";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";
import { useMe } from "@/hooks/useMe";
import { logout } from "@/lib/me";

/** Boşta oturum oto-kapatma — 15 dk GERÇEK hareketsizlik (PCI DSS 8.2.8).
 * Önceki sürümdeki hata: 'scroll' iç kaydırma alanlarından window'a kabarcıklanmaz →
 * kullanıcı aktifken sayaç sıfırlanmıyordu. Çözüm:
 *  - capture:true + geniş olay seti (pointermove/wheel dahil) → HER etkileşim sayılır
 *  - setTimeout yerine son-aktivite damgası + 30sn'lik kontrol (uyku/sekme dönüşünde de doğru)
 *  - aktifken 5 dk'da bir sunucuya heartbeat → kayan cookie süresi de tazelenir */
function useIdleLogout(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return;
    const IDLE_MS = 15 * 60 * 1000;
    let last = Date.now();
    let lastBeat = Date.now();
    const activity = () => { last = Date.now(); };

    const evs = ["pointermove", "mousemove", "mousedown", "keydown", "click", "scroll", "wheel", "touchstart"] as const;
    evs.forEach((ev) => window.addEventListener(ev, activity, { passive: true, capture: true }));
    const onVis = () => { if (document.visibilityState === "visible") activity(); };
    document.addEventListener("visibilitychange", onVis);

    const iv = setInterval(() => {
      const idle = Date.now() - last;
      if (idle >= IDLE_MS) {
        logout().catch(() => { window.location.href = "/Account/Login"; });
        return;
      }
      // Aktifken sunucu oturumunu canlı tut (kayan süre tazelenir)
      if (Date.now() - lastBeat >= 5 * 60 * 1000 && idle < 5 * 60 * 1000) {
        lastBeat = Date.now();
        fetch("/api/me", { credentials: "same-origin" }).catch(() => {});
      }
    }, 30 * 1000);

    return () => {
      clearInterval(iv);
      evs.forEach((ev) => window.removeEventListener(ev, activity, { capture: true } as EventListenerOptions));
      document.removeEventListener("visibilitychange", onVis);
    };
  }, [enabled]);
}

export function AppShell({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: ReactNode;
}) {
  const { me } = useMe();
  useIdleLogout(!!me?.authEnabled);
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar title={title} subtitle={subtitle} />
        <motion.main
          className="flex-1 p-5"
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.38, ease: [0.16, 1, 0.3, 1] }}
        >
          <div className="mx-auto max-w-[1500px]">{children}</div>
        </motion.main>
      </div>
    </div>
  );
}
