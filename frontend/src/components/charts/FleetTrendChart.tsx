import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid, Legend } from "recharts";

export interface FleetPoint { day: string; cpu: number; ram: number; disk: number }

const series = [
  { key: "cpu", label: "CPU", color: "hsl(358 85% 55%)" },
  { key: "ram", label: "RAM", color: "hsl(217 91% 60%)" },
  { key: "disk", label: "Disk", color: "hsl(38 92% 55%)" },
] as const;

export function FleetTrendChart({ data }: { data: FleetPoint[] }) {
  return (
    <ResponsiveContainer width="100%" height={280}>
      <AreaChart data={data} margin={{ top: 10, right: 8, left: -18, bottom: 0 }}>
        <defs>
          {series.map((s) => (
            <linearGradient key={s.key} id={`g-${s.key}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={s.color} stopOpacity={0.35} />
              <stop offset="100%" stopColor={s.color} stopOpacity={0} />
            </linearGradient>
          ))}
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="day" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} minTickGap={24} />
        <YAxis domain={[0, 100]} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <Tooltip contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px" }} />
        <Legend iconType="circle" formatter={(v) => <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 12 }}>{v}</span>} />
        {series.map((s) => (
          <Area key={s.key} type="monotone" dataKey={s.key} name={s.label} stroke={s.color} strokeWidth={2} fill={`url(#g-${s.key})`} animationDuration={900} />
        ))}
      </AreaChart>
    </ResponsiveContainer>
  );
}
