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

            builder.Services.AddControllers();
            builder.Services.AddSingleton<WebhookEventProcessor, EventProcessor>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            //app.UseAuthorization();

            app.MapControllers();

            app.MapGitHubWebhooks("/github");

            Metrics.SuppressDefaultMetrics();
            app.MapMetrics();

            app.Run();
        }
    }
}
