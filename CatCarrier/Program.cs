using CatCarrier.Runners;
using CatCarrier.Webhook;
using Octokit.Webhooks;
using Octokit.Webhooks.AspNetCore;
using Prometheus;

namespace CatCarrier
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Configuration.AddEnvironmentVariables(prefix: "CAT_CARRIER_");

            builder.Services.AddControllers();
            builder.Services.AddSingleton<WebhookEventProcessor, EventProcessor>();

            // Runner gauges. The hosted service is added only when it is
            // configured to run, so an existing deployment keeps serving the
            // webhook metrics alone and never constructs the poller.
            var runnersSection = builder.Configuration.GetSection(RunnersOptions.SectionName);
            var runners = runnersSection.Get<RunnersOptions>() ?? new RunnersOptions();
            builder.Services.Configure<RunnersOptions>(runnersSection);

            if (runners.ShouldPoll)
            {
                builder.Services.AddHttpClient();
                builder.Services.AddHostedService<RunnerPoller>();
            }

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            var githubWebhookPath = builder.Configuration.GetSection("Github")["WebhookPath"];
            if (string.IsNullOrWhiteSpace(githubWebhookPath))
            {
                githubWebhookPath = "/github";
            }

            app.MapGitHubWebhooks(githubWebhookPath, builder.Configuration.GetSection("Github")["WebhookSecret"])
                .AddEndpointFilter<IEndpointConventionBuilder, ExceptionFilter>();


            if (!runners.ShouldPoll)
            {
                app.Logger.LogInformation(
                    "Runner polling is off. It needs {Section}:Enabled and at least one "
                    + "organisation with a Token, or AppId plus InstallationId plus PrivateKey.",
                    RunnersOptions.SectionName);
            }

            Metrics.SuppressDefaultMetrics();
            app.MapMetrics();

            app.MapGet("/", () => { });

            app.Run();
        }
    }
}
