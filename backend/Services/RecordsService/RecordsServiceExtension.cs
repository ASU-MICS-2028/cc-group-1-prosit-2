using Amazon;
using Amazon.S3;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Calls;
using HealthConnect.RecordsService.Demo;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Media;
using HealthConnect.SharedLibrary.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.RecordsService;

public static class RecordsServiceExtension
{
    /// <summary>Patients, vitals and appointments: saving them by the phone's own id, and pulling what other workers saved.</summary>
    public static IServiceCollection AddRecordsService(this IServiceCollection services)
    {
        // Columns are written with underscores (given_name); the C# row classes use PascalCase (GivenName).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Media goes straight from the phone to the private bucket the deploy names. The signer talks to AWS only when it first signs a link.
        services.AddSingleton(sp => new MediaOptions(sp.GetRequiredService<IConfiguration>()["MEDIA_BUCKET"]));
        services.AddSingleton<IDatabaseSeeder, DemoRecordsSeeder>();
        services.AddSingleton<IMediaUrlSigner>(sp => new S3MediaUrlSigner(new Lazy<IAmazonS3>(() =>
            sp.GetRequiredService<IConfiguration>()["AWS_REGION"] is { Length: > 0 } region
                ? new AmazonS3Client(RegionEndpoint.GetBySystemName(region))
                : new AmazonS3Client())));

        // Teleconsult video calls (Lab 7 Part C): one-time tickets, the rooms, and the ICE servers handed to the app.
        services.AddSingleton<CallTickets>();
        services.AddSingleton<CallRooms>();
        services.AddSingleton(sp => CallOptions.From(sp.GetRequiredService<IConfiguration>()));

        return services
            .AddFeature<PutPatient>()
            .AddFeature<GetPatients>()
            .AddFeature<PutVitals>()
            .AddFeature<GetVitals>()
            .AddFeature<PutAppointment>()
            .AddFeature<GetAppointments>()
            .AddFeature<PostAuditEvents>()
            .AddFeature<GetAuditEvents>()
            .AddFeature<VerifyAuditEvents>()
            .AddFeature<PostMediaUploadUrl>()
            .AddFeature<GetSurveillance>()
            .AddFeature<PostCallTicket>()
            .AddFeature<CallSocket>();
    }
}
