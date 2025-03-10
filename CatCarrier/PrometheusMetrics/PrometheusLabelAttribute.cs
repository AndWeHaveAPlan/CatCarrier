namespace CatCarrier.PrometheusMetrics;

public class PrometheusLabelAttribute : Attribute
{
    public PrometheusLabelAttribute()
    {
    }

    public PrometheusLabelAttribute(string? name)
    {
        Name = name;
    }

    public string? Name { get; }
}