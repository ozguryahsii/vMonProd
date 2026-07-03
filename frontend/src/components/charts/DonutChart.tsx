import { useState } from "react";
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Legend, Sector } from "recharts";
import type { NameValue } from "@/lib/stats";

const PALETTE = [
  "hsl(358 85% 55%)", "hsl(217 91% 60%)", "hsl(158 64% 44%)", "hsl(38 92% 55%)",
  "hsl(271 76% 63%)", "hsl(190 90% 50%)", "hsl(24 90% 55%)", "hsl(140 60% 50%)",
];

const trunc = (s: string, n = 22) => (s.length > n ? s.slice(0, n - 1) + "…" : s);

/* Hover'da dilim öne çıkar (eski ECharts emphasis davranışı) */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function ActiveSlice(props: any) {
  const { cx, cy, innerRadius, outerRadius, startAngle, endAngle, fill } = props;
  return (
    <g>
      <Sector cx={cx} cy={cy} innerRadius={innerRadius - 2} outerRadius={outerRadius + 7}
        startAngle={startAngle} endAngle={endAngle} fill={fill} style={{ filter: "drop-shadow(0 6px 14px rgba(0,0,0,.45))" }} />
    </g>
  );
}

export function DonutChart({ data, height = 240, onSliceClick, centerLabel }: {
  data: NameValue[];
  height?: number | string;
  onSliceClick?: (name: string) => void;
  centerLabel?: string | number;
}) {
  const [active, setActive] = useState<number | undefined>(undefined);
  const total = centerLabel ?? data.reduce((a, b) => a + b.value, 0);

  return (
    <ResponsiveContainer width="100%" height={height}>
      <PieChart>
        <Pie
          data={data} dataKey="value" nameKey="name" cx="50%" cy="46%"
          innerRadius="46%" outerRadius="72%" paddingAngle={2} stroke="none"
          activeIndex={active} activeShape={ActiveSlice}
          onMouseEnter={(_, i) => setActive(i)}
          onMouseLeave={() => setActive(undefined)}
          onClick={(entry) => onSliceClick?.((entry as unknown as NameValue).name)}
          style={onSliceClick ? { cursor: "pointer" } : undefined}
          animationDuration={700}
        >
          {data.map((_, i) => <Cell key={i} fill={PALETTE[i % PALETTE.length]} />)}
        </Pie>
        {/* Ortadaki toplam (eski tasarımdaki graphic text) */}
        <text x="50%" y="44%" textAnchor="middle" dominantBaseline="middle"
          style={{ fontSize: 22, fontWeight: 700, fill: "hsl(var(--foreground))" }}>
          {total}
        </text>
        <Tooltip
          cursor={false}
          contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
          itemStyle={{ color: "hsl(var(--foreground))" }}
        />
        <Legend
          verticalAlign="bottom"
          iconType="circle"
          formatter={(v: string) => (
            <span title={v} style={{ color: "hsl(var(--muted-foreground))", fontSize: 11 }}>{trunc(v)}</span>
          )}
        />
      </PieChart>
    </ResponsiveContainer>
  );
}
