using Aspire.Hosting.Azure;
using Orchestrator.Constants;

var builder = DistributedApplication.CreateBuilder(args);

// Local dev/CI: the Azurite emulator (Docker, no Azure subscription needed) — mirrors the "ai-jobs"
// Storage Queue from the existing Terraform stack (terraform/storage.tf). Production keeps
// provisioning the real Storage Account via that hand-written Terraform, not Aspire's own Bicep/azd
// (see design.md D7 — Aspire is dev-time composition only).
var storage = builder.AddAzureStorage(Components.Storage).RunAsEmulator();
var queue = storage.AddQueue(Components.JobsQueue);

builder
    .AddProject<Projects.Orchestrator_Receiver>(Components.Receiver)
    .WithReference(queue)
    .WaitFor(queue);

builder
    .AddProject<Projects.Orchestrator_Agent>(Components.Agent)
    .WithReference(queue)
    .WaitFor(queue)
    .WithExplicitStart();

// The Portal reads the same queue (depth now, enqueue for the manual-dispatch verb later). Its other
// two sources — ACA executions and Log Analytics — have no emulator; until the read-model lands
// (tasks 3.1-3.2) there is nothing to stub, and when it does those panels degrade to "unavailable"
// locally rather than being faked into looking real.
//
// Local sign-in uses the stand-in principal instead of Entra, so the UI is workable with no app
// registration. It is Development-only by construction (see LocalDevAuthentication), and passing it
// here keeps the affordance visible in the composition rather than buried in appsettings.
builder
    .AddProject<Projects.Orchestrator_Portal>(Components.Portal)
    .WithReference(queue)
    .WaitFor(queue)
    .WithEnvironment("Portal__LocalDevAuth__Enabled", "true");

builder.Build().Run();
