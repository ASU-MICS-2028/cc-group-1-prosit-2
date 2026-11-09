using System.Text.Json;
using System.Text.Json.Serialization;
using HealthConnect.Data;
using HealthConnect.PlatformService;
using HealthConnect.SharedLibrary.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured logs on stdout (Docker sends them to CloudWatch). Never log personal data: log ids only.
builder.Host.UseSerilog((context, _, logger) =>
{
    logger.ReadFrom.Configuration(context.Configuration).Enrich.FromLogContext();
    if (context.HostingEnvironment.IsDevelopment())
    {
        logger.WriteTo.Console();
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Enums travel as the snake_case names the app uses ("district_officer", "follow_up").
// Numbers must be JSON numbers (not "45" as text), which also keeps the contract's number types exact.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// The API only runs behind nginx on a private Docker network, so trust its X-Forwarded-For for the client address
// (the audit log and the sign-in limits need the real one).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Each service adds itself here. A new service is one line.
builder.Services
    .AddData(builder.Configuration)
    .AddPlatformService();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging(options =>
{
    // The health check is called every few seconds by the load balancer and the deploy: keep it out of the normal logs.
    options.GetLevel = (httpContext, _, exception) =>
        exception is null && httpContext.Request.Path == "/health" ? LogEventLevel.Verbose : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapFeatures();

// Development only: the contract at /openapi/v1.json and Swagger UI at /swagger to try every endpoint.
// Never on the servers, where the API should not describe itself to the internet.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "HealthConnect API");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "HealthConnect API";
        options.EnablePersistAuthorization();
    });
}

app.Run();

// Lets the integration tests start the whole API in memory.
public partial class Program;
