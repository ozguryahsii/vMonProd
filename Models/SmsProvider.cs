namespace vMonitor.Models;

/// <summary>UI'dan tanımlanabilen genel (şablonlu) HTTP SMS sağlayıcısı.
/// Çoğu sağlayıcı "GET/POST + user/pass/numara/mesaj" düzenindedir; bu yüzden URL/gövde
/// şablonlarına yer tutucular konur ve gönderimde değiştirilir — kod değişikliği gerekmez.
/// Yer tutucular: {to} {from} {message} {user} {password} {apikey}</summary>
public class SmsProvider
{
    public int Id { get; set; }

    /// <summary>Kanal türü: Sms / Whatsapp / Voice / Ivr. Tek "Bildirim Kanalları" kutusundan yönetilir;
    /// hepsi aynı şablonlu HTTP isteğiyle çalışır (yer tutucular ortak).</summary>
    public string Kind { get; set; } = "Sms";

    /// <summary>Bu entegrasyonun kendi alıcı listesi (virgül/; ile). Boşsa global ayar alıcıları kullanılır.</summary>
    public string? Recipients { get; set; }

    /// <summary>WhatsApp (Twilio) için onaylı şablon Content SID'i (HX...). Doluysa alarm, butonlu şablon
    /// olarak gönderilir (interaktif); boşsa düz metin. Yalnızca Kind=Whatsapp için anlamlıdır.</summary>
    public string? TemplateSid { get; set; }

    /// <summary>Benzersiz görünen ad (Ayarlar'daki açılır listede çıkar). "Twilio" rezervedir (yerleşik).</summary>
    public string Name { get; set; } = "";

    /// <summary>GET veya POST.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>İstek URL'i (yer tutucular içerebilir). GET'te tüm parametreler burada olur.</summary>
    public string Url { get; set; } = "";

    /// <summary>POST gövde türü: "form" (application/x-www-form-urlencoded) veya "json".</summary>
    public string ContentType { get; set; } = "form";

    /// <summary>POST gövdesi şablonu (yer tutucular). form için "k1={to}&k2={message}", json için JSON metni.</summary>
    public string? Body { get; set; }

    /// <summary>Ek başlıklar — her satır "Anahtar: Değer" (yer tutucu olabilir). Opsiyonel.</summary>
    public string? Headers { get; set; }

    /// <summary>Kimlik doğrulama kısayolu: none / basic / bearer.</summary>
    public string AuthType { get; set; } = "none";

    public string Username { get; set; } = "";
    /// <summary>DPAPI ile şifreli.</summary>
    public string PasswordEncrypted { get; set; } = "";
    /// <summary>DPAPI ile şifreli (token/apikey).</summary>
    public string ApiKeyEncrypted { get; set; } = "";

    /// <summary>Gönderen / başlık (msgheader) — {from} yer tutucusunu besler.</summary>
    public string Sender { get; set; } = "";

    /// <summary>Boşsa: HTTP 2xx = başarılı. Doluysa: yanıt gövdesi bu metni de içermeli (ör. "00" / "success").</summary>
    public string? SuccessContains { get; set; }

    public bool Enabled { get; set; } = true;
}
