import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Drawer } from "@/components/ui/drawer";
import { Input, Select, Textarea, Field, Switch } from "@/components/ui/input";
import {
  type ServiceItem, type ServiceInput, type ServicesMeta, CONTROL_TYPES,
  createService, updateService,
} from "@/lib/services";

const empty: ServiceInput = {
  name: "", type: "Http", target: "", port: null, extra: null,
  useSsl: false, ignoreCertErrors: true, credentialId: null, enabled: true,
  intervalMinutesOverride: null, responseTimeThresholdMs: null, timeoutSeconds: 15,
  cpuThresholdPercent: null, ramThresholdPercent: null, diskThresholdPercent: null,
  keyword: null, description: null,
  alertMail: true, alertSms: false, alertWhatsapp: false, alertCall: false,
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
  const isControl = CONTROL_TYPES.includes(form.type);

  async function submit() {
    setSaving(true); setErr(null);
    try {
      if (service) { await updateService(service.id, form); onSaved("Servis güncellendi."); }
      else { await createService(form); onSaved("Servis eklendi."); }
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
      title={service ? "Servisi Düzenle" : "Yeni Servis"}
      description={service ? service.name : "İzlenecek yeni bir servis tanımla"}
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
          <Field label="Tip">
            <Select value={form.type} onChange={(e) => set("type", e.target.value)}>
              {(meta?.types ?? [form.type]).map((t) => <option key={t} value={t}>{t}</option>)}
            </Select>
          </Field>
        </div>

        <div className="grid grid-cols-3 gap-4">
          <div className="col-span-2">
            <Field label="Hedef (Host/URL/IP)"><Input value={form.target} onChange={(e) => set("target", e.target.value)} placeholder="host.firma.local" /></Field>
          </div>
          <Field label="Port"><Input type="number" value={form.port ?? ""} onChange={(e) => set("port", numOrNull(e.target.value))} placeholder="—" /></Field>
        </div>

        <Field label={isControl ? "Servis adı (Windows servisi / systemd birimi)" : "Ekstra (beklenen kod / DB adı vb.)"}>
          <Input value={form.extra ?? ""} onChange={(e) => set("extra", e.target.value || null)} placeholder={isControl ? "Spooler / crond" : "opsiyonel"} />
        </Field>

        <Field label="Kimlik Bilgisi">
          <Select value={form.credentialId ?? ""} onChange={(e) => set("credentialId", e.target.value ? Number(e.target.value) : null)}>
            <option value="">— Yok —</option>
            {(meta?.credentials ?? []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </Select>
        </Field>

        <div className="flex flex-wrap gap-6 rounded-lg border border-border/60 bg-muted/30 p-4">
          <Switch checked={form.enabled} onChange={(v) => set("enabled", v)} label="Aktif" />
          <Switch checked={form.useSsl} onChange={(v) => set("useSsl", v)} label="SSL/TLS" />
          <Switch checked={form.ignoreCertErrors} onChange={(v) => set("ignoreCertErrors", v)} label="Sertifika hatalarını yoksay" />
        </div>

        <div className="grid grid-cols-3 gap-4">
          <Field label="Aralık (dk)" hint="boş = global"><Input type="number" value={form.intervalMinutesOverride ?? ""} onChange={(e) => set("intervalMinutesOverride", numOrNull(e.target.value))} /></Field>
          <Field label="Zaman aşımı (sn)"><Input type="number" value={form.timeoutSeconds} onChange={(e) => set("timeoutSeconds", Number(e.target.value) || 15)} /></Field>
          <Field label="Yavaşlık eşiği (ms)" hint="boş = kapalı"><Input type="number" value={form.responseTimeThresholdMs ?? ""} onChange={(e) => set("responseTimeThresholdMs", numOrNull(e.target.value))} /></Field>
        </div>

        <div className="grid grid-cols-3 gap-4">
          <Field label="CPU eşiği (%)"><Input type="number" value={form.cpuThresholdPercent ?? ""} onChange={(e) => set("cpuThresholdPercent", numOrNull(e.target.value))} /></Field>
          <Field label="RAM eşiği (%)"><Input type="number" value={form.ramThresholdPercent ?? ""} onChange={(e) => set("ramThresholdPercent", numOrNull(e.target.value))} /></Field>
          <Field label="Disk eşiği (%)"><Input type="number" value={form.diskThresholdPercent ?? ""} onChange={(e) => set("diskThresholdPercent", numOrNull(e.target.value))} /></Field>
        </div>

        <Field label="Etiketler" hint="virgülle ayır"><Input value={form.keyword ?? ""} onChange={(e) => set("keyword", e.target.value || null)} placeholder="web, üretim" /></Field>
        <Field label="Açıklama"><Textarea value={form.description ?? ""} onChange={(e) => set("description", e.target.value || null)} /></Field>

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
