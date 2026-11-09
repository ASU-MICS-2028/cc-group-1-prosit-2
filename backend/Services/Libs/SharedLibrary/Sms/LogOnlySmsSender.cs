using HealthConnect.SharedLibrary.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HealthConnect.SharedLibrary.Sms;

/// <summary>
/// The MVP's "SMS": writes to the log instead of sending. The number is always masked. The text itself (which holds the
/// sign-in code) is written only when Sms:LogMessages is on, which is for a developer's laptop and never for a server.
/// No SMS provider is connected yet; that is a known limit stated in the report.
/// </summary>
public sealed partial class LogOnlySmsSender(IConfiguration configuration, ILogger<LogOnlySmsSender> logger) : ISmsSender
{
    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        if (configuration.GetValue<bool>("Sms:LogMessages"))
        {
            LogWithText(logger, to.Masked, message);
        }
        else
        {
            LogSent(logger, to.Masked, message.Length);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SMS not sent (no provider connected) to {Phone}: {Length} characters")]
    private static partial void LogSent(ILogger logger, string phone, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SMS not sent (no provider connected) to {Phone}: {Message}")]
    private static partial void LogWithText(ILogger logger, string phone, string message);
}
