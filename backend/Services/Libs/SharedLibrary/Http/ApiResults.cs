using Microsoft.AspNetCore.Http;

namespace HealthConnect.SharedLibrary.Http;

/// <summary>
/// The error shapes the app expects (docs/frontend-needs.md): a refused request is a status code and, for validation, the
/// fields that are wrong. 401 signs the person out, 403 keeps the change queued, 409 is "needs review", 422 lists the fields.
/// </summary>
public static class ApiResults
{
    /// <summary>A plain refusal with a short code the app can show or log: { "error": "unknown_number" }.</summary>
    public static IResult Error(int statusCode, string code) =>
        Results.Json(new { error = code }, statusCode: statusCode);

    /// <summary>422 with the wrong fields: { "errors": { "phone": "..." } }.</summary>
    public static IResult Validation(IReadOnlyDictionary<string, string> errors) =>
        Results.Json(new { errors }, statusCode: StatusCodes.Status422UnprocessableEntity);

    public static IResult Validation(string field, string message) =>
        Validation(new Dictionary<string, string> { [field] = message });
}
