using CatCarrier.PrometheusMetrics;
using CatCarrier.PrometheusMetrics.LabelGroups;
using Octokit.Webhooks;
using Octokit.Webhooks.Events;
using Octokit.Webhooks.Events.WorkflowJob;
using Octokit.Webhooks.Events.WorkflowRun;
using Octokit.Webhooks.Models.WorkflowJobEvent;
using Prometheus;

namespace CatCarrier.Webhook
{
    public class EventProcessor : WebhookEventProcessor
    {
        /// <summary>
        /// 
        /// </summary>
        private readonly Counter _jobDurationCounter = Metrics.CreateCounter(
            "github_workflow_job_duration_seconds",
            "",
            PrometheusLabelsCollection<WorkflowJobLabelGroup>.GetNames()
            );

        /// <summary>
        /// 
        /// </summary>
        private readonly Counter _jobStartupCounter = Metrics.CreateCounter(
            "github_workflow_job_startup_seconds",
            "",
            PrometheusLabelsCollection<WorkflowJobLabelGroup>.GetNames()
        );

        /// <summary>
        /// 
        /// </summary>
        private readonly Counter _jobFailCounter = Metrics.CreateCounter(
            "github_workflow_job_fails_count",
            "",
            PrometheusLabelsCollection<WorkflowJobLabelGroup>.GetNames()
        );

        /// <summary>
        /// 
        /// </summary>
        private readonly Counter _workflowDurationCounter = Metrics.CreateCounter(
            "github_workflow_run_duration_seconds",
            "",
            PrometheusLabelsCollection<WorkflowRunLabelGroup>.GetNames()
        );

        /// <summary>
        /// 
        /// </summary>
        /// <param name="headers"></param>
        /// <param name="workflowJobEvent"></param>
        /// <param name="action"></param>
        /// <returns></returns>
        protected override Task ProcessWorkflowJobWebhookAsync(WebhookHeaders headers, WorkflowJobEvent workflowJobEvent,
            WorkflowJobAction action)
        {
            Console.WriteLine($"Received {workflowJobEvent} event with action {action}.");

            if (action != WorkflowJobAction.Completed)
            {
                return Task.CompletedTask;
            }

            var labels = new PrometheusLabelsCollection<WorkflowJobLabelGroup>(new WorkflowJobLabelGroup
            {
                Organization = workflowJobEvent.Organization?.Login ?? "",
                Repo = workflowJobEvent.Repository?.Name ?? "",
                WorkflowName = workflowJobEvent.WorkflowJob.WorkflowName ?? "",
                JobName = workflowJobEvent.WorkflowJob.Name,
                RunnerGroupName = workflowJobEvent.WorkflowJob.RunnerGroupName ?? "",
                JobLabels = string.Join(',', workflowJobEvent.WorkflowJob.Labels),
                IsPrivateRepo = workflowJobEvent.Repository?.Private.ToString() ?? ""
            });

            var runDuration = workflowJobEvent.WorkflowJob.CompletedAt - workflowJobEvent.WorkflowJob.StartedAt;
            _jobDurationCounter.WithLabels(labels.GetValues()).Inc(runDuration!.Value.TotalSeconds);

            var startupDuration = workflowJobEvent.WorkflowJob.StartedAt - workflowJobEvent.WorkflowJob.CreatedAt;
            _jobStartupCounter.WithLabels(labels.GetValues()).Inc(startupDuration!.Value.TotalSeconds);

            if (workflowJobEvent.WorkflowJob.Conclusion == WorkflowJobConclusion.Success)
            {
                _jobFailCounter.WithLabels(labels.GetValues()).Inc();
            }

            return Task.CompletedTask;
        }

        protected override Task ProcessWorkflowRunWebhookAsync(WebhookHeaders headers, WorkflowRunEvent workflowRunEvent,
            WorkflowRunAction action)
        {
            Console.WriteLine($"Received {workflowRunEvent} event with action {action}.");

            if (action != WorkflowRunAction.Completed)
            {
                return Task.CompletedTask;
            }

            var runDuration = workflowRunEvent.WorkflowRun.UpdatedAt - workflowRunEvent.WorkflowRun.RunStartedAt;
            var labels = new PrometheusLabelsCollection<WorkflowRunLabelGroup>(new WorkflowRunLabelGroup
            {
                Organization = workflowRunEvent.Organization?.Login ?? "",
                Repo = workflowRunEvent.Repository?.Name ?? "",
                WorkflowName = workflowRunEvent.WorkflowRun.Name,
                Event = workflowRunEvent.WorkflowRun.Event
            });

            _workflowDurationCounter.WithLabels(labels.GetValues()).Inc(runDuration!.TotalSeconds);

            return Task.CompletedTask;
        }
    }
}
