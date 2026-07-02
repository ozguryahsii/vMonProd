import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import { AnimatePresence } from "framer-motion";
import { AppShell } from "@/components/layout/AppShell";
import { Dashboard } from "@/pages/Dashboard";
import { Placeholder } from "@/pages/Placeholder";

const titles: Record<string, { t: string; s: string }> = {
  dashboard: { t: "Dashboard", s: "Genel bakış ve anlık durum" },
  reports: { t: "Raporlar", s: "Erişilebilirlik ve performans raporları" },
  statistics: { t: "İstatistikler", s: "Kapasite ve dağılım analizi" },
  services: { t: "Servisler", s: "İzlenen servisleri yönet" },
  credentials: { t: "Kimlik Bilgileri", s: "Kayıtlı kimlik bilgileri" },
  settings: { t: "Ayarlar", s: "Uygulama yapılandırması" },
  users: { t: "Kullanıcılar", s: "Kullanıcı ve yetki yönetimi" },
  audit: { t: "Denetim", s: "Değiştirilemez denetim kaydı" },
  about: { t: "Hakkında", s: "Sürüm ve uyumluluk bilgisi" },
};

export default function App() {
  const loc = useLocation();
  const key = loc.pathname.split("/")[2] || "dashboard";
  const meta = titles[key] ?? titles.dashboard;

  return (
    <AppShell title={meta.t} subtitle={meta.s}>
      <AnimatePresence mode="wait">
        <Routes location={loc} key={loc.pathname}>
          <Route path="/" element={<Navigate to="/app/dashboard" replace />} />
          <Route path="/app" element={<Navigate to="/app/dashboard" replace />} />
          <Route path="/app/dashboard" element={<Dashboard />} />
          {["reports", "statistics", "services", "credentials", "settings", "users", "audit", "about"].map((p) => (
            <Route key={p} path={`/app/${p}`} element={<Placeholder name={titles[p].t} />} />
          ))}
          <Route path="*" element={<Placeholder name="Bulunamadı" />} />
        </Routes>
      </AnimatePresence>
    </AppShell>
  );
}
