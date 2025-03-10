namespace CatCarrier.PrometheusMetrics.LabelGroups;

public class WorkflowRunLabelGroup : ILabelGroup
{
    [PrometheusLabel("organization")]
    public required string Organization { get; init; }

    [PrometheusLabel("repo")]
    public required string Repo { get; init; }

    [PrometheusLabel("workflow_name")]
    public required string WorkflowName { get; init; }

    [PrometheusLabel("event")]
    public required string Event { get; init; }

}