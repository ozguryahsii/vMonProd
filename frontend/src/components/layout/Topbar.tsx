import { useEffect, useRef, useState } from "react";
import { Moon, Sun, Search, ChevronDown, LogOut, UserCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useMe } from "@/hooks/useMe";
import { setPreference, logout } from "@/lib/me";
import { cn } from "@/lib/utils";

export function Topbar({ title, subtitle }: { title: string; subtitle?: string }) {
  const { me } = useMe();
  const [dark, setDark] = useState(() => document.documentElement.classList.contains("dark"));
  const [menuOpen, setMenuOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  // me yüklenince tema durumunu eşitle
  useEffect(() => { if (me) setDark(me.theme !== "light"); }, [me]);

  useEffect(() => {
    const onDoc = (e: MouseEvent) => { if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false); };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, []);

  function toggleTheme() {
    const next = !dark;
    setDark(next);
    document.documentElement.classList.toggle("dark", next);
    setPreference({ theme: next ? "dark" : "light" }).catch(() => {});
  }
  function setLang(lang: "tr" | "en") {
    setPreference({ lang }).finally(() => window.location.reload());
  }

  const name = me?.displayName ?? me?.sam ?? "—";
  const initial = (name ?? "?").charAt(0).toUpperCase();
  const lang = (document.documentElement.lang === "en" ? "en" : "tr") as "tr" | "en";

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

      <Button variant="ghost" size="icon" onClick={toggleTheme} title="Tema">
        {dark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
      </Button>

      <div className="relative" ref={menuRef}>
        <button onClick={() => setMenuOpen((o) => !o)} className="flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm transition hover:bg-accent">
          <span className="grid h-7 w-7 place-items-center rounded-full bg-gradient-to-br from-primary to-primary/60 text-xs font-bold text-white">{initial}</span>
          <span className="hidden max-w-[140px] truncate font-medium sm:inline">{name}</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
        </button>
        {menuOpen && (
          <div className="absolute right-0 top-full z-40 mt-1 w-56 rounded-xl border border-border bg-card p-1.5 shadow-2xl">
            <a href="/app/profile" className="flex items-center gap-2 rounded-lg px-2.5 py-2 text-sm transition hover:bg-accent">
              <UserCircle className="h-4 w-4 text-muted-foreground" /> Profilim
            </a>
            <div className="my-1 border-t border-border/60" />
            <div className="px-2.5 py-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">Dil / Language</div>
            <div className="flex gap-1 px-2 pb-1.5">
              {(["tr", "en"] as const).map((l) => (
                <button key={l} onClick={() => setLang(l)}
                  className={cn("flex-1 rounded-md px-2 py-1 text-xs font-semibold transition",
                    lang === l ? "bg-primary/15 text-primary ring-1 ring-inset ring-primary/30" : "text-muted-foreground hover:bg-accent")}>
                  {l === "tr" ? "🇹🇷 Türkçe" : "🇬🇧 English"}
                </button>
              ))}
            </div>
            {me?.authEnabled && (
              <>
                <div className="my-1 border-t border-border/60" />
                <button onClick={() => logout()} className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-sm text-rose-400 transition hover:bg-rose-500/10">
                  <LogOut className="h-4 w-4" /> Çıkış
                </button>
              </>
            )}
          </div>
        )}
      </div>
    </header>
  );
}
