import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Drawer } from "@/components/ui/drawer";
import { Input, Select, Textarea, Field, Switch } from "@/components/ui/input";
import {
  type ServiceItem, type ServiceInput, type ServicesMeta,
  TYPE_META, TYPE_GROUP_ORDER, CONTROL_TYPES,
  createService, updateService,
} from "@/lib/services";

const empty: ServiceInput = {
  name: "", type: "Http", target: "", port: null, extra: null,
  useSsl: false, ignoreCertErrors: true, credentialId: null, enabled: true,
  intervalMinutesOverride: null, responseTimeThresholdMs: null, timeoutSeconds: 15,
  cpuThresholdPercent: null, ramThresholdPercent: null, diskThresholdPercent: null,
  keyword: null, description: null,
  alertMail: true, alertSms: false, alertWhatsapp: false, alertCall: false,
  selfHealEnabled: false, selfHealMaxRetries: 1, selfHealAfterFailures: 1,
};

function toInput(s: ServiceItem): ServiceInput {
  const { id, credentialName, lastCheckedAt, lastIsUp, lastStatus, lastResponseTimeMs, lastError, slow, ...rest } = s;
  void id; void credentialName; void lastCheckedAt; void lastIsUp; void lastStatus; void lastResponseTimeMs; void lastError; void slow;
  return rest;
}

const numOrNull = (v: string): number | null => (v.trim() === "" ? null : Number(v));

