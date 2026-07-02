import { motion } from "framer-motion";
import { ArrowDownRight, ArrowUpRight, type LucideIcon } from "lucide-react";
import { Card } from "@/components/ui/card";
import { cn } from "@/lib/utils";

export interface KpiCardProps {
  label: string;
  value: string;
  delta?: number;          // yüzde değişim (+/-)
  icon: LucideIcon;
  accent?: "primary" | "success" | "warning" | "muted";
  index?: number;
}

const accentMap: Record<string, string> = {
  primary: "from-primary/20 to-primary/5 text-primary",
  success: "from-emerald-500/20 to-emerald-500/5 text-emerald-400",
  warning: "from-amber-500/20 to-amber-500/5 text-amber-400",
  muted: "from-slate-500/20 to-slate-500/5 text-slate-300",
};

export function KpiCard({ label, value, delta, icon: Icon, accent = "primary", index = 0 }: KpiCardProps) {
  const up = (delta ?? 0) >= 0;
  return (
    <motion.div
      initial={{ opacity: 0, y: 14, scale: 0.98 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.45, delay: index * 0.06, ease: [0.16, 1, 0.3, 1] }}
    >
      <Card className="p-5">
        <div className="flex items-start justify-between">
          <div>
            <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">{label}</p>
            <p className="mt-2 text-3xl font-bold tabular-nums tracking-tight">{value}</p>
          </div>
          <div className={cn("grid h-11 w-11 place-items-center rounded-xl bg-gradient-to-br", accentMap[accent])}>
            <Icon className="h-5 w-5" />
          </div>
        </div>
        {delta !== undefined && (
          <div className="mt-3 flex items-center gap-1.5 text-xs">
            <span
              className={cn(
                "inline-flex items-center gap-0.5 rounded-md px-1.5 py-0.5 font-semibold",
                up ? "bg-emerald-500/15 text-emerald-400" : "bg-rose-500/15 text-rose-400"
              )}
            >
              {up ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
              {Math.abs(delta)}%
            </span>
            <span className="text-muted-foreground">son 24 saat</span>
          </div>
        )}
      </Card>
    </motion.div>
  );
}
