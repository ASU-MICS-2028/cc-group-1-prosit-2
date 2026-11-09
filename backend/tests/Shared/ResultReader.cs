using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.Tests.Shared;

/// <summary>Runs what a handler returned the way ASP.NET would, so a test sees the status code and JSON the app would get.</summary>
public static class ResultReader
{
    public static async Task<Reply> RunAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().ConfigureHttpJsonOptions(_ => { }).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return new Reply(context.Response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}

public sealed record Reply(int Status, JsonElement Body)
{
    public string? Error => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("error", out var e) ? e.GetString() : null;

    public string? FieldError(string field) =>
        Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out var message)
            ? message.GetString()
            : null;
}
