using System.Net;
using System.Text;

namespace Orchestrator.Modules.Jira.UnitTests;

/// <summary>
/// Serves a scripted sequence of responses and records what was asked for, so tests can assert on the
/// exact endpoint, API version, auth header, and body the adapter produced. The last entry is reused
/// once the script is exhausted, which keeps retry tests from having to script every attempt.
/// <para>
/// Takes factories rather than instances deliberately: the adapter disposes each response it receives,
/// so handing the same <see cref="HttpResponseMessage"/> back for a retry would fail on a disposed
/// content stream instead of exercising the retry.
/// </para>
/// </summary>
sealed class RecordingHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
{
    int _index;

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public static Func<HttpResponseMessage> Json(string json) =>
        () =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

    public static Func<HttpResponseMessage> Empty(
        HttpStatusCode status = HttpStatusCode.NoContent
    ) => () => new HttpResponseMessage(status) { Content = new StringContent("", Encoding.UTF8) };

    public static Func<HttpResponseMessage> RateLimited(int? retryAfterSeconds = null) =>
        () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited", Encoding.UTF8),
            };
            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.Add("Retry-After", seconds.ToString());
            }

            return response;
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        Bodies.Add(
            request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken)
        );

        var factory = responses[Math.Min(_index, responses.Length - 1)];
        _index++;

        return factory();
    }
}
