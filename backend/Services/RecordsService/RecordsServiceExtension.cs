using Dapper;
using HealthConnect.RecordsService.Features;
using HealthConnect.SharedLibrary.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HealthConnect.RecordsService;

public static class RecordsServiceExtension
{
    /// <summary>Patients, vitals and appointments: saving them by the phone's own id, and pulling what other workers saved.</summary>
    public static IServiceCollection AddRecordsService(this IServiceCollection services)
    {
        // Columns are written with underscores (given_name); the C# row classes use PascalCase (GivenName).
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services
            .AddFeature<PutPatient>()
            .AddFeature<GetPatients>()
            .AddFeature<PutVitals>()
            .AddFeature<GetVitals>()
            .AddFeature<PutAppointment>()
            .AddFeature<GetAppointments>();
    }
}
