import {
  Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis, CartesianGrid,
} from "recharts";

export interface UptimePoint {
  t: string;        // ISO tarih (UTC)
  uptime: number;   // %
}

function hourLabel(iso: string) {
  const d = new Date(iso);
  return `${String(d.getHours()).padStart(2, "0")}:00`;
}

export function UptimeChart({ data, xFormat = hourLabel }: { data: UptimePoint[]; xFormat?: (iso: string) => string }) {
  const chart = data.map((p) => ({ saat: xFormat(p.t), uptime: p.uptime }));
  return (
    <ResponsiveContainer width="100%" height={280}>
      <AreaChart data={chart} margin={{ top: 10, right: 8, left: -18, bottom: 0 }}>
        <defs>
          <linearGradient id="gUptime" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="hsl(158 64% 44%)" stopOpacity={0.5} />
            <stop offset="100%" stopColor="hsl(158 64% 44%)" stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="saat" tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} interval="preserveStartEnd" minTickGap={28} />
        <YAxis domain={["dataMin - 2", 100]} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} tickLine={false} axisLine={false} />
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
        <Area type="monotone" dataKey="uptime" stroke="hsl(158 64% 44%)" strokeWidth={2} fill="url(#gUptime)" animationDuration={900} />
      </AreaChart>
    </ResponsiveContainer>
  );
}
