import { useState } from "react";
import { Line, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend, LineChart } from "recharts";

export interface SeriesDef {
  name: string;
  points: { t: string; v: number | null; mark?: "down" | "warn" }[];
}

const PALETTE = [
  "hsl(358 85% 55%)", "hsl(217 91% 60%)", "hsl(158 64% 44%)", "hsl(38 92% 55%)",
  "hsl(271 76% 63%)", "hsl(190 90% 50%)", "hsl(24 90% 55%)", "hsl(140 60% 50%)",
  "hsl(330 80% 60%)", "hsl(60 70% 45%)", "hsl(200 70% 55%)", "hsl(0 0% 60%)",
];

/** Çok-servisli çizgi grafik — eski tasarım davranışı:
 *  - noktalar GERÇEK zamana göre sıralanır (kopukluk yok)
 *  - durum işaretleri ÇİZGİNİN ÜZERİNDE kendi değerinde çizilir (down=büyük kırmızı,
 *    yavaş/hata=koyu sarı) — ayrı şerit/katman yok, lejant temiz
 *  - animasyon ve hover tooltip (servis adı + değer) korunur
 *  - LEJANT TIKLANABİLİR: isme basınca seri gizlenir (üstü çizilir), tekrar basınca döner.
 *    hiddenNames/onToggleName verilirse durum DIŞARIDAN yönetilir (birden çok grafik
 *    aynı seçimi paylaşır — dashboard'da yanıt süresi + CPU/RAM/Disk birlikte). */
export function MultiLineChart({ series, height = 260, unit = "", domainMax, longRange = false, hiddenNames, onToggleName }: {
  series: SeriesDef[];
  height?: number;
  unit?: string;
  domainMax?: number;
  longRange?: boolean;   // 7g/1a: eksende tarih de göster
  hiddenNames?: Set<string>;            // opsiyonel: paylaşılan gizli-seri kümesi
  onToggleName?: (name: string) => void; // opsiyonel: paylaşılan toggle
}) {
  // Dışarıdan yönetilmiyorsa grafik kendi gizli kümesini tutar (her lejantlı grafikte çalışsın)
  const [localHidden, setLocalHidden] = useState<Set<string>>(new Set());
  const hidden = hiddenNames ?? localHidden;
  const toggle = (name: string) => {
    if (onToggleName) { onToggleName(name); return; }
    setLocalHidden((prev) => {
      const n = new Set(prev);
      if (n.has(name)) n.delete(name); else n.add(name);
      return n;
    });
  };

  const fmt = (ms: number) => {
    const d = new Date(ms);
    const hm = `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
    return longRange ? `${String(d.getDate()).padStart(2, "0")}.${String(d.getMonth() + 1).padStart(2, "0")} ${hm}` : hm;
  };

  // Zaman damgasıyla birleştir + sırala; işaret bilgisi seri-bazlı sütunda taşınır (name__m)
  const rows = new Map<number, Record<string, number | string | null | undefined>>();
  for (const s of series)
    for (const p of s.points) {
      const ts = new Date(p.t).getTime();
      if (!rows.has(ts)) rows.set(ts, { ts });
      const row = rows.get(ts)!;
      if (p.v != null) row[s.name] = p.v;
      if (p.mark) row[`${s.name}__m`] = p.mark;
    }
  const data = Array.from(rows.values()).sort((a, b) => (a.ts as number) - (b.ts as number));

  // Durum noktası: yalnız işaretli noktalarda, çizginin üzerinde (eski görünüm)
  const makeDot = (name: string) =>
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (props: any) => {
      const { cx, cy, payload, index } = props;
      const m = payload?.[`${name}__m`];
      if (m === "down")
        return <circle key={`${name}-${index}`} cx={cx} cy={cy} r={6} fill="hsl(0 84% 55%)" stroke="hsl(var(--card))" strokeWidth={1.5} />;
      if (m === "warn")
        return <circle key={`${name}-${index}`} cx={cx} cy={cy} r={5} fill="hsl(38 92% 45%)" stroke="hsl(var(--card))" strokeWidth={1.5} />;
      return <g key={`${name}-${index}`} />;
    };

  return (
    <ResponsiveContainer width="100%" height={height}>
      <LineChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="ts" type="number" scale="time" domain={["dataMin", "dataMax"]}
          tickFormatter={(v) => fmt(v as number)}
          tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={48} />
        <YAxis domain={[0, domainMax ?? "auto"]} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <Tooltip
          labelFormatter={(v) => fmt(v as number)}
          contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          formatter={(v: number, n: string, item: any) => {
            const m = item?.payload?.[`${n}__m`];
            const state = m === "down" ? " · DOWN" : m === "warn" ? " · YAVAŞ/HATA" : "";
            return [`${v}${unit}${state}`, n];
          }}
        />
        {series.length <= 12 && (
          <Legend iconType="circle"
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            onClick={(e: any) => { const n = String(e?.dataKey ?? e?.value ?? ""); if (n) toggle(n); }}
            formatter={(v) => (
              <span style={{
                color: "hsl(var(--muted-foreground))", fontSize: 11, cursor: "pointer", userSelect: "none",
                textDecoration: hidden.has(String(v)) ? "line-through" : "none",
                opacity: hidden.has(String(v)) ? 0.45 : 1,
              }}>{v}</span>
            )} />
        )}
        {series.map((s, i) => (
          // Gerçek çizgi animasyonu GERİ: backend artık seri başına ~300 kovalı özet döndüğü
          // için (100k ham satır yok) animasyon her aralıkta akıcı çalışır.
          // hide: lejantta kalır (üstü çizili) ama çizgi çizilmez; renk sırası değişmez.
          <Line key={s.name} type="monotone" dataKey={s.name} stroke={PALETTE[i % PALETTE.length]}
            strokeWidth={1.8} dot={makeDot(s.name)} connectNulls animationDuration={900}
            hide={hidden.has(s.name)} />
        ))}
      </LineChart>
    </ResponsiveContainer>
  );
}
