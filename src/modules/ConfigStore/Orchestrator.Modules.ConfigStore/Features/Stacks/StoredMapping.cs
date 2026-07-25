using Orchestrator.Modules.ConfigStore.Contracts;
using Orchestrator.Modules.Dispatch.Contracts;

namespace Orchestrator.Modules.ConfigStore.Features.Stacks;

/// <summary>
/// Turns a stored stack into the validated <see cref="MappingConfig"/> the resolver consumes.
/// <para>
/// It builds the raw <see cref="MappingFile"/> shape and hands it to <c>MappingLoader.Validate</c> rather
/// than constructing a <see cref="MappingConfig"/> directly. That is the whole point of design D5: rows and
/// files are two sources for one contract, and if the store built the validated form itself it would be a
/// second implementation of "what a valid mapping is" — free to drift on exactly the rules that decide
/// which states spend agent money.
/// </para>
/// <para>
/// The cost is a shape conversion that looks redundant. It is not: validation is what is being reused, and
/// the raw shape is its input.
/// </para>
/// </summary>
public static class StoredMapping
{
    public static MappingConfig ToMappingConfig(StoredStack stack) =>
        MappingLoader.Validate(
            new MappingFile
            {
                Targets =
                [
                    new MappingFileTarget
                    {
                        Vendor = stack.Vendor.Vendor.ToString(),
                        Project = stack.Vendor.Project,
                        Repo = string.IsNullOrEmpty(stack.Vendor.Repo) ? null : stack.Vendor.Repo,
                        // Written explicitly rather than left to default: a stored row always knows its
                        // code host, so relying on the file format's "defaults to vendor" rule here would
                        // hide a null that should have been caught on write.
                        CodeHost = stack.States.FirstOrDefault()?.CodeHost.ToString(),
                        CodeRepo = stack.States.FirstOrDefault()?.CodeRepo,
                        States = stack.States.ToDictionary(
                            s => s.NativeState,
                            s => new MappingFileState
                            {
                                Action = s.Action,
                                Runtime = s.Runtime.ToString(),
                                OnSuccessState = s.OnSuccessState,
                                OnFailureState = s.OnFailureState,
                            },
                            StringComparer.Ordinal
                        ),
                    },
                ],
            }
        );
}
