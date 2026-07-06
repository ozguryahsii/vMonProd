import { apiGet, apiSend, apiForm } from "./api";

export interface AppSettings {
  // İzleme
  checkIntervalMinutes: number; failureThreshold: number; historyRetentionDays: number;
  // E-posta
  emailEnabled: boolean; smtpHost: string; smtpPort: number; mailFrom: string; mailRecipients: string;
  // LDAP + genel
  authEnabled: boolean; ldapAuthHost: string; ldapAuthPort: number; ldapAuthUseSsl: boolean;
  ldapAuthDomain: string; ldapAuthBaseDn: string; ldapAuthGroupDn: string;
  adminUsers: string; companyName: string; ldapSyncCredentialId: number | null;
  // OTP
  otpEnabled: boolean; otpChannel: string;
  // Yedekleme
  backupEnabled: boolean; backupPath: string; backupHour: number; backupMinute: number;
  backupRetentionCount: number; backupEncrypt: boolean; hasBackupPassword: boolean;
  // EOL
  eolEnabled: boolean; eolWarnDays: number; eolProxyUrl: string;
  // Güvenlik
  minPasswordLength: number; requirePasswordComplexity: boolean; passwordHistoryCount: number;
  trustInternalTlsCertificates: boolean; maxLoginAttempts: number; lockoutMinutes: number; auditRetentionDays: number;
  // SIEM
  syslogEnabled: boolean; syslogHost: string; syslogPort: number; syslogTcp: boolean;
  // SMS / WhatsApp
  smsEnabled: boolean; smsProvider: string; smsAccountSid: string; smsFrom: string; smsRecipients: string; hasSmsToken: boolean;
  whatsappEnabled: boolean; whatsappAccountSid: string; whatsappFrom: string; whatsappRecipients: string;
  whatsappAlarmTemplateSid: string; whatsappWebhookSecret: string; hasWhatsappToken: boolean;
  // Mutabakat
  mutabakatEnabled: boolean; mutabakatOwnCompany: string; mutabakatVendorCompany: string;
  // Logo
  loginLogoFile: string;
}

export const getSettings = (signal?: AbortSignal) => apiGet<AppSettings>("/settings", signal);

export function saveSettings(s: AppSettings, newSmsToken: string, newWhatsappToken: string, newBackupPassword: string) {
  // has* bayrakları modele girmez; sunucu sırları kendisi korur
  const { hasBackupPassword, hasSmsToken, hasWhatsappToken, ...model } = s;
  void hasBackupPassword; void hasSmsToken; void hasWhatsappToken;
  return apiSend<{ ok: boolean }>("POST", "/settings", {
    model,
    newSmsToken: newSmsToken || null,
    newWhatsappToken: newWhatsappToken || null,
    newBackupPassword: newBackupPassword || null,
  });
}

// Test uçları
export const testEmail = () => apiSend<{ ok: boolean }>("POST", "/test-email");
export const testSms = (to: string) => apiForm<{ ok: boolean; message: string }>("/test-sms", to ? { to } : {});
export const testWhatsapp = (to: string) => apiForm<{ ok: boolean; message: string }>("/test-whatsapp", to ? { to } : {});
export const testLdap = (username: string, password: string) =>
  apiForm<{ ok: boolean; message: string }>("/test-ldap-login", { username, password });
export const testSyslog = () => apiSend<{ ok: boolean; message: string }>("POST", "/syslog-test");
export const eolSyncNow = () => apiSend<{ ok: boolean; message: string }>("POST", "/eol-sync");

// ---- Lisans (Ayarlar > Lisans kartı: paket yükseltme/düşürme) ----
export interface LicenseState {
  machineCode: string;
  status: string;
  license: {
    edition: "Basic" | "Standard" | "Enterprise";
    company: string; issued: string; expires: string; daysLeft: number;
    maxMonitors: number | null; maxUsers: number | null; maxDashboards: number | null;
    sqliteOnly: boolean; emailOnly: boolean; siem: boolean;
  } | null;
}
export const getLicense = (signal?: AbortSignal) => apiGet<LicenseState>("/license", signal);
export const applyLicense = (key: string) =>
  apiSend<{ ok: boolean; edition: string; expires: string; message: string; warn: string | null }>("POST", "/license", { key });
