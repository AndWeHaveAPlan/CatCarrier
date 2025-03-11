namespace CatCarrier.PrometheusMetrics;

public class PrometheusLabelAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}