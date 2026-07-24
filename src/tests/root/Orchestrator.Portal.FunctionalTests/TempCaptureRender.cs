// TEMPORARY — capture the real rendered shell for the keyboard/theme passes, then delete this file.
using Orchestrator.Constants;

namespace Orchestrator.Portal.FunctionalTests;

public class TempCaptureRender
{
    const string Dir =
        "/private/tmp/claude-501/-Users-andoniplain-projects-personal-aca-loop/89906bbf-5434-4f72-b089-dad46c38bc38/scratchpad/preview";

    [Fact]
    public async Task Capture()
    {
        Directory.CreateDirectory(Dir);

        using var factory = new PortalTestFactory { Roles = [PortalRoles.Operator] };
        await File.WriteAllTextAsync(
            Path.Combine(Dir, "real-home.html"),
            await factory.CreateClient().GetStringAsync("/")
        );

        using var denyFactory = new PortalTestFactory { Roles = ["SomeUnrelated.Role"] };
        await File.WriteAllTextAsync(
            Path.Combine(Dir, "real-norole.html"),
            await denyFactory.CreateClient().GetStringAsync("/")
        );
    }
}
