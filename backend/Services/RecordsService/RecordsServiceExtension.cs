using Amazon;
using Amazon.S3;
using Amazon.SimpleNotificationService;
using Dapper;
using HealthConnect.Data;
using HealthConnect.RecordsService.Calls;
using HealthConnect.RecordsService.Demo;
using HealthConnect.RecordsService.Events;
using HealthConnect.RecordsService.Features;
using HealthConnect.RecordsService.Media;
using HealthConnect.RecordsService.Patients;
using HealthConnect.RecordsService.Payments;
using HealthConnect.SharedLibrary.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Appointment events (Lab 7) go to the environment's SNS topic when the deploy names one; otherwise nowhere.
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            return new AppointmentEventOptions(configuration["APPOINTMENT_TOPIC_ARN"], configuration["APP_ENV"] ?? "local");
        });
        services.AddSingleton<IAppointmentEvents>(sp =>
        {
            var options = sp.GetRequiredService<AppointmentEventOptions>();
            if (string.IsNullOrWhiteSpace(options.TopicArn))
            {
                return new NoAppointmentEvents();
            }

            var region = sp.GetRequiredService<IConfiguration>()["AWS_REGION"];
            return new SnsAppointmentEvents(
                new Lazy<IAmazonSimpleNotificationService>(() => region is { Length: > 0 }
                    ? new AmazonSimpleNotificationServiceClient(RegionEndpoint.GetBySystemName(region))
                    : new AmazonSimpleNotificationServiceClient()),
                options,
                sp.GetRequiredService<ILogger<SnsAppointmentEvents>>());
        });

        // Teleconsult video calls (Lab 7 Part C): one-time tickets, the rooms, and the ICE servers handed to the app.
        services.AddSingleton<CallTickets>();
        services.AddSingleton<CallRooms>();
        services.AddSingleton(sp => CallOptions.From(sp.GetRequiredService<IConfiguration>()));

        // Consultation fees by mobile money (Paystack). Off until the environment's Paystack secret exists.
        services.AddSingleton(sp => PaymentSettings.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<PaymentStore>();
        services.AddSingleton<PatientRecords>();
        services.AddHttpClient(PaystackClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<IPaystack>(sp => new PaystackClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(PaystackClient.HttpClientName),
            sp.GetRequiredService<PaymentSettings>(),
            sp.GetRequiredService<ILogger<PaystackClient>>()));

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
            .AddFeature<CallSocket>()
            .AddFeature<GetPayment>()
            .AddFeature<PostCheckout>()
            .AddFeature<PaystackWebhook>()
            .AddFeature<GetPayments>()
            .AddFeature<GetMyHealth>();
    }
}
