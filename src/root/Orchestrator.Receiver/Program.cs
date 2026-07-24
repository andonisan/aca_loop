using Orchestrator.BuildingBlocks.API;
using Orchestrator.BuildingBlocks.CQS;
using Orchestrator.BuildingBlocks.Modules;
using Orchestrator.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOptionalAzureKeyVault();
builder.AddServiceDefaults();

builder.Services.AddApiServices();
builder.Services.AddVsaCqsArchitecture();
builder.Services.RegisterAppModules(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseApiServices();
app.UseAppModules();

await app.Services.InitializeAppModulesAsync();

app.Run();
