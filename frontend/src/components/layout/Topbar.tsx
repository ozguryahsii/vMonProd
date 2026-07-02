import { useEffect, useState } from "react";
import { Moon, Sun, Search, Bell, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";

export function Topbar({ title, subtitle }: { title: string; subtitle?: string }) {
  const [dark, setDark] = useState(true);

  useEffect(() => {
    const root = document.documentElement;
    root.classList.toggle("dark", dark);
  }, [dark]);

  return (
    <header className="sticky top-0 z-30 flex h-16 items-center gap-4 border-b border-border bg-background/70 px-5 backdrop-blur-xl">
      <div className="min-w-0">
        <h1 className="truncate text-lg font-semibold leading-tight">{title}</h1>
        {subtitle && <p className="truncate text-xs text-muted-foreground">{subtitle}</p>}
      </div>

      <div className="relative ml-auto hidden md:block">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <input
          placeholder="Servis, rapor ara…"
          className="h-9 w-64 rounded-lg border border-input bg-card/60 pl-9 pr-3 text-sm outline-none transition focus:border-primary focus:ring-2 focus:ring-ring/40"
        />
      </div>

      <Button variant="ghost" size="icon" className="relative">
        <Bell className="h-4 w-4" />
        <span className="absolute right-2 top-2 h-1.5 w-1.5 rounded-full bg-primary" />
      </Button>

      <Button variant="ghost" size="icon" onClick={() => setDark((d) => !d)} title="Tema">
        {dark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
      </Button>

      <button className="flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm transition hover:bg-accent">
        <span className="grid h-7 w-7 place-items-center rounded-full bg-gradient-to-br from-primary to-primary/60 text-xs font-bold text-white">
          A
        </span>
        <span className="hidden font-medium sm:inline">admin</span>
        <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
      </button>
    </header>
  );
}
