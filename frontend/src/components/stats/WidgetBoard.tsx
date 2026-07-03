import { useCallback, useRef, useState, type PointerEvent as RPointerEvent } from "react";
import { GripVertical, X } from "lucide-react";
import type { StatsData, StatWidgetDef } from "@/lib/stats";
import { WidgetRenderer, widgetLabel } from "./widgets";
import type { Drill } from "./StatDetailDrawer";
import { cn } from "@/lib/utils";

const COLS = 12;
const ROW_H = 76;   // px — 1 grid satırı
const GAP = 12;     // px — widget'lar arası boşluk (iç padding olarak uygulanır)

interface DragState {
  id: number;
  mode: "move" | "resize";
  startX: number; startY: number;         // pointer px
  origX: number; origY: number; origW: number; origH: number;  // grid birimleri
}

/** Gridstack benzeri hafif pano: 12 kolon, sürükle-taşı + köşeden boyutlandır (yalnız düzenleme modunda). */
export function WidgetBoard({ widgets, data, editing, onChange, onRemove, onDrill }: {
  widgets: StatWidgetDef[];
  data: StatsData;
  editing: boolean;
  onChange: (w: StatWidgetDef[]) => void;
  onRemove: (id: number) => void;
  onDrill: (d: Drill) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [drag, setDrag] = useState<DragState | null>(null);

  const rows = Math.max(4, ...widgets.map((w) => w.y + w.h));

  const begin = useCallback((e: RPointerEvent, w: StatWidgetDef, mode: "move" | "resize") => {
    if (!editing) return;
    e.preventDefault();
    (e.target as HTMLElement).setPointerCapture?.(e.pointerId);
    setDrag({ id: w.id, mode, startX: e.clientX, startY: e.clientY, origX: w.x, origY: w.y, origW: w.w, origH: w.h });
  }, [editing]);

  const onMove = useCallback((e: RPointerEvent) => {
    if (!drag || !ref.current) return;
    const colW = ref.current.getBoundingClientRect().width / COLS;
    const dx = Math.round((e.clientX - drag.startX) / colW);
    const dy = Math.round((e.clientY - drag.startY) / ROW_H);
    onChange(widgets.map((w) => {
      if (w.id !== drag.id) return w;
      if (drag.mode === "move") {
        const x = Math.max(0, Math.min(COLS - w.w, drag.origX + dx));
        const y = Math.max(0, drag.origY + dy);
        return { ...w, x, y };
      }
      const nw = Math.max(2, Math.min(COLS - w.x, drag.origW + dx));
      const nh = Math.max(2, Math.min(12, drag.origH + dy));
      return { ...w, w: nw, h: nh };
    }));
  }, [drag, widgets, onChange]);

  const end = useCallback(() => setDrag(null), []);

  return (
    <div
      ref={ref}
      className={cn("relative w-full", editing && "rounded-xl bg-primary/5 ring-1 ring-primary/20")}
      style={{ height: rows * ROW_H + GAP }}
      onPointerMove={drag ? onMove : undefined}
      onPointerUp={drag ? end : undefined}
      onPointerLeave={drag ? end : undefined}
    >
      {widgets.map((w) => {
        const dragging = drag?.id === w.id;
        return (
          <div
            key={w.id}
            className="absolute transition-[left,top,width,height] duration-100"
            style={{
              left: `${(w.x / COLS) * 100}%`,
              top: w.y * ROW_H,
              width: `${(w.w / COLS) * 100}%`,
              height: w.h * ROW_H,
              padding: GAP / 2,
              zIndex: dragging ? 20 : 1,
              transitionDuration: dragging ? "0ms" : undefined,
            }}
          >
            <div className={cn(
              "card-glow flex h-full flex-col overflow-hidden rounded-lg border bg-gradient-to-b from-card to-card/60 shadow-[0_10px_30px_-14px_rgba(0,0,0,0.5)]",
              dragging ? "border-primary/60 shadow-[0_20px_50px_-15px_hsl(var(--primary)/0.4)]" : "border-border",
              !editing && "transition-all duration-200 hover:-translate-y-1 hover:border-primary/30 hover:shadow-[0_24px_55px_-18px_rgba(0,0,0,0.75)]"
            )}>
              <div
                className={cn("flex items-center gap-1.5 border-b border-border/60 px-3 py-1.5", editing && "cursor-grab select-none active:cursor-grabbing")}
                onPointerDown={(e) => begin(e, w, "move")}
              >
                {editing && <GripVertical className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />}
                <span className="truncate text-xs font-semibold">{widgetLabel(w)}</span>
                {editing && (
                  <button
                    className="ml-auto rounded p-0.5 text-muted-foreground transition hover:bg-destructive/15 hover:text-destructive"
                    onPointerDown={(e) => e.stopPropagation()}
                    onClick={() => onRemove(w.id)}
                    title="Widget'ı kaldır"
                  >
                    <X className="h-3.5 w-3.5" />
                  </button>
                )}
              </div>
              <div className="min-h-0 flex-1">
                <WidgetRenderer w={w} data={data} onDrill={onDrill} />
              </div>
              {editing && (
                <div
                  className="absolute bottom-1.5 right-1.5 h-4 w-4 cursor-nwse-resize rounded-sm border-b-2 border-r-2 border-primary/60"
                  onPointerDown={(e) => begin(e, w, "resize")}
                  title="Boyutlandır"
                />
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
