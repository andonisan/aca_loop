using System.Net;
using Xunit.Sdk;

namespace Orchestrator.Portal.FunctionalTests;

/// <summary>
/// Status assertions that carry the server's own error output.
/// <para>
/// Deliberately not Shouldly: Shouldly's message would be "expected OK but was InternalServerError", which
/// is exactly the information that is not useful. This throws with the captured server errors appended, so
/// the assertion names the exception that caused the status.
/// </para>
/// </summary>
static class HttpAssertions
{
    public static void ShouldHaveStatus(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        throw new XunitException(
            $"Expected {(int)expected} {expected} but got {(int)response.StatusCode} "
                + $"{response.StatusCode} for {response.RequestMessage?.RequestUri}."
                + ServerErrorLog.Summary
        );
    }
}
