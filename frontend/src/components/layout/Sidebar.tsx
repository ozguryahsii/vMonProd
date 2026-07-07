import { useEffect, useState } from "react";
import { NavLink } from "react-router-dom";
import { motion } from "framer-motion";
import {
  LayoutDashboard, BarChart3, Activity, Server, KeyRound,
  Settings, Users, ClipboardCheck, Info, ChevronsLeft, ChevronsRight, ClipboardList, X,
} from "lucide-react";
import { useMe } from "@/hooks/useMe";
import { cn } from "@/lib/utils";

// perm: gerekli yetki anahtarı; admin: yalnız uygulama adminleri; flag: settings bayrağı
const allNav = [
  { to: "/app/dashboard", label: "Dashboard'lar", icon: LayoutDashboard, perm: "dashboards.view" },
  { to: "/app/reports", label: "Raporlar", icon: BarChart3, perm: "dashboards.view" },
  { to: "/app/statistics", label: "İstatistikler", icon: Activity, perm: "dashboards.view" },
  { to: "/app/services", label: "İzlemeler", icon: Server, perm: "services.manage" },
  { to: "/app/credentials", label: "Kimlik Bilgileri", icon: KeyRound, perm: "credentials.manage" },
  { to: "/app/settings", label: "Ayarlar", icon: Settings, admin: true },
  { to: "/app/users", label: "Kullanıcılar", icon: Users, admin: true },
  { to: "/app/audit", label: "Denetim", icon: ClipboardCheck, admin: true },
  { to: "/app/mutabakat", label: "Mutabakat", icon: ClipboardList, perm: "mutabakat.view", flag: "mutabakat" },
  { to: "/app/about", label: "Hakkında", icon: Info },
] as const;

