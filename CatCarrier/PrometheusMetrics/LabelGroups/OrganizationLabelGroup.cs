namespace CatCarrier.PrometheusMetrics.LabelGroups;

public class OrganizationLabelGroup : ILabelGroup
{
    [PrometheusLabel("organization")]
    public required string Organization { get; init; }
}
