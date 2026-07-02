import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend } from "recharts";
import type { NameValue } from "@/lib/stats";

const PALETTE = [
  "hsl(358 85% 55%)", "hsl(217 91% 60%)", "hsl(158 64% 44%)", "hsl(38 92% 55%)",
  "hsl(271 76% 63%)", "hsl(190 90% 50%)", "hsl(24 90% 55%)", "hsl(140 60% 50%)",
];

export function DonutChart({ data, height = 240 }: { data: NameValue[]; height?: number | string }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <PieChart>
        <Pie data={data} dataKey="value" nameKey="name" cx="50%" cy="50%" innerRadius={55} outerRadius={85} paddingAngle={2} stroke="none">
          {data.map((_, i) => <Cell key={i} fill={PALETTE[i % PALETTE.length]} />)}
        </Pie>
        <Tooltip
          cursor={false}
          contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
          itemStyle={{ color: "hsl(var(--foreground))" }}
        />
        <Legend
          verticalAlign="bottom"
          iconType="circle"
          formatter={(v) => <span style={{ color: "hsl(var(--muted-foreground))", fontSize: 12 }}>{v}</span>}
        />
      </PieChart>
    </ResponsiveContainer>
  );
}
