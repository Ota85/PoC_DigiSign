using System.Text.Json;

namespace DigiSignPoC;

public sealed record DigiSignCompletion(
    string FlowId,
    string IdentificationId,
    DateTimeOffset ReceivedAt,
    string? ProviderStatus,
    string? ApprovalMode,
    string? CompletedAt,
    string? ApprovedAt,
    string? DeniedAt,
    int? ProviderHttpStatus,
    string? ProviderResponseJson,
    string? ErrorMessage,
    Dictionary<string, string> QueryParams);

public static class DigiSignSession
{
    public const string BaseUrlKey = "DigiSign:BaseUrl";
    public const string BearerTokenKey = "DigiSign:BearerToken";
    public const string IdentificationIdKey = "DigiSign:IdentificationId";
    public const string FlowIdKey = "DigiSign:FlowId";
    private const string CompletionKey = "DigiSign:Completion";

    public static void ClearCompletion(ISession session) => session.Remove(CompletionKey);

    public static void SetCompletion(ISession session, DigiSignCompletion completion) =>
        session.SetString(CompletionKey, JsonSerializer.Serialize(completion));

    public static DigiSignCompletion? GetCompletion(ISession session)
    {
        var value = session.GetString(CompletionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DigiSignCompletion>(value);
        }
        catch (JsonException)
        {
            session.Remove(CompletionKey);
            return null;
        }
    }
}
