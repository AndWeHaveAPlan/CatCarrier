namespace CatCarrier.PrometheusMetrics.LabelGroups;

public class WorkflowJobLabelGroup : ILabelGroup
{
    [PrometheusLabel("organization")]
    public required string Organization { get; init; }

    [PrometheusLabel("repo")]
    public required string Repo { get; init; }

    [PrometheusLabel("workflow_name")]
    public required string WorkflowName { get; init; }

    [PrometheusLabel("job_name")]
    public required string JobName { get; init; }

    [PrometheusLabel("runner_group_name")]
    public required string RunnerGroupName { get; init; }

    [PrometheusLabel("job_labels")]
    public required string JobLabels { get; init; }

    [PrometheusLabel("is_private_repo")]
    public required string IsPrivateRepo { get; init; }
}