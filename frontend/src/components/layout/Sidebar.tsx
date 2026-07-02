import { useEffect, useState } from "react";
import { NavLink } from "react-router-dom";
import { motion } from "framer-motion";
import {
  LayoutDashboard, BarChart3, Activity, Server, KeyRound,
  Settings, Users, ClipboardCheck, Info, ChevronsLeft, ChevronsRight,
} from "lucide-react";
import { cn } from "@/lib/utils";

const nav = [
  { to: "/app/dashboard", label: "Dashboard'lar", icon: LayoutDashboard },
  { to: "/app/reports", label: "Raporlar", icon: BarChart3 },
  { to: "/app/statistics", label: "İstatistikler", icon: Activity },
  { to: "/app/services", label: "Servisler", icon: Server },
  { to: "/app/credentials", label: "Kimlik Bilgileri", icon: KeyRound },
  { to: "/app/settings", label: "Ayarlar", icon: Settings },
  { to: "/app/users", label: "Kullanıcılar", icon: Users },
  { to: "/app/audit", label: "Denetim", icon: ClipboardCheck },
  { to: "/app/about", label: "Hakkında", icon: Info },
];

export function Sidebar() {
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem("vmon.sidebar") === "min");
  useEffect(() => { localStorage.setItem("vmon.sidebar", collapsed ? "min" : "max"); }, [collapsed]);

  return (
    <aside className={cn(
      "sticky top-0 hidden h-screen shrink-0 flex-col border-r border-border bg-card/40 backdrop-blur-xl transition-[width] duration-200 lg:flex",
      collapsed ? "w-16" : "w-64"
    )}>
      <div className={cn("flex h-16 items-center gap-2.5", collapsed ? "justify-center px-0" : "px-5")}>
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-primary to-primary/70 shadow-lg shadow-primary/30">
          <Activity className="h-5 w-5 text-white" />
        </span>
        {!collapsed && (
          <>
            <span className="text-lg font-bold tracking-tight">vMon</span>
            <span className="ml-auto rounded-md bg-primary/15 px-1.5 py-0.5 text-[10px] font-semibold text-primary">PRE</span>
          </>
        )}
      </div>

      <nav className={cn("flex-1 space-y-1 py-4", collapsed ? "px-2" : "px-3")}>
        {nav.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            title={collapsed ? item.label : undefined}
            className={({ isActive }) =>
              cn(
                "group relative flex items-center gap-3 rounded-xl py-2.5 text-sm font-medium transition-colors",
                collapsed ? "justify-center px-0" : "px-3",
                isActive ? "text-foreground" : "text-muted-foreground hover:text-foreground hover:bg-accent/60"
              )
            }
          >
            {({ isActive }) => (
              <>
                {isActive && (
                  <motion.span
                    layoutId="sidebar-active"
                    className="absolute inset-0 rounded-xl bg-primary/15 ring-1 ring-inset ring-primary/30"
                    transition={{ type: "spring", stiffness: 500, damping: 40 }}
                  />
                )}
                <item.icon className="relative z-10 h-4 w-4 shrink-0 transition-transform group-hover:scale-110" />
                {!collapsed && <span className="relative z-10 truncate">{item.label}</span>}
              </>
            )}
          </NavLink>
        ))}
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
  );
}
