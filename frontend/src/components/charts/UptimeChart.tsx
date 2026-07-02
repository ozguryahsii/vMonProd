import {
  Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid,
} from "recharts";

const data = Array.from({ length: 24 }, (_, i) => ({
  saat: `${String(i).padStart(2, "0")}:00`,
  uptime: 97 + Math.sin(i / 2.5) * 1.6 + (i === 14 ? -4.5 : 0) + Math.random() * 0.6,
  yanit: 120 + Math.cos(i / 3) * 40 + (i === 14 ? 180 : 0) + Math.random() * 30,
}));

export function UptimeChart() {
  return (
    <ResponsiveContainer width="100%" height={280}>
      <AreaChart data={data} margin={{ top: 10, right: 8, left: -18, bottom: 0 }}>
        <defs>
          <linearGradient id="gUptime" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="hsl(158 64% 44%)" stopOpacity={0.5} />
            <stop offset="100%" stopColor="hsl(158 64% 44%)" stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="saat" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} interval={3} />
        <YAxis domain={[88, 100]} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
        <Tooltip
          contentStyle={{
            background: "hsl(var(--card))",
            border: "1px solid hsl(var(--border))",
            borderRadius: "0.75rem",
            fontSize: "12px",
            boxShadow: "0 20px 50px -20px rgba(0,0,0,0.6)",
          }}
          labelStyle={{ color: "hsl(var(--muted-foreground))" }}
          formatter={(v: number) => [`${v.toFixed(1)}%`, "Erişilebilirlik"]}
        />
        <Area type="monotone" dataKey="uptime" stroke="hsl(158 64% 44%)" strokeWidth={2} fill="url(#gUptime)" />
      </AreaChart>
    </ResponsiveContainer>
  );
}
