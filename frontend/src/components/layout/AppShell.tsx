import { useEffect, type ReactNode } from "react";
import { motion } from "framer-motion";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";
import { useMe } from "@/hooks/useMe";
import { logout } from "@/lib/me";

/** Boşta oturum oto-kapatma — 15 dk hareketsizlik (PCI DSS 8.2.8, klasik arayüzle aynı). */
function useIdleLogout(enabled: boolean) {
  useEffect(() => {
    if (!enabled) return;
    const IDLE_MS = 15 * 60 * 1000;
    let t: ReturnType<typeof setTimeout>;
    const reset = () => { clearTimeout(t); t = setTimeout(() => { logout().catch(() => { window.location.href = "/Account/Login"; }); }, IDLE_MS); };
    const evs = ["mousemove", "keydown", "click", "scroll", "touchstart"] as const;
    evs.forEach((ev) => window.addEventListener(ev, reset, { passive: true }));
    reset();
    return () => { clearTimeout(t); evs.forEach((ev) => window.removeEventListener(ev, reset)); };
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
