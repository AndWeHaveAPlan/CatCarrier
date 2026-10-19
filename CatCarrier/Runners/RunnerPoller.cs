using System.Text.RegularExpressions;
using CatCarrier.PrometheusMetrics;
using CatCarrier.PrometheusMetrics.LabelGroups;
using Microsoft.Extensions.Options;
using Prometheus;

namespace CatCarrier.Runners;

/// <summary>
/// Polls the organisation runners endpoint and publishes the fleet as gauges.
///
/// The workflow_job counters cannot answer this. They move when a job reaches
/// its next state, so a runner halfway through a build is invisible to them.
/// GitHub knows, because each runner reports itself busy.
/// </summary>
public sealed class RunnerPoller : BackgroundService
{
    private static readonly Gauge RegisteredGauge = Metrics.CreateGauge(
        "github_runners_registered",
        "Self-hosted runners registered with the organisation that carry this label. "
        + "A runner carries several labels and is counted under each one, so never sum across runner_label.",
        PrometheusLabelsCollection<RunnerLabelGroup>.GetNames());

    private static readonly Gauge OnlineGauge = Metrics.CreateGauge(
        "github_runners_online",
        "Registered runners carrying this label whose status is online.",
        PrometheusLabelsCollection<RunnerLabelGroup>.GetNames());

    private static readonly Gauge BusyGauge = Metrics.CreateGauge(
        "github_runners_busy",
        "Online runners carrying this label that report themselves busy.",
        PrometheusLabelsCollection<RunnerLabelGroup>.GetNames());

    private static readonly Gauge TotalGauge = Metrics.CreateGauge(
        "github_runners_total",
        "Self-hosted runners registered with the organisation, counted once each.",
        PrometheusLabelsCollection<OrganizationLabelGroup>.GetNames());

    /// <summary>
    /// The gauges above hold their last value, and a correct reading of an idle
    /// fleet is a constant zero. A frozen reading is a constant too, so nothing
    /// in those series tells them apart. This is the heartbeat that does:
    ///   time() - github_runners_last_success_timestamp_seconds > 300
    /// Per organisation, because each one polls on its own loop and one can be
    /// broken while the other is healthy.
    /// </summary>
    private static readonly Gauge LastSuccessGauge = Metrics.CreateGauge(
        "github_runners_last_success_timestamp_seconds",
        "Unix time of the last poll of this organisation that succeeded.",
        PrometheusLabelsCollection<OrganizationLabelGroup>.GetNames());

    private readonly RunnersOptions _options;
    private readonly RunnerApiClient _client;
    private readonly ILogger<RunnerPoller> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Regex _labelAllowlist;

    public RunnerPoller(
        IOptions<RunnersOptions> options,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<RunnerPoller>();
        _client = new RunnerApiClient(CreateHttpClient());
        try
        {
            _labelAllowlist = new Regex(
                _options.LabelAllowlist, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{RunnersOptions.SectionName}:LabelAllowlist is not a valid regex: "
                + $"'{_options.LabelAllowlist}'", exception);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var orgs = _options.Orgs.Where(org => org.IsUsable).ToArray();
        foreach (var rejected in _options.Orgs.Except(orgs))
        {
            _logger.LogError(
                "Ignoring organisation {Organization}: it needs a Token, or AppId plus "
                + "InstallationId plus PrivateKey or PrivateKeyPath",
                string.IsNullOrWhiteSpace(rejected.Name) ? "<unnamed>" : rejected.Name);
        }

        // Program.cs only registers this service when at least one organisation
        // is usable, so this is a guard against a future direct registration
        // rather than a path reached by configuration.
        if (orgs.Length == 0)
        {
            _logger.LogError("Runner polling started with no usable organisation.");
            return;
        }

        // One loop per organisation, so a rate-limited or broken org cannot
        // hold up the others.
        await Task.WhenAll(orgs.Select(org => PollForeverAsync(org, stoppingToken)));
    }

    private async Task PollForeverAsync(RunnerOrgOptions org, CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var tokens = new GitHubTokenSource(
            org, CreateHttpClient(), _loggerFactory.CreateLogger<GitHubTokenSource>());

        // Labels published on the previous round. A label that disappears must
        // be removed, or its gauge would sit at its last value for ever.
        var published = new HashSet<string>(StringComparer.Ordinal);
        var backoff = interval;

        _logger.LogInformation(
            "Polling runners for {Organization} every {Interval}", org.Name, interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var runners = await _client.ListAsync(tokens, stoppingToken);
                published = Publish(org.Name, runners, published);
                backoff = interval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Back off to eight intervals at most. A failing organisation
                // must not burn the rate limit the healthy ones still need.
                var firstFailure = backoff == interval;
                backoff = backoff < interval * 8 ? backoff * 2 : interval * 8;

                // The stack trace is worth one line per outage. An org that
                // stays down retries for ever, and repeating the trace every
                // few seconds buries everything else in the log.
                if (firstFailure)
                {
                    _logger.LogError(
                        exception,
                        "Polling runners for {Organization} failed. Next attempt in {Backoff}",
                        org.Name, backoff);
                }
                else
                {
                    _logger.LogError(
                        "Polling runners for {Organization} failed again: {Reason}. "
                        + "Next attempt in {Backoff}",
                        org.Name, exception.Message, backoff);
                }
            }

            try
            {
                await Task.Delay(backoff, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private HashSet<string> Publish(
        string organization, IReadOnlyList<RunnerApiClient.Runner> runners, HashSet<string> published)
    {
        var registered = new Dictionary<string, int>(StringComparer.Ordinal);
        var online = new Dictionary<string, int>(StringComparer.Ordinal);
        var busy = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var runner in runners)
        {
            foreach (var label in runner.Labels.Select(label => label.Name).Distinct(StringComparer.Ordinal))
            {
                if (!_labelAllowlist.IsMatch(label))
                {
                    continue;
                }

                registered[label] = registered.GetValueOrDefault(label) + 1;
                online[label] = online.GetValueOrDefault(label) + (runner.IsOnline ? 1 : 0);
                busy[label] = busy.GetValueOrDefault(label) + (runner.Busy ? 1 : 0);
            }
        }

        foreach (var label in registered.Keys)
        {
            var values = new PrometheusLabelsCollection<RunnerLabelGroup>(new RunnerLabelGroup
            {
                Organization = organization,
                RunnerLabel = label
            }).GetValues();

            RegisteredGauge.WithLabels(values).Set(registered[label]);
            OnlineGauge.WithLabels(values).Set(online[label]);
            BusyGauge.WithLabels(values).Set(busy[label]);
        }

        foreach (var label in published.Where(label => !registered.ContainsKey(label)))
        {
            RegisteredGauge.RemoveLabelled(organization, label);
            OnlineGauge.RemoveLabelled(organization, label);
            BusyGauge.RemoveLabelled(organization, label);
        }

        var orgLabel = new PrometheusLabelsCollection<OrganizationLabelGroup>(
            new OrganizationLabelGroup { Organization = organization }).GetValues();

        TotalGauge.WithLabels(orgLabel).Set(runners.Count);

        // Last, and only on the success path, so the heartbeat can never run
        // ahead of the numbers it vouches for.
        LastSuccessGauge.WithLabels(orgLabel).Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        return [.. registered.Keys];
    }

    private HttpClient CreateHttpClient()
    {
        var http = _httpClientFactory.CreateClient();
        // A trailing slash matters: without it the last path segment of a
        // base address is dropped when a relative request is resolved.
        http.BaseAddress = new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        http.DefaultRequestHeaders.Add("User-Agent", "CatCarrier");
        return http;
    }
}
