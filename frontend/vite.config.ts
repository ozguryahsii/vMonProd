import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

// Production: .NET uygulaması statik dosyaları /app/ altından sunar → base '/app/'.
// Build çıktısı doğrudan ../wwwroot/app'e yazılır (CI publish onu paketler; yerelde .NET de sunabilir).
// Dev: 'npm run dev' → http://localhost:5173/app/  ; /api istekleri .NET'e (8080) proxy'lenir.
export default defineConfig({
  base: "/app/",
  plugins: [react()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  build: {
    outDir: path.resolve(__dirname, "../wwwroot/app"),
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      "/api": { target: "http://localhost:8080", changeOrigin: true },
    },
  },
});
