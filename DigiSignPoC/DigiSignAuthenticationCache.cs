using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace DigiSignPoC;

public sealed class DigiSignAuthenticationCache
{
    private const string CacheKey = "DigiSign:SharedAuthentication";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions PersistedStateJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DigiSignAuthenticationCache> _logger;
    private readonly string _persistedStatePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DigiSignAuthenticationCache(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DigiSignAuthenticationCache> logger)
    {
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _persistedStatePath = Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "digisign-auth.json");

        var cfg = configuration.GetSection("DigiSign");
        var configuredBaseUrl = NormalizeBaseUrl(
            cfg["BaseUrl"] ?? "https://api.staging.digisign.org");
        var baseUrl = configuredBaseUrl;
        var bearerToken = cfg["BearerToken"].NullIfWhiteSpace();
        DateTimeOffset? tokenExpiresAt = TryReadJwtExpiration(bearerToken);
        DateTimeOffset? tokenObtainedAt =
            bearerToken is not null ? DateTimeOffset.UtcNow : null;

        if (bearerToken is null && TryLoadPersistedState() is { } persistedState)
        {
            baseUrl = persistedState.BaseUrl;
            bearerToken = persistedState.BearerToken;
            tokenExpiresAt =
                persistedState.TokenExpiresAt ?? TryReadJwtExpiration(bearerToken);
            tokenObtainedAt = persistedState.TokenObtainedAt;
        }

        var configuredEnvironmentMatches = string.Equals(
            baseUrl,
            configuredBaseUrl,
            StringComparison.OrdinalIgnoreCase);
        var accessKey = configuredEnvironmentMatches
            ? cfg["AccessKey"].NullIfWhiteSpace()
            : null;
        var secretKey = configuredEnvironmentMatches
            ? cfg["SecretKey"].NullIfWhiteSpace()
            : null;

