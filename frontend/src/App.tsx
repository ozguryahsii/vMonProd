import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import { AnimatePresence } from "framer-motion";
import { AppShell } from "@/components/layout/AppShell";
import { Dashboard } from "@/pages/Dashboard";
import { Services } from "@/pages/Services";
import { Reports } from "@/pages/Reports";
import { Statistics } from "@/pages/Statistics";
import { Audit } from "@/pages/Audit";
import { Users } from "@/pages/Users";
import { Credentials } from "@/pages/Credentials";
import { Settings } from "@/pages/Settings";
import { Profile } from "@/pages/Profile";
import { About } from "@/pages/About";
import { Placeholder } from "@/pages/Placeholder";

const titles: Record<string, { t: string; s: string }> = {
  dashboard: { t: "Dashboard'lar", s: "Özel izleme ekranların — anlık durum" },
  reports: { t: "Raporlar", s: "Erişilebilirlik ve performans raporları" },
  statistics: { t: "İstatistikler", s: "Kapasite ve dağılım analizi" },
  services: { t: "İzlemeler", s: "İzlenen hedefleri yönet" },
  credentials: { t: "Kimlik Bilgileri", s: "Kayıtlı kimlik bilgileri" },
  settings: { t: "Ayarlar", s: "Uygulama yapılandırması" },
  users: { t: "Kullanıcılar", s: "Kullanıcı ve yetki yönetimi" },
  audit: { t: "Denetim", s: "Değiştirilemez denetim kaydı" },
  mutabakat: { t: "Mutabakat", s: "Envanter karşılaştırma" },
  profile: { t: "Profilim", s: "İletişim bilgileri ve şifre" },
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
          <Route path="/app/services" element={<Services />} />
          <Route path="/app/reports" element={<Reports />} />
          <Route path="/app/statistics" element={<Statistics />} />
          <Route path="/app/audit" element={<Audit />} />
          <Route path="/app/users" element={<Users />} />
          <Route path="/app/credentials" element={<Credentials />} />
          <Route path="/app/settings" element={<Settings />} />
          <Route path="/app/profile" element={<Profile />} />
          <Route path="/app/about" element={<About />} />
          {["mutabakat"].map((p) => (
            <Route key={p} path={`/app/${p}`} element={<Placeholder name={titles[p].t} />} />
          ))}
          <Route path="*" element={<Placeholder name="Bulunamadı" />} />
        </Routes>
      </AnimatePresence>
    </AppShell>
  );
}
