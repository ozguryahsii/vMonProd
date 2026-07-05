import { useState } from "react";
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip, Sector } from "recharts";
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
    <Sector cx={cx} cy={cy} innerRadius={innerRadius - 2} outerRadius={outerRadius + 7}
      startAngle={startAngle} endAngle={endAngle} fill={fill} style={{ filter: "drop-shadow(0 6px 14px rgba(0,0,0,.45))" }} />
  );
}

/** Donut: lejant HTML olarak AYRI (en büyük 4 + "+N diğer") → pasta kendi alanında tam ortalanır,
 * toplam sayı overlay ile HER ZAMAN tam merkezde ve tıklanabilir. */
export function DonutChart({ data, height = 240, onSliceClick, onCenterClick, centerLabel }: {
  data: NameValue[];
  height?: number | string;
  onSliceClick?: (name: string) => void;
  onCenterClick?: () => void;
  centerLabel?: string | number;
}) {
  const [active, setActive] = useState<number | undefined>(undefined);
  const total = centerLabel ?? data.reduce((a, b) => a + b.value, 0);

  const top4 = data
    .map((d, i) => ({ ...d, color: PALETTE[i % PALETTE.length] }))
    .sort((a, b) => b.value - a.value)
    .slice(0, 4);
  const hidden = data.length - top4.length;

  return (
    <div className="flex h-full w-full flex-col" style={typeof height === "number" ? { height } : undefined}>
      {/* Pasta alanı — lejant burada DEĞİL, bu yüzden merkez = gerçek merkez */}
      <div className="relative min-h-0 flex-1">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data} dataKey="value" nameKey="name" cx="50%" cy="50%"
              innerRadius="52%" outerRadius="80%" paddingAngle={2} stroke="none"
              activeIndex={active} activeShape={ActiveSlice}
              onMouseEnter={(_, i) => setActive(i)}
              onMouseLeave={() => setActive(undefined)}
              onClick={(entry) => onSliceClick?.((entry as unknown as NameValue).name)}
              style={onSliceClick ? { cursor: "pointer" } : undefined}
              animationDuration={700}
            >
              {data.map((_, i) => <Cell key={i} fill={PALETTE[i % PALETTE.length]} />)}
            </Pie>
            <Tooltip
              cursor={false}
              contentStyle={{ background: "hsl(var(--card))", border: "1px solid hsl(var(--border))", borderRadius: "0.75rem", fontSize: "12px", color: "hsl(var(--foreground))" }}
              itemStyle={{ color: "hsl(var(--foreground))" }}
            />
          </PieChart>
        </ResponsiveContainer>
        {/* Toplam — mutlak konumla TAM merkez, tıklanabilir */}
        <button
          type="button"
          onClick={onCenterClick}
          disabled={!onCenterClick}
          className="pointer-events-auto absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-full px-2 text-2xl font-bold tabular-nums text-foreground transition-transform enabled:cursor-pointer enabled:hover:scale-110"
          title={onCenterClick ? "Tümünü listele" : undefined}
        >
          {total}
        </button>
      </div>

      {/* HTML lejant: en büyük 4 + kalan sayısı (taşma yok; tam ad title'da) */}
      <div className="flex flex-wrap items-center justify-center gap-x-3 gap-y-0.5 pb-1 pt-1.5 text-[11px] text-muted-foreground">
        {top4.map((d) => (
          <button key={d.name} type="button" title={d.name}
            onClick={() => onSliceClick?.(d.name)}
            className="inline-flex items-center gap-1 transition-colors hover:text-foreground"
            style={onSliceClick ? undefined : { cursor: "default" }}>
            <span className="h-2 w-2 shrink-0 rounded-full" style={{ background: d.color }} />
            {trunc(d.name)}
          </button>
        ))}
        {hidden > 0 && (
          <button type="button" onClick={onCenterClick} className="inline-flex items-center gap-1 transition-colors hover:text-foreground">
            <span className="h-2 w-2 shrink-0 rounded-full bg-muted-foreground" /> +{hidden} diğer
          </button>
        )}
      </div>
    </div>
  );
}
