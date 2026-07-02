import { LineChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend } from "recharts";

export interface SeriesDef { name: string; points: { t: string; v: number | null }[] }

const PALETTE = [
  "hsl(358 85% 55%)", "hsl(217 91% 60%)", "hsl(158 64% 44%)", "hsl(38 92% 55%)",
  "hsl(271 76% 63%)", "hsl(190 90% 50%)", "hsl(24 90% 55%)", "hsl(140 60% 50%)",
  "hsl(330 80% 60%)", "hsl(60 70% 45%)", "hsl(200 70% 55%)", "hsl(0 0% 60%)",
];

const clock = (iso: string) => {
  const d = new Date(iso);
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
};

/** Çok-servisli çizgi grafik: seriler zaman etiketiyle birleştirilir (eski dashboard'daki canlı grafikler). */
export function MultiLineChart({ series, height = 260, unit = "", domainMax }: {
  series: SeriesDef[];
  height?: number;
  unit?: string;
  domainMax?: number;
}) {
  // Tüm zaman noktalarını birleştir → satır bazlı veri
  const rows = new Map<string, Record<string, number | string | null>>();
  for (const s of series)
    for (const p of s.points) {
      const key = clock(p.t);
      if (!rows.has(key)) rows.set(key, { t: key });
      rows.get(key)![s.name] = p.v;
    }
  const data = Array.from(rows.values());

  return (
    <ResponsiveContainer width="100%" height={height}>
      <LineChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="t" tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={40} />
        <YAxis domain={[0, domainMax ?? "auto"]} tick={{ fontSize: 10, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <Tooltip
          contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
          formatter={(v: number, n: string) => [`${v}${unit}`, n]}
        />
        {series.length <= 12 && (
          <Legend iconType="circle" formatter={(v) => <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 11 }}>{v}</span>} />
        )}
        {series.map((s, i) => (
          <Line key={s.name} type="monotone" dataKey={s.name} stroke={PALETTE[i % PALETTE.length]}
            strokeWidth={1.8} dot={false} connectNulls />
        ))}
      </LineChart>
    </ResponsiveContainer>
  );
}
