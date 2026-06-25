using Renci.SshNet;
using vMonitor.Models;

namespace vMonitor.Services.Checkers;

/// <summary>SFTP: yetkili kullanıcıyla gerçek oturum açıp SFTP kanalını test eder.</summary>
public class SftpChecker : CheckerBase
{
    public override ServiceType Type => ServiceType.Sftp;

    protected override Task<string?> ExecuteAsync(MonitoredService service, Credential? credential, CancellationToken ct)
    {
        if (credential == null) return Task.FromResult<string?>("Kimlik bilgisi tanımlı değil");

        return Task.Run((Func<string?>)(() =>
        {
            var username = PlainUsername(credential);
            var connInfo = new Renci.SshNet.ConnectionInfo(
                service.Target,
                service.Port ?? 22,
                username,
                new PasswordAuthenticationMethod(username, PlainPassword(credential)))
            {
                Timeout = TimeSpan.FromSeconds(service.TimeoutSeconds)
            };

            using var sftp = new SftpClient(connInfo);
            sftp.Connect();
            sftp.ListDirectory("."); // kanalın gerçekten çalıştığını doğrula
            sftp.Disconnect();
            return null;
        }), ct);
    }
}
