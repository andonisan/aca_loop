using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.Modules.Execution;
using Orchestrator.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddOptionalAzureKeyVault();
builder.AddServiceDefaults();

builder.Services.RegisterAppModules(builder.Configuration);

using var host = builder.Build();

await host.Services.InitializeAppModulesAsync();

// One Container Apps Job execution = one dequeue attempt, then exit -- KEDA overscale (more
// executions than queued messages) is handled inside ExecutionService as a clean no-op, never an
// error (see the ai-pod-dispatch spec's "scale-to-zero job execution" requirement).
using (var scope = host.Services.CreateScope())
{
    var executionService = scope.ServiceProvider.GetRequiredService<ExecutionService>();
    await executionService.RunOnceAsync();
}