export function ServiceForm({
  open, service, meta, onClose, onSaved,
}: {
  open: boolean;
  service: ServiceItem | null;
  meta: ServicesMeta | null;
  onClose: () => void;
  onSaved: (msg: string) => void;
}) {
  const [form, setForm] = useState<ServiceInput>(empty);
  const [saving, setSaving] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (open) { setForm(service ? toInput(service) : empty); setErr(null); }
  }, [open, service]);

  const set = <K extends keyof ServiceInput>(k: K, v: ServiceInput[K]) => setForm((f) => ({ ...f, [k]: v }));

  // Tip kataloğu: kategorili liste + seçili tipin form davranışı (eski dinamik form birebir)
  const tm = TYPE_META[form.type] ?? TYPE_META.Tcp;
  const grouped = useMemo(() => {
    const known = new Set(Object.keys(TYPE_META));
    const backendTypes = new Set(meta?.types ?? Object.keys(TYPE_META));
    const groups = TYPE_GROUP_ORDER.map((g) => ({
      group: g,
      items: Object.entries(TYPE_META)
        .filter(([k, m]) => m.group === g && backendTypes.has(k))
        .map(([k, m]) => ({ value: k, label: m.label })),
    })).filter((g) => g.items.length > 0);
    // Backend'de olup katalogda olmayan tipler (ileriye dönük) sona eklenir
    const extra = (meta?.types ?? []).filter((t) => !known.has(t));
    if (extra.length > 0) groups.push({ group: "Diğer", items: extra.map((t) => ({ value: t, label: t })) });
    return groups;
  }, [meta]);

  async function submit() {
    setSaving(true); setErr(null);
    try {
      if (service) { await updateService(service.id, form); onSaved("İzleme güncellendi."); }
      else { await createService(form); onSaved("İzleme eklendi."); }
      onClose();
    } catch (e) {
      setErr((e as Error).message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <Drawer
      open={open}
      onClose={onClose}
      title={service ? "İzlemeyi Düzenle" : "Yeni İzleme"}
      description={service ? service.name : "İzlenecek yeni bir hedef tanımla"}
      footer={
        <>
          <Button variant="outline" size="sm" onClick={onClose} disabled={saving}>Vazgeç</Button>
          <Button size="sm" onClick={submit} disabled={saving}>{saving ? "Kaydediliyor…" : "Kaydet"}</Button>
        </>
      }
    >
      {err && (
        <div className="mb-4 rounded-lg border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {err}
        </div>
      )}

      <div className="space-y-5">
        <div className="grid grid-cols-2 gap-4">
          <Field label="Ad"><Input value={form.name} onChange={(e) => set("name", e.target.value)} placeholder="örn. Intranet Portal" /></Field>
          <Field label="İzleme Tipi">
            <Select value={form.type} onChange={(e) => set("type", e.target.value)}>
              {grouped.map((g) => (
                <optgroup key={g.group} label={g.group}>
                  {g.items.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                </optgroup>
              ))}
            </Select>
          </Field>
        </div>
        <p className="rounded-lg border border-border/60 bg-muted/20 px-3 py-2 text-xs text-muted-foreground">{tm.hint}</p>

        <div className="grid grid-cols-3 gap-4">
          <div className={tm.port ? "col-span-2" : "col-span-3"}>
            <Field label={tm.target.label} hint={tm.target.hint}>
              <Input value={form.target} onChange={(e) => set("target", e.target.value)} className="font-mono text-xs" />
            </Field>
          </div>
          {tm.port && (
            <Field label="Port" hint={tm.port.hint}>
              <Input type="number" value={form.port ?? ""} onChange={(e) => set("port", numOrNull(e.target.value))} placeholder="—" />
            </Field>
          )}
        </div>

        {tm.extra && (
          <Field label={tm.extra.label} hint={tm.extra.hint}>
            <Input value={form.extra ?? ""} onChange={(e) => set("extra", e.target.value || null)} className="font-mono text-xs" />
          </Field>
        )}

        {tm.cred && (
          <Field label={tm.cred.label} hint={tm.cred.hint}>
            <Select value={form.credentialId ?? ""} onChange={(e) => set("credentialId", e.target.value ? Number(e.target.value) : null)}>
              <option value="">— Yok —</option>
              {(meta?.credentials ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </Field>
        )}

        <div className="flex flex-wrap gap-6 rounded-lg border border-border/60 bg-muted/30 p-4">
          <Switch checked={form.enabled} onChange={(v) => set("enabled", v)} label="Aktif" />
          {tm.ssl && <Switch checked={form.useSsl} onChange={(v) => set("useSsl", v)} label={tm.ssl} />}
          {tm.cert && <Switch checked={form.ignoreCertErrors} onChange={(v) => set("ignoreCertErrors", v)} label="Sertifika hatalarını yoksay" />}
        </div>

        <div className="grid grid-cols-3 gap-4">
          <Field label="Aralık (dk)" hint="boş = global"><Input type="number" value={form.intervalMinutesOverride ?? ""} onChange={(e) => set("intervalMinutesOverride", numOrNull(e.target.value))} /></Field>
          <Field label="Zaman aşımı (sn)"><Input type="number" value={form.timeoutSeconds} onChange={(e) => set("timeoutSeconds", Number(e.target.value) || 15)} /></Field>
          <Field label={form.type === "OracleActiveSessions" ? "Session eşiği (adet)" : "Yavaşlık eşiği (ms)"} hint="boş = kapalı">
            <Input type="number" value={form.responseTimeThresholdMs ?? ""} onChange={(e) => set("responseTimeThresholdMs", numOrNull(e.target.value))} />
          </Field>
        </div>

        {tm.health && (
          <div className="rounded-lg border border-border/60 bg-muted/30 p-4">
            <p className="mb-3 text-sm font-medium text-muted-foreground">Sağlık Eşikleri <span className="font-normal">(aşılırsa DOWN + alarm; boş metrik yalnız ölçülür)</span></p>
            <div className="grid grid-cols-3 gap-4">
              <Field label="CPU eşiği (%)"><Input type="number" value={form.cpuThresholdPercent ?? ""} onChange={(e) => set("cpuThresholdPercent", numOrNull(e.target.value))} placeholder="örn. 90" /></Field>
              <Field label="RAM eşiği (%)"><Input type="number" value={form.ramThresholdPercent ?? ""} onChange={(e) => set("ramThresholdPercent", numOrNull(e.target.value))} placeholder="örn. 90" /></Field>
              <Field label="Disk eşiği (%)"><Input type="number" value={form.diskThresholdPercent ?? ""} onChange={(e) => set("diskThresholdPercent", numOrNull(e.target.value))} placeholder="örn. 85" /></Field>
            </div>
          </div>
        )}

        <Field label="Etiketler" hint="virgülle ayır"><Input value={form.keyword ?? ""} onChange={(e) => set("keyword", e.target.value || null)} placeholder="web, üretim" /></Field>
        <Field label="Açıklama"><Textarea value={form.description ?? ""} onChange={(e) => set("description", e.target.value || null)} /></Field>

        {CONTROL_TYPES.includes(form.type) && (
          // Yol haritası #1 — Self-Healing: yalnız Windows/Linux servis kontrol tiplerinde görünür
          <div className="rounded-lg border border-border/60 bg-muted/30 p-4">
            <p className="mb-1 text-sm font-medium text-muted-foreground">Self-Healing (otomatik iyileştirme)</p>
            <p className="mb-3 text-xs text-muted-foreground">
              Belirlenen sayıda ARDIŞIK kontrol DOWN görülürse (false-positive koruması) alarm üretmeden ÖNCE
              otomatik yeniden başlatma denenir. Denemeler biter ve servis hâlâ down ise normal alarm akışı çalışır.
              Müdahaleler Denetim'e kaydedilir.
            </p>
            <div className="flex flex-wrap items-end gap-6">
              <Switch checked={form.selfHealEnabled} onChange={(v) => set("selfHealEnabled", v)} label="Down olunca otomatik yeniden başlat" />
              {form.selfHealEnabled && (
                <>
                  <Field label="Kaç ardışık down sonrası" hint="1 = ilk down'da hemen dene">
                    <Input type="number" min={1} max={10} className="w-24"
                      value={form.selfHealAfterFailures ?? 1}
                      onChange={(e) => set("selfHealAfterFailures", Math.max(1, Math.min(10, Number(e.target.value) || 1)))} />
                  </Field>
                  <Field label="Deneme sayısı" hint="1-10 (sorun döngüsü başına)">
                    <Input type="number" min={1} max={10} className="w-24"
                      value={form.selfHealMaxRetries ?? 1}
                      onChange={(e) => set("selfHealMaxRetries", Math.max(1, Math.min(10, Number(e.target.value) || 1)))} />
                  </Field>
                </>
              )}
            </div>
          </div>
        )}

        <div>
          <p className="mb-2 text-sm font-medium text-muted-foreground">Alarm Kanalları</p>
          <div className="flex flex-wrap gap-6 rounded-lg border border-border/60 bg-muted/30 p-4">
            <Switch checked={form.alertMail} onChange={(v) => set("alertMail", v)} label="Mail" />
            <Switch checked={form.alertSms} onChange={(v) => set("alertSms", v)} label="SMS" />
            <Switch checked={form.alertWhatsapp} onChange={(v) => set("alertWhatsapp", v)} label="WhatsApp" />
            <Switch checked={form.alertCall} onChange={(v) => set("alertCall", v)} label="Arama" />
          </div>
        </div>
      </div>
    </Drawer>
  );
}
