import { useEffect, useMemo, useState } from "react";
import { Search } from "lucide-react";
import { Drawer } from "@/components/ui/drawer";
import { Button } from "@/components/ui/button";
import { Input, Select, Field } from "@/components/ui/input";
import {
  type Board, type BoardInput, type BoardsMeta,
  getBoardsMeta, createBoard, updateBoard,
} from "@/lib/monitor";
import { cn } from "@/lib/utils";

export function BoardForm({ open, board, onClose, onSaved }: {
  open: boolean;
  board: Board | null;                 // null = yeni pano
  onClose: () => void;
  onSaved: () => void;
}) {
  const [meta, setMeta] = useState<BoardsMeta | null>(null);
  const [name, setName] = useState("");
  const [typeFilter, setTypeFilter] = useState("");
  const [keywordFilter, setKeywordFilter] = useState("");
  const [sortOrder, setSortOrder] = useState(0);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [q, setQ] = useState("");
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    getBoardsMeta().then(setMeta).catch((e) => setErr((e as Error).message));
    setName(board?.name ?? "");
    setTypeFilter(board?.typeFilter ?? "");
    setKeywordFilter(board?.keywordFilter ?? "");
    setSortOrder(board?.sortOrder ?? 0);
    setQ(""); setErr(null);
  }, [open, board]);

  // Elle seçilenler: board.serviceIds filtre sonucunu da içerir; formda yalnız CSV'dekiler işaretli olmalı.
  // Meta gelince, mevcut panoda elle seçili olanları filtreyle GELMEYENLERDEN ayıramayız → pratik çözüm:
  // düzenlemede tüm çözülmüş liste işaretli gelir, kullanıcı istediğini kaldırır (eski davranışla uyumlu).
  useEffect(() => {
    if (open) setSelected(new Set(board?.serviceIds ?? []));
  }, [open, board]);

  const filtered = useMemo(() => {
    const list = meta?.services ?? [];
    if (!q) return list;
    const t = q.toLowerCase();
    return list.filter((s) => s.name.toLowerCase().includes(t) || s.type.toLowerCase().includes(t));
  }, [meta, q]);

  const toggle = (id: number) =>
    setSelected((prev) => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n; });

  async function submit() {
    if (!name.trim()) { setErr("Pano adı zorunlu."); return; }
    setSaving(true); setErr(null);
    const input: BoardInput = {
      name: name.trim(),
      serviceIds: Array.from(selected),
      typeFilter: typeFilter || null,
      keywordFilter: keywordFilter || null,
      sortOrder,
    };
    try {
      if (board) await updateBoard(board.id, input);
      else await createBoard(input);
      onSaved(); onClose();
    } catch (e) { setErr((e as Error).message); }
    finally { setSaving(false); }
  }

  return (
    <Drawer open={open} onClose={onClose}
      title={board ? "Panoyu Düzenle" : "Yeni Dashboard"}
      description="Servisleri elle seç ve/veya tip-etiket filtresiyle otomatik dahil et"
      footer={<>
        <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Vazgeç</Button>
        <Button size="sm" onClick={submit} disabled={saving}>{saving ? "Kaydediliyor…" : "Kaydet"}</Button>
      </>}>
      {err && <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{err}</div>}

      <div className="space-y-5">
        <div className="grid grid-cols-3 gap-4">
          <div className="col-span-2"><Field label="Pano Adı"><Input value={name} onChange={(e) => setName(e.target.value)} placeholder="örn. Weblogic" /></Field></div>
          <Field label="Sıra"><Input type="number" value={sortOrder} onChange={(e) => setSortOrder(Number(e.target.value) || 0)} /></Field>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Field label="Tip filtresi" hint="bu tipteki TÜM servisler otomatik dahil">
            <Select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value)}>
              <option value="">— Yok —</option>
              {(meta?.types ?? []).map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
          </Field>
          <Field label="Etiket filtresi" hint="bu etiketli TÜM servisler otomatik dahil">
            <Select value={keywordFilter} onChange={(e) => setKeywordFilter(e.target.value)}>
              <option value="">— Yok —</option>
              {(meta?.keywords ?? []).map((k) => <option key={k} value={k}>{k}</option>)}
            </Select>
          </Field>
        </div>

        <div>
          <div className="mb-2 flex items-center justify-between">
            <span className="text-sm font-medium text-muted-foreground">Servisler ({selected.size} seçili)</span>
            <div className="flex gap-2 text-xs">
              <button className="text-primary hover:underline" onClick={() => setSelected(new Set((meta?.services ?? []).map((s) => s.id)))}>Tümünü seç</button>
              <button className="text-muted-foreground hover:underline" onClick={() => setSelected(new Set())}>Temizle</button>
            </div>
          </div>
          <div className="relative mb-2">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Servis ara…" className="pl-9" />
          </div>
          <div className="max-h-72 space-y-0.5 overflow-y-auto rounded-lg border border-border/60 p-2">
            {filtered.map((s) => (
              <label key={s.id} className={cn("flex cursor-pointer items-center gap-2.5 rounded-md px-2 py-1.5 text-sm transition-colors hover:bg-accent/60", selected.has(s.id) && "bg-primary/10")}>
                <input type="checkbox" checked={selected.has(s.id)} onChange={() => toggle(s.id)}
                  className="h-4 w-4 rounded border-border accent-[hsl(var(--primary))]" />
                <span className="flex-1 truncate">{s.name}</span>
                <span className="shrink-0 text-xs text-muted-foreground">{s.type}</span>
              </label>
            ))}
            {filtered.length === 0 && <div className="py-6 text-center text-sm text-muted-foreground">Eşleşen servis yok</div>}
          </div>
        </div>
      </div>
    </Drawer>
  );
}
