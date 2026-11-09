namespace HealthConnect.Data.Migrations;

/// <summary>Reads a migration's SQL file out of the assembly (they are embedded, see Data.csproj).</summary>
internal static class MigrationSql
{
    public static string Read(string fileName)
    {
        var name = $"HealthConnect.Data.Sql.{fileName}";
        using var stream = typeof(MigrationSql).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The migration script {fileName} is not part of the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
