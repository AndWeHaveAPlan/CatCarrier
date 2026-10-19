namespace CatCarrier.PrometheusMetrics.LabelGroups;

public class RunnerLabelGroup : ILabelGroup
{
    [PrometheusLabel("organization")]
    public required string Organization { get; init; }

    /// <summary>
    /// One label the runner registered with. A runner carries several labels
    /// and is counted under each one, so these series must never be summed
    /// across runner_label.
    /// </summary>
    [PrometheusLabel("runner_label")]
    public required string RunnerLabel { get; init; }
}
