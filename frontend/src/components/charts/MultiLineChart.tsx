import { Line, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend, Scatter, ComposedChart } from "recharts";

export interface SeriesDef {
  name: string;
  points: { t: string; v: number | null; mark?: "down" | "warn" }[];
}

const PALETTE = [
  "hsl(358 85% 55%)", "hsl(217 91% 60%)", "hsl(158 64% 44%)", "hsl(38 92% 55%)",
  "hsl(271 76% 63%)", "hsl(190 90% 50%)", "hsl(24 90% 55%)", "hsl(140 60% 50%)",
  "hsl(330 80% 60%)", "hsl(60 70% 45%)", "hsl(200 70% 55%)", "hsl(0 0% 60%)",
];

/** Çok-servisli çizgi grafik. Noktalar GERÇEK zamana göre sıralanır (parça parça görünme düzeltmesi).
 * mark'lı noktalar durum işareti olarak çizilir: down=BÜYÜK KIRMIZI, warn(yavaş/hata)=koyu sarı. */
export function MultiLineChart({ series, height = 260, unit = "", domainMax, longRange = false }: {
  series: SeriesDef[];
  height?: number;
  unit?: string;
  domainMax?: number;
  longRange?: boolean;   // 7g/1a: eksende tarih de göster
}) {
  const fmt = (ms: number) => {
    const d = new Date(ms);
    const hm = `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
    return longRange ? `${String(d.getDate()).padStart(2, "0")}.${String(d.getMonth() + 1).padStart(2, "0")} ${hm}` : hm;
  };

  // Zaman damgasıyla birleştir + SIRALA (etiket-sıralı birleştirme grafiği bölüyordu)
  const rows = new Map<number, Record<string, number | string | null>>();
  const marks: { ts: number; name: string; y: number; kind: "down" | "warn" }[] = [];
  for (const s of series)
    for (const p of s.points) {
      const ts = new Date(p.t).getTime();
      if (!rows.has(ts)) rows.set(ts, { ts });
      if (p.v != null) rows.get(ts)![s.name] = p.v;
      if (p.mark) marks.push({ ts, name: s.name, y: p.v ?? 0, kind: p.mark });
    }
  const data = Array.from(rows.values()).sort((a, b) => (a.ts as number) - (b.ts as number));

  // İşaret serileri: her satıra downY/warnY değerleri (Scatter noktaları)
  for (const m of marks) {
    const row = rows.get(m.ts)!;
    if (m.kind === "down") row.__down = m.y; else row.__warn = m.y;
  }

  return (
    <ResponsiveContainer width="100%" height={height}>
      <ComposedChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="ts" type="number" scale="time" domain={["dataMin", "dataMax"]}
          tickFormatter={(v) => fmt(v as number)}
          tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={48} />
        <YAxis domain={[0, domainMax ?? "auto"]} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <Tooltip
          labelFormatter={(v) => fmt(v as number)}
          contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
          formatter={(v: number, n: string) => (n.startsWith("__") ? [null, null] : [`${v}${unit}`, n])}
        />
        {series.length <= 12 && (
          <Legend iconType="circle" formatter={(v) => (v.startsWith("__") ? null : <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 11 }}>{v}</span>)} />
        )}
        {series.map((s, i) => (
          <Line key={s.name} type="monotone" dataKey={s.name} stroke={PALETTE[i % PALETTE.length]}
            strokeWidth={1.8} dot={false} connectNulls animationDuration={900} />
        ))}
        {/* Durum işaretleri: down=büyük kırmızı, yavaş/hata=koyu sarı */}
        <Scatter dataKey="__down" fill="hsl(0 84% 55%)" shape={(p: { cx?: number; cy?: number }) => (
          <circle cx={p.cx} cy={p.cy} r={6} fill="hsl(0 84% 55%)" stroke="hsl(var(--card))" strokeWidth={1.5} />)} legendType="none" />
        <Scatter dataKey="__warn" fill="hsl(38 92% 45%)" shape={(p: { cx?: number; cy?: number }) => (
          <circle cx={p.cx} cy={p.cy} r={5} fill="hsl(38 92% 45%)" stroke="hsl(var(--card))" strokeWidth={1.5} />)} legendType="none" />
      </ComposedChart>
    </ResponsiveContainer>
  );
}