export function Sidebar({ mobileOpen = false, onClose }: { mobileOpen?: boolean; onClose?: () => void } = {}) {
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem("vmon.sidebar") === "min");
  useEffect(() => { localStorage.setItem("vmon.sidebar", collapsed ? "min" : "max"); }, [collapsed]);
  const { me, hasPerm } = useMe();

  // Yetkiye göre menü: me yüklenene dek yalnız Hakkında gizlenmesin diye tümü gösterilmez — boş bekleriz
  const nav = allNav.filter((item) => {
    if ("flag" in item && item.flag === "mutabakat" && !me?.mutabakatEnabled) return false;
    if ("admin" in item && item.admin) return !!me?.isAdmin;
    if ("perm" in item && item.perm) return hasPerm(item.perm);
    return true;
  });

  // Menü bağlantıları — hem masaüstü (daraltılabilir) hem mobil drawer (hep açık) kullanır
  const navLinks = (expanded: boolean, onNavigate?: () => void) => nav.map((item) => (
    <NavLink
      key={item.to}
      to={item.to}
      onClick={onNavigate}
      title={!expanded ? item.label : undefined}
      className={({ isActive }) =>
        cn(
          "group relative flex items-center gap-3 rounded-xl py-2.5 text-sm font-medium transition-colors",
          expanded ? "px-3" : "justify-center px-0",
          isActive ? "text-foreground" : "text-muted-foreground hover:text-foreground hover:bg-accent/60"
        )
      }
    >
      {({ isActive }) => (
        <>
          {isActive && (
            <motion.span
              layoutId={expanded ? "sidebar-active" : "sidebar-active-m"}
              className="absolute inset-0 rounded-xl bg-primary/15 ring-1 ring-inset ring-primary/30"
              transition={{ type: "spring", stiffness: 500, damping: 40 }}
            />
          )}
          <item.icon className="relative z-10 h-4 w-4 shrink-0 transition-transform group-hover:scale-110" />
          {expanded && <span className="relative z-10 truncate">{item.label}</span>}
        </>
      )}
    </NavLink>
  ));

  return (
    <>
    {/* Mobil / dar ekran: üstten kayan overlay drawer (lg altında) */}
    <div className={cn("fixed inset-0 z-50 lg:hidden", mobileOpen ? "" : "pointer-events-none")}>
      <div className={cn("absolute inset-0 bg-black/50 backdrop-blur-sm transition-opacity", mobileOpen ? "opacity-100" : "opacity-0")}
        onClick={onClose} />
      <aside className={cn(
        "absolute left-0 top-0 flex h-full w-60 flex-col border-r border-border bg-card shadow-2xl transition-transform duration-200",
        mobileOpen ? "translate-x-0" : "-translate-x-full"
      )}>
        <div className="flex h-16 items-center gap-2.5 px-5">
          <NavLink to="/app/dashboard" onClick={onClose} className="logo-beat grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-primary to-primary/70 shadow-lg shadow-primary/30">
            <Activity className="h-5 w-5 text-white" />
          </NavLink>
          <div className="flex min-w-0 flex-col justify-center leading-none">
            <span className="text-lg font-bold tracking-tight">vMon</span>
            {me?.license && (
              <span className={cn("mt-1 w-fit rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide",
                me.license.edition === "Enterprise" ? "bg-amber-500/15 text-amber-400"
                  : me.license.edition === "Standard" ? "bg-sky-500/15 text-sky-400" : "bg-primary/15 text-primary")}>
                {me.license.edition}
              </span>
            )}
          </div>
          <button onClick={onClose} className="ml-auto rounded-lg p-1.5 text-muted-foreground hover:bg-accent/60" title="Kapat">
            <X className="h-5 w-5" />
          </button>
        </div>
        <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">{navLinks(true, onClose)}</nav>
      </aside>
    </div>

    <aside className={cn(
      "sticky top-0 hidden h-screen shrink-0 flex-col border-r border-border bg-card/40 backdrop-blur-xl transition-[width] duration-200 lg:flex",
      collapsed ? "w-16" : "w-44"
    )}>
      <div className={cn("flex h-16 items-center gap-2.5", collapsed ? "justify-center px-0" : "px-5")}>
        {/* Logo tıklanınca ana ekrana (Dashboard'lar) döner */}
        <NavLink to="/app/dashboard" title="Dashboard'lar"
          className="logo-beat grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-primary to-primary/70 shadow-lg shadow-primary/30">
          <Activity className="h-5 w-5 text-white" />
        </NavLink>
        {!collapsed && (
          <div className="flex min-w-0 flex-col justify-center leading-none">
            <NavLink to="/app/dashboard" className="text-lg font-bold tracking-tight hover:opacity-80">vMon</NavLink>
            {/* Lisans Fazı L1: paket rozeti "vMon" ALTINDA (dar yan panele sığar; eski PRE rozetinin yeri) */}
            <span
              title={me?.license ? `${me.license.company} · bitiş: ${me.license.expires} (${me.license.daysLeft} gün)` : undefined}
              className={cn(
                "mt-1 w-fit rounded px-1.5 py-0.5 text-[9px] font-semibold uppercase tracking-wide",
                me?.license?.edition === "Enterprise" ? "bg-amber-500/15 text-amber-400"
                  : me?.license?.edition === "Standard" ? "bg-sky-500/15 text-sky-400"
                  : "bg-primary/15 text-primary"
              )}
            >
              {me?.license?.edition ?? "…"}
            </span>
          </div>
        )}
      </div>

      <nav className={cn("flex-1 space-y-1 py-4", collapsed ? "px-2" : "px-3")}>
        {navLinks(!collapsed)}
      </nav>

      <div className={cn("border-t border-border p-3", collapsed && "px-2")}>
        {!collapsed && (
          <div className="mb-2 flex items-center gap-2 px-1 text-xs text-muted-foreground">
            <span className="h-2 w-2 animate-pulse rounded-full bg-success" /> Tüm sistemler çalışıyor
          </div>
        )}
        <button
          onClick={() => setCollapsed((c) => !c)}
          title={collapsed ? "Menüyü genişlet" : "Menüyü daralt"}
          className={cn(
            "flex w-full items-center gap-2 rounded-lg py-2 text-xs text-muted-foreground transition hover:bg-accent/60 hover:text-foreground",
            collapsed ? "justify-center px-0" : "px-2"
          )}
        >
          {collapsed ? <ChevronsRight className="h-4 w-4" /> : (<><ChevronsLeft className="h-4 w-4" /> Daralt</>)}
        </button>
      </div>
    </aside>
    </>
  );
}