        _cache.Set(
            CacheKey,
            new AuthenticationState(
                baseUrl,
                accessKey,
                secretKey,
                bearerToken,
                tokenExpiresAt,
                tokenObtainedAt));
    }

    public DigiSignAuthenticationSnapshot GetSnapshot()
    {
        var state = GetState();
        return new DigiSignAuthenticationSnapshot(
            state.BaseUrl,
            state.AccessKey is not null && state.SecretKey is not null,
            state.BearerToken is not null,
            state.TokenExpiresAt,
            state.TokenObtainedAt);
    }

    public async Task<DigiSignAuthenticationSnapshot> ConfigureAsync(
        string baseUrl,
        string? bearerToken,
        string? accessKey,
        string? secretKey,
        CancellationToken cancellationToken = default)
    {
        baseUrl = NormalizeBaseUrl(baseUrl);
        bearerToken = bearerToken.NullIfWhiteSpace();
        accessKey = accessKey.NullIfWhiteSpace();
        secretKey = secretKey.NullIfWhiteSpace();

        if (bearerToken is null && (accessKey is null || secretKey is null))
        {
            throw new DigiSignAuthenticationException(
                "Enter a bearer token, or enter both the DigiSign access key and secret key.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = new AuthenticationState(
                baseUrl,
                accessKey,
                secretKey,
                bearerToken,
                TryReadJwtExpiration(bearerToken),
                bearerToken is not null ? DateTimeOffset.UtcNow : null);

            if (bearerToken is null)
            {
                state = await RequestTokenAsync(state, cancellationToken);
            }

            await PersistStateAsync(state, cancellationToken);
            _cache.Set(CacheKey, state);
            return GetSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string? expectedBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = GetState();
            if (expectedBaseUrl is not null &&
                !string.Equals(
                    NormalizeBaseUrl(expectedBaseUrl),
                    state.BaseUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new DigiSignAuthenticationException(
                    "The shared DigiSign environment changed after this flow started. Restore the original credentials or start a new flow.");
            }

            if (state.BearerToken is null ||
                state.TokenExpiresAt is not null &&
                state.TokenExpiresAt <= DateTimeOffset.UtcNow.Add(RefreshMargin))
            {
                if (state.AccessKey is null || state.SecretKey is null)
                {
                    throw new DigiSignAuthenticationException(
                        "The shared bearer token is missing or expired. Configure DigiSign credentials again.");
                }

                state = await RequestTokenAsync(state, cancellationToken);
                await PersistStateAsync(state, cancellationToken);
                _cache.Set(CacheKey, state);
            }

            var http = _httpClientFactory.CreateClient("DigiSign");
            http.BaseAddress = new Uri($"{state.BaseUrl}/");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", state.BearerToken);
            return http;
        }
        finally
        {
            _gate.Release();
        }
    }

    private PersistedAuthenticationState? TryLoadPersistedState()
    {
        if (!File.Exists(_persistedStatePath))
        {
            return null;
        }

        try
        {
            var persistedState = JsonSerializer.Deserialize<PersistedAuthenticationState>(
                File.ReadAllText(_persistedStatePath),
                PersistedStateJsonOptions);
            if (persistedState?.BearerToken.NullIfWhiteSpace() is not { } bearerToken)
            {
                return null;
            }

            return persistedState with
            {
                BaseUrl = NormalizeBaseUrl(persistedState.BaseUrl),
                BearerToken = bearerToken
            };
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                DigiSignAuthenticationException)
        {
            _logger.LogWarning(
                exception,
                "The persisted DigiSign bearer token could not be loaded from {Path}.",
                _persistedStatePath);
            return null;
        }
    }

    private async Task PersistStateAsync(
        AuthenticationState state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_persistedStatePath)!;
        var temporaryPath = $"{_persistedStatePath}.tmp";
        var persistedState = new PersistedAuthenticationState(
            state.BaseUrl,
            state.BearerToken
                ?? throw new DigiSignAuthenticationException(
                    "DigiSign returned no bearer token to save."),
            state.TokenExpiresAt,
            state.TokenObtainedAt);

        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(persistedState, PersistedStateJsonOptions),
                cancellationToken);
            File.Move(temporaryPath, _persistedStatePath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new DigiSignAuthenticationException(
                $"The bearer token could not be saved to {_persistedStatePath}.",
                exception);
        }
    }

    private async Task<AuthenticationState> RequestTokenAsync(
        AuthenticationState state,
        CancellationToken cancellationToken)
    {
        var http = _httpClientFactory.CreateClient("DigiSign");
        http.BaseAddress = new Uri($"{state.BaseUrl}/");

        var payload = JsonSerializer.Serialize(new
        {
            accessKey = state.AccessKey,
            secretKey = state.SecretKey
        });

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                "api/auth-token",
                new StringContent(payload, Encoding.UTF8, "application/json"),
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new DigiSignAuthenticationException(
                "DigiSign could not be reached while obtaining a bearer token.",
                exception);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DigiSignAuthenticationException(
                $"DigiSign rejected the API credentials ({(int)response.StatusCode} {response.StatusCode}).");
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("token", out var tokenElement) ||
                tokenElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                throw new DigiSignAuthenticationException(
                    "DigiSign authentication succeeded but returned no bearer token.");
            }

            var token = tokenElement.GetString()!;
            var expiresAt = document.RootElement.TryGetProperty("exp", out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64())
                : TryReadJwtExpiration(token);

            return state with
            {
                BearerToken = token,
                TokenExpiresAt = expiresAt,
                TokenObtainedAt = DateTimeOffset.UtcNow
            };
        }
        catch (JsonException exception)
        {
            throw new DigiSignAuthenticationException(
                "DigiSign returned an invalid authentication response.",
                exception);
        }
    }

    private AuthenticationState GetState() =>
        _cache.Get<AuthenticationState>(CacheKey)
        ?? throw new DigiSignAuthenticationException(
            "Shared DigiSign credentials are not configured.");

    private static string NormalizeBaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new DigiSignAuthenticationException(
                "DigiSign base URL must be an absolute HTTP or HTTPS URL.");
        }

        return value.Trim().TrimEnd('/');
    }

    private static DateTimeOffset? TryReadJwtExpiration(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            return document.RootElement.TryGetProperty("exp", out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64())
                : null;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private sealed record AuthenticationState(
        string BaseUrl,
        string? AccessKey,
        string? SecretKey,
        string? BearerToken,
        DateTimeOffset? TokenExpiresAt,
        DateTimeOffset? TokenObtainedAt);

    private sealed record PersistedAuthenticationState(
        string BaseUrl,
        string BearerToken,
        DateTimeOffset? TokenExpiresAt,
        DateTimeOffset? TokenObtainedAt);
}

public sealed record DigiSignAuthenticationSnapshot(
    string BaseUrl,
    bool HasApiKeys,
    bool HasBearerToken,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? TokenObtainedAt)
{
    public bool IsConfigured => HasBearerToken || HasApiKeys;
}

public sealed class DigiSignAuthenticationException : Exception
{
    public DigiSignAuthenticationException(string message) : base(message)
    {
    }

    public DigiSignAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class AuthenticationStringExtensions
{
    internal static string? NullIfWhiteSpace(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
