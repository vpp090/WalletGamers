namespace System.Tests;

public sealed class FaultInjectingHandler : DelegatingHandler
{
    // Requests matching the predicate get a 503.
    public Func<HttpRequestMessage, bool>? FailWhen { get; set; }

    // false: 503 without forwarding (request never arrives); true: forward, then discard the response (lost response).
    public bool ForwardThenFail { get; set; }

    public void Reset()
    {
        FailWhen = null;
        ForwardThenFail = false;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (FailWhen?.Invoke(request) != true)
            return await base.SendAsync(request, cancellationToken);

        if (ForwardThenFail)
        {
            using var forwarded = await base.SendAsync(request, cancellationToken);
            await forwarded.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
    }
}
