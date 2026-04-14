using Permissions.Application;
using Permissions.Infrastructure;
using QueryProfiler.Core;
using QueryProfiler.Core.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddQueryProfiler();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Profiler endpoint — shows aggregated query stats
app.MapGet("/metrics/snapshot", (IQueryMetricsStore store) =>
    Results.Ok(store.GetSnapshot()));

app.Run();