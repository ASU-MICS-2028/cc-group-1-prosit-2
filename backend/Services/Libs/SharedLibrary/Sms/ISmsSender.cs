using HealthConnect.SharedLibrary.ValueObjects;

namespace HealthConnect.SharedLibrary.Sms;

/// <summary>Sends a text message. The MVP only logs it (see <see cref="LogOnlySmsSender"/>); a real provider plugs in here.</summary>
public interface ISmsSender
{
    Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken);
}
