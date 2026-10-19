using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatCarrier.Runners;

/// <summary>
/// Hands out an Authorization header for one organisation.
///
/// A configured token is returned as-is. A GitHub App signs a short JWT, trades
/// it for an installation token and caches that until shortly before it
/// expires. .NET signs RS256 on its own, so this needs no extra package.
/// </summary>
public sealed class GitHubTokenSource(RunnerOrgOptions options, HttpClient http, ILogger logger)
{
    /// <summary>
    /// Installation tokens last an hour. Renew early, because a token that
    /// expires mid-poll costs a whole interval.
    /// </summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public string Organization { get; } = options.Name;

    public async Task<AuthenticationHeaderValue> GetAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            return new AuthenticationHeaderValue("token", options.Token);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
            {
                return new AuthenticationHeaderValue("token", _token);
            }

            var installation = await CreateInstallationTokenAsync(cancellationToken);
            _token = installation.Token;
            _expiresAt = installation.ExpiresAt;
            logger.LogInformation(
                "Minted a GitHub App installation token for {Organization}, valid until {ExpiresAt:O}",
                Organization, _expiresAt);
            return new AuthenticationHeaderValue("token", _token);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<InstallationToken> CreateInstallationTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"app/installations/{options.InstallationId}/access_tokens");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt());

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<InstallationToken>(body, JsonOptions, cancellationToken);
        return token ?? throw new InvalidOperationException("GitHub returned an empty installation token");
    }

    private string CreateJwt()
    {
        // iat is backdated to absorb clock skew between here and GitHub, and
        // the lifetime stays under the ten minutes GitHub allows.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64Url("""{"alg":"RS256","typ":"JWT"}"""u8.ToArray());
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new JwtClaims
        {
            IssuedAt = now - 60,
            ExpiresAt = now + 540,
            Issuer = options.AppId!
        }, JsonOptions));

        var signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem());
        var signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private string PrivateKeyPem()
    {
        // A PEM carried through an environment variable often arrives with its
        // line breaks escaped. A real PEM is base64 and dashes, so a literal
        // backslash-n is always the escaped form and never key material.
        return options.PrivateKey!.Replace("\\n", "\n");
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed class JwtClaims
    {
        [JsonPropertyName("iat")]
        public long IssuedAt { get; init; }

        [JsonPropertyName("exp")]
        public long ExpiresAt { get; init; }

        [JsonPropertyName("iss")]
        public required string Issuer { get; init; }
    }

    private sealed class InstallationToken
    {
        [JsonPropertyName("token")]
        public string Token { get; init; } = "";

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
