using System.Reflection;

namespace CatCarrier.PrometheusMetrics;

public class PrometheusLabelsCollection<T>(T labelsGroup) where T : ILabelGroup
{
    private static readonly PropertyInfo[] Props = typeof(T).GetProperties()
        .Where(p => Attribute.IsDefined(p, typeof(PrometheusLabelAttribute))).ToArray();

    public static string[] GetNames()
    {
        return Props.Select(p => p.GetCustomAttribute<PrometheusLabelAttribute>()?.Name ?? p.Name).ToArray();
    }

    public string[] GetValues()
    {
        return Props.Select(p => p.GetValue(labelsGroup)?.ToString() ?? "").ToArray();
    }
}