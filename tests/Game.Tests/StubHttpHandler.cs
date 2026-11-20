using System.Net;
using System.Text;

namespace Game.Tests;

public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;

    public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = new();

    public StubHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

    public static StubHttpHandler Json(HttpStatusCode status, string json) =>
        new((_, _) => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return _respond(request, body);
    }
}
