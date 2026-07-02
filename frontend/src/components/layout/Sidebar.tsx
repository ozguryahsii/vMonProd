import { NavLink } from "react-router-dom";
import { motion } from "framer-motion";
import {
  LayoutDashboard, BarChart3, Activity, Server, KeyRound,
  Settings, Users, ClipboardCheck, Info,
} from "lucide-react";
import { cn } from "@/lib/utils";

const nav = [
  { to: "/app/dashboard", label: "Dashboard", icon: LayoutDashboard },
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
  return (
    <aside className="sticky top-0 hidden h-screen w-64 shrink-0 flex-col border-r border-border bg-card/40 backdrop-blur-xl lg:flex">
      <div className="flex h-16 items-center gap-2.5 px-5">
        <span className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-primary to-primary/70 shadow-lg shadow-primary/30">
          <Activity className="h-5 w-5 text-white" />
        </span>
        <span className="text-lg font-bold tracking-tight">vMon</span>
        <span className="ml-auto rounded-md bg-primary/15 px-1.5 py-0.5 text-[10px] font-semibold text-primary">
          PRE
        </span>
      </div>

      <nav className="flex-1 space-y-1 px-3 py-4">
        {nav.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                "group relative flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors",
                isActive
                  ? "text-foreground"
                  : "text-muted-foreground hover:text-foreground hover:bg-accent/60"
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
                <item.icon className="relative z-10 h-4 w-4 transition-transform group-hover:scale-110" />
                <span className="relative z-10">{item.label}</span>
              </>
            )}
          </NavLink>
        ))}
      </nav>

      <div className="border-t border-border p-4 text-xs text-muted-foreground">
        <div className="flex items-center gap-2">
          <span className="h-2 w-2 animate-pulse rounded-full bg-success" />
          Tüm sistemler çalışıyor
        </div>
      </div>
    </aside>
  );
}
