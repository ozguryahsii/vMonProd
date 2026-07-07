import { apiGet } from "./api";

export interface MeLicense {
  edition: "Basic" | "Standard" | "Enterprise";
  company: string;
  expires: string;    // yyyy-MM-dd
  daysLeft: number;
  maxMonitors: number | null;      // null = sınırsız
  maxUsers: number | null;
  maxDashboards: number | null;
  emailOnly: boolean;
  siem: boolean;
  selfHeal: boolean;
}

export interface Me {
  sam: string | null;
  displayName: string | null;
  isAdmin: boolean;
  authEnabled: boolean;
  mutabakatEnabled: boolean;
  perms: string[];
  theme: "dark" | "light";
  lang: "tr" | "en";
  companyName: string;
  license: MeLicense | null;
}

export const getMe = (signal?: AbortSignal) => apiGet<Me>("/me", signal);

/** Tema/dil tercihini kalıcılaştırır (çerez + DB — klasik SetPreference). */
export function setPreference(pref: { theme?: string; lang?: string }) {
  const p: Record<string, string> = {};
  if (pref.theme) p.theme = pref.theme;
  if (pref.lang) p.lang = pref.lang;
  // /Account/SetPreference /api dışında → apiForm /api öneki ekler; bu yüzden özel yol kullan
  return rawForm("/Account/SetPreference", p);
}

/** Oturumu kapat (CSRF'li) ve giriş sayfasına dön. */
export async function logout() {
  await rawForm("/Account/Logout", {});
  window.location.href = "/Account/Login";
}

async function rawForm(path: string, params: Record<string, string>) {
  const r = await fetch("/api/antiforgery", { credentials: "same-origin" });
  const token = (await r.json()).token as string;
  return fetch(path, {
    method: "POST",
    credentials: "same-origin",
    headers: { "X-CSRF-TOKEN": token, "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams(params),
  });
}
