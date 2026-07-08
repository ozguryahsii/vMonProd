import { useEffect, useState } from "react";
import { Drawer } from "@/components/ui/drawer";
import { Button } from "@/components/ui/button";
import { Input, Field } from "@/components/ui/input";
import { bulkEdit, type BulkEditInput } from "@/lib/services";
import { cn } from "@/lib/utils";

type Tri = "" | "on" | "off";

function Segmented({ value, onChange }: { value: Tri; onChange: (v: Tri) => void }) {
  const opts: { v: Tri; label: string; on: string }[] = [
    { v: "", label: "Dokunma", on: "bg-secondary text-secondary-foreground" },
    { v: "on", label: "Aç", on: "bg-emerald-600 text-white" },
    { v: "off", label: "Kapat", on: "bg-rose-600 text-white" },
  ];
  return (
    <div className="inline-flex overflow-hidden rounded-lg border border-border">
      {opts.map((o) => (
        <button key={o.v} type="button" onClick={() => onChange(o.v)}
          className={cn("px-2.5 py-1 text-xs font-semibold transition-colors",
            value === o.v ? o.on : "bg-card text-muted-foreground hover:bg-accent/60")}>
          {o.label}
        </button>
      ))}
    </div>
  );
}

function NumRow({ label, hint, set, setSet, val, setVal }: {
  label: string; hint: string;
  set: boolean; setSet: (b: boolean) => void;
  val: string; setVal: (s: string) => void;
}) {
  return (
    <div className="flex items-center gap-3">
      <label className="flex w-40 shrink-0 cursor-pointer items-center gap-2 text-sm">
        <input type="checkbox" checked={set} onChange={(e) => setSet(e.target.checked)}
          className="h-4 w-4 rounded border-border accent-[hsl(var(--primary))]" />
        {label}
      </label>
      <Input type="number" value={val} onChange={(e) => setVal(e.target.value)} disabled={!set}
        placeholder={hint} className="h-9" />
    </div>
  );
}

export function BulkEditForm({ open, ids, onClose, onDone }: {
  open: boolean;
  ids: number[];
  onClose: () => void;
  onDone: (msg: string) => void;
}) {
  const [mail, setMail] = useState<Tri>("");
  const [sms, setSms] = useState<Tri>("");
  const [wa, setWa] = useState<Tri>("");
  const [call, setCall] = useState<Tri>("");
  const [enabled, setEnabled] = useState<Tri>("");
  const [statusPage, setStatusPage] = useState<Tri>("");
  const [ivOn, setIvOn] = useState(false); const [iv, setIv] = useState("");
  const [slOn, setSlOn] = useState(false); const [sl, setSl] = useState("");
  const [cpOn, setCpOn] = useState(false); const [cp, setCp] = useState("");
  const [rmOn, setRmOn] = useState(false); const [rm, setRm] = useState("");
  const [dkOn, setDkOn] = useState(false); const [dk, setDk] = useState("");
  const [kw, setKw] = useState("");
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setMail(""); setSms(""); setWa(""); setCall(""); setEnabled(""); setStatusPage("");
      setIvOn(false); setIv(""); setSlOn(false); setSl(""); setCpOn(false); setCp("");
      setRmOn(false); setRm(""); setDkOn(false); setDk(""); setKw(""); setErr(null);
    }
  }, [open]);

  const num = (s: string): number | null => (s.trim() === "" ? null : Number(s));

  async function submit() {
    setSaving(true); setErr(null);
    const input: BulkEditInput = {
      ids,
      alertMail: mail || null, alertSms: sms || null, alertWhatsapp: wa || null,
      alertCall: call || null, enabled: enabled || null,
      showOnStatusPage: statusPage || null,
      setInterval: ivOn, interval: num(iv),
      setSlow: slOn, slow: num(sl),
      setCpu: cpOn, cpu: num(cp),
      setRam: rmOn, ram: num(rm),
      setDisk: dkOn, disk: num(dk),
      addKeywords: kw.trim() || null,
    };
    try {
      const r = await bulkEdit(input);
      onDone(`${r.updated} servis güncellendi (${r.changes.join(", ")}).`);
      onClose();
    } catch (e) { setErr((e as Error).message); }
    finally { setSaving(false); }
  }

  return (
    <Drawer open={open} onClose={onClose}
      title={`Toplu Düzenle (${ids.length} servis)`}
      description="Yalnız işaretlediğin alanlar değişir; diğerleri dokunulmaz"
      footer={<>
        <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Vazgeç</Button>
        <Button size="sm" onClick={submit} disabled={saving}>{saving ? "Uygulanıyor…" : "Uygula"}</Button>
      </>}>
      {err && <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">{err}</div>}

      <div className="space-y-6">
        <div>
          <p className="mb-2 text-sm font-medium text-muted-foreground">Alarm Kanalları & Aktiflik</p>
          <div className="space-y-2.5 rounded-lg border border-border/60 bg-muted/20 p-4">
            {([["Mail", mail, setMail], ["SMS", sms, setSms], ["WhatsApp", wa, setWa], ["Arama", call, setCall], ["Aktif", enabled, setEnabled], ["Durum sayfasında göster", statusPage, setStatusPage]] as const).map(([label, v, set]) => (
              <div key={label} className="flex items-center justify-between gap-3">
                <span className="text-sm">{label}</span>
                <Segmented value={v} onChange={set} />
              </div>
            ))}
          </div>
        </div>

        <div>
          <p className="mb-2 text-sm font-medium text-muted-foreground">Sayısal Alanlar <span className="font-normal">(işaretle → yaz; boş bırakırsan özellik kapatılır)</span></p>
          <div className="space-y-2.5 rounded-lg border border-border/60 bg-muted/20 p-4">
            <NumRow label="Aralık (dk)" hint="boş = global" set={ivOn} setSet={setIvOn} val={iv} setVal={setIv} />
            <NumRow label="Yavaşlık eşiği (ms)" hint="boş = kapalı" set={slOn} setSet={setSlOn} val={sl} setVal={setSl} />
            <NumRow label="CPU eşiği (%)" hint="boş = kapalı" set={cpOn} setSet={setCpOn} val={cp} setVal={setCp} />
            <NumRow label="RAM eşiği (%)" hint="boş = kapalı" set={rmOn} setSet={setRmOn} val={rm} setVal={setRm} />
            <NumRow label="Disk eşiği (%)" hint="boş = kapalı" set={dkOn} setSet={setDkOn} val={dk} setVal={setDk} />
          </div>
        </div>

        <Field label="Etiket ekle" hint="virgülle ayır — mevcut etiketlerin üzerine eklenir">
          <Input value={kw} onChange={(e) => setKw(e.target.value)} placeholder="örn. üretim, weblogic" />
        </Field>
      </div>
    </Drawer>
  );
}
