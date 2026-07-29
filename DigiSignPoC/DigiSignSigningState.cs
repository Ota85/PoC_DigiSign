using System.Text.Json;

namespace DigiSignPoC;

public sealed record DigiSignSigningFlow(
    string FlowId,
    string BaseUrl,
    string EnvelopeId,
    string RecipientId,
    string DocumentId,
    string OriginalFileName,
    string SigningMethod,
    DateTimeOffset CreatedAt);

public sealed record DigiSignSigningCompletion(
    string FlowId,
    string EnvelopeId,
    DateTimeOffset ReceivedAt,
    string? ProviderStatus,
    int? ProviderHttpStatus,
    string? ProviderResponseJson,
    string? CallbackEvent,
    string? CallbackError,
    string? ErrorMessage,
    Dictionary<string, string> QueryParams);

public static class DigiSignSigningSession
{
    private const string FlowKey = "DigiSign:Signing:Flow";
    private const string CompletionKey = "DigiSign:Signing:Completion";

    public static void SetFlow(ISession session, DigiSignSigningFlow flow)
    {
        session.SetString(FlowKey, JsonSerializer.Serialize(flow));
        session.Remove(CompletionKey);
    }

    public static DigiSignSigningFlow? GetFlow(ISession session) =>
        GetValue<DigiSignSigningFlow>(session, FlowKey);

    public static void SetCompletion(
        ISession session,
        DigiSignSigningCompletion completion) =>
        session.SetString(CompletionKey, JsonSerializer.Serialize(completion));

    public static DigiSignSigningCompletion? GetCompletion(ISession session) =>
        GetValue<DigiSignSigningCompletion>(session, CompletionKey);

    private static T? GetValue<T>(ISession session, string key)
    {
        var value = session.GetString(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch (JsonException)
        {
            session.Remove(key);
            return default;
        }
    }
}
