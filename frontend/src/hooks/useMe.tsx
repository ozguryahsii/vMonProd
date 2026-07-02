import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getMe, type Me } from "@/lib/me";

interface MeCtx {
  me: Me | null;
  hasPerm: (perm: string) => boolean;
  reloadMe: () => void;
}

const Ctx = createContext<MeCtx>({ me: null, hasPerm: () => false, reloadMe: () => {} });

export function MeProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);

  const load = () =>
    getMe()
      .then((m) => {
        setMe(m);
        // Tema/dil kullanıcı tercihinden uygula (DB > çerez > varsayılan koyu)
        document.documentElement.classList.toggle("dark", m.theme !== "light");
        document.documentElement.lang = m.lang;
      })
      .catch(() => {
        // /api/me 401 fırlatırsa api.ts login'e yönlendirir; diğer hatalarda varsayılanlarla devam
        setMe(null);
      });

  useEffect(() => { load(); }, []);

  const hasPerm = (perm: string) => !!me && (me.isAdmin || me.perms.includes(perm));

  return <Ctx.Provider value={{ me, hasPerm, reloadMe: load }}>{children}</Ctx.Provider>;
}

export const useMe = () => useContext(Ctx);
