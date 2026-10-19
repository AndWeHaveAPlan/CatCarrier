namespace CatCarrier.Runners;

/// <summary>
/// Configuration for the self-hosted runner poller, bound from Github:Runners.
/// </summary>
public sealed class RunnersOptions
{
    public const string SectionName = "Github:Runners";

    /// <summary>
    /// Off by default, so an existing deployment keeps serving webhook metrics
    /// only until it is configured.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// One poll costs one request per 100 runners per org. The installation
    /// rate limit is 5000 requests an hour.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Regex over runner labels. Every label a runner carries becomes a series,
    /// and self-hosted fleets carry one label per machine class, so widen this
    /// deliberately.
    /// </summary>
    public string LabelAllowlist { get; set; } = ".*";

    /// <summary>
    /// The API root. Point this at a GitHub Enterprise Server installation, or
    /// at a stub when testing.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com/";

    public List<RunnerOrgOptions> Orgs { get; set; } = [];
}

public sealed class RunnerOrgOptions
{
    public string Name { get; set; } = "";

    /// <summary>
    /// A token short-circuits the GitHub App fields below. It needs the
    /// organization self-hosted runners read permission.
    /// </summary>
    public string? Token { get; set; }

    public string? AppId { get; set; }

    public string? InstallationId { get; set; }

    /// <summary>
    /// The App private key as PEM. PKCS#1 and PKCS#8 are both accepted.
    /// </summary>
    public string? PrivateKey { get; set; }

    /// <summary>
    /// A file holding the same PEM, for a key mounted from a secret.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    public bool HasAppCredentials =>
        !string.IsNullOrWhiteSpace(AppId)
        && !string.IsNullOrWhiteSpace(InstallationId)
        && (!string.IsNullOrWhiteSpace(PrivateKey) || !string.IsNullOrWhiteSpace(PrivateKeyPath));

    public bool IsUsable => !string.IsNullOrWhiteSpace(Name)
                            && (!string.IsNullOrWhiteSpace(Token) || HasAppCredentials);
}
