using Prometheus;

namespace CatCarrier.Webhook;

public class ExceptionFilter : IEndpointFilter
{
    private readonly Counter _exceptionCounter = Metrics.CreateCounter("catcarrier_exceptions", "Number of exceptions thrown in CatCarrier");

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            var result = await next(context);
            var statusCode = context.HttpContext.Response.StatusCode;
            if (statusCode is >= 299 or <= 200)
            {
                _exceptionCounter.Inc();
            }
            return result;
        }
        catch (Exception e)
        {
            _exceptionCounter.Inc();
            return ValueTask.FromResult<object?>(Results.Json(e.Message, statusCode: 500));
        }


    }
}