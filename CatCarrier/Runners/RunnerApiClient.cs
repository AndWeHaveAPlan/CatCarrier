using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatCarrier.Runners;

/// <summary>
/// Reads the self-hosted runners registered with an organisation.
/// </summary>
public sealed class RunnerApiClient(HttpClient http)
{
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<IReadOnlyList<Runner>> ListAsync(
        GitHubTokenSource tokens, CancellationToken cancellationToken)
    {
        var runners = new List<Runner>();

        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"orgs/{tokens.Organization}/actions/runners?per_page={PageSize}&page={page}");
            request.Headers.Authorization = await tokens.GetAsync(cancellationToken);

            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var batch = await JsonSerializer.DeserializeAsync<RunnerPage>(body, JsonOptions, cancellationToken);

            var items = batch?.Runners ?? [];
            runners.AddRange(items);

            // A short page is the last page. GitHub reports total_count as well,
            // but it counts every runner rather than the ones left to read.
            if (items.Count < PageSize)
            {
                return runners;
            }
        }
    }

    public sealed class RunnerPage
    {
        [JsonPropertyName("runners")]
        public List<Runner> Runners { get; init; } = [];
    }

    public sealed class Runner
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("status")]
        public string Status { get; init; } = "";

        [JsonPropertyName("busy")]
        public bool Busy { get; init; }

        [JsonPropertyName("labels")]
        public List<RunnerLabel> Labels { get; init; } = [];

        public bool IsOnline => string.Equals(Status, "online", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class RunnerLabel
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
    }
}
