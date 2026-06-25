namespace vMonitor.Models;

/// <summary>İnteraktif alarm oturumu: bir servis için bir telefona alarm (buton) gönderildiğinde
/// oluşturulur. Kullanıcı butona basınca gelen webhook, en son açık oturumu bu telefonla eşleyip
/// hangi servise işlem yapılacağını bulur.</summary>
public class AlarmSession
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    /// <summary>Normalize telefon (yalnız rakamlar).</summary>
    public string Phone { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? HandledAt { get; set; }
    public string? Action { get; set; }
    public string? Result { get; set; }
}
