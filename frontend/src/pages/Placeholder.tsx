import { Construction } from "lucide-react";
import { Card } from "@/components/ui/card";

export function Placeholder({ name }: { name: string }) {
  return (
    <Card className="flex flex-col items-center justify-center gap-3 py-24 text-center">
      <div className="grid h-14 w-14 place-items-center rounded-2xl bg-primary/10 text-primary">
        <Construction className="h-7 w-7" />
      </div>
      <h2 className="text-lg font-semibold">{name}</h2>
      <p className="max-w-sm text-sm text-muted-foreground">
        Bu ekran sonraki fazda taşınacak. Şu an tasarım kabuğu (Faz 2) hazır; içerik ekran ekran gelecek.
      </p>
    </Card>
  );
}
