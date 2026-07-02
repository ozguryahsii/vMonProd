# vMon Frontend (React SPA — Yol A)

Premium enterprise dashboard — React + TypeScript + Vite + Tailwind + shadcn/ui + Framer Motion + Recharts + Lucide.

**Production'da Node ÇALIŞMAZ.** Bu proje derlenip statik dosyalara dönüşür (`../wwwroot/app`),
mevcut .NET uygulaması onları `/app/` altından sunar. Node yalnız *build/geliştirme* aracıdır.

## Kurulum (bir kez)
1. [Node.js LTS](https://nodejs.org) kur (18+).
2. Bu klasörde:
   ```bash
   npm install
   ```

## Geliştirme (canlı önizleme)
```bash
npm run dev
```
→ http://localhost:5173/app/dashboard  (kaydettikçe anında yenilenir)

> `/api/*` istekleri otomatik olarak http://localhost:8080 (çalışan .NET) adresine proxy'lenir.

## Production build (yerelde denemek için)
```bash
npm run build
```
→ çıktı: `../wwwroot/app/`  — .NET publish bunu paketler. CI'da bu adım otomatik çalışır.

## Yapı
```
src/
  components/
    ui/          shadcn primitifleri (button, card…)   → npx shadcn@latest add <bileşen>
    layout/      AppShell, Sidebar, Topbar
    dashboard/   KpiCard vb.
    charts/      Recharts grafik bileşenleri
  pages/         ekranlar (Dashboard, …)
  lib/utils.ts   cn() yardımcısı
  index.css      tasarım token'ları (marka kırmızısı, koyu-tema öncelikli)
```

## shadcn bileşeni ekleme
```bash
npx shadcn@latest add dialog table dropdown-menu badge input ...
```
`components.json` hazır; bileşenler `src/components/ui/` altına iner.
