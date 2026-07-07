import type { ReactNode } from "react";
import { AlertTriangle, Inbox, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { cn } from "@/lib/utils";

/** Yükleme iskeleti bloğu */
export function Skeleton({ className }: { className?: string }) {
  return <div className={cn("skeleton", className)} />;
}

/** Lisans kilidi notu — paket-kısıtlı özelliklerde standart sarı bilgi kutusu.
 *  Amaç: kullanıcı denemeye uğraşmadan neyin hangi pakette olduğunu görsün. */
export function LicenseLockNote({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn("rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-400", className)}>
      {children}
    </div>
  );
}

/** Hata durumu — yeniden dene ile */
export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <Card className="flex flex-col items-center justify-center gap-3 py-16 text-center">
      <div className="grid h-12 w-12 place-items-center rounded-2xl bg-destructive/10 text-destructive">
        <AlertTriangle className="h-6 w-6" />
      </div>
      <p className="max-w-sm text-sm text-muted-foreground">{message}</p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          <RefreshCw className="h-4 w-4" /> Yeniden dene
        </Button>
      )}
    </Card>
  );
}

/** Boş durum */
export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-2 py-16 text-center text-muted-foreground">
      <Inbox className="h-8 w-8 opacity-50" />
      <p className="font-medium">{title}</p>
      {hint && <p className="max-w-sm text-sm opacity-80">{hint}</p>}
    </div>
  );
}
