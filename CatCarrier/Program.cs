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

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            var githubWebhookPath = builder.Configuration.GetSection("Github")["WebhookPath"];
            if (string.IsNullOrWhiteSpace(githubWebhookPath))
            {
                githubWebhookPath = "/github";
            }

            app.MapGitHubWebhooks(githubWebhookPath, builder.Configuration.GetSection("Github")["WebhookSecret"])
                .AddEndpointFilter<IEndpointConventionBuilder, ExceptionFilter>();


            Metrics.SuppressDefaultMetrics();
            app.MapMetrics();

            app.MapGet("/", () => { });

            app.Run();
        }
    }
}
