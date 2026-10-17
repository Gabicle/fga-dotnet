using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Permissions.Api.Security;
using Permissions.Application;
using Permissions.Application.Policies;
using Permissions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(sp => ApiKeyCredentials.FromConfiguration(
    sp.GetRequiredService<IConfiguration>()));
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ApiKeyPolicies.Check, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(ApiKeyPolicies.CapabilityClaim, ApiKeyPolicies.Check))
    .AddPolicy(ApiKeyPolicies.Admin, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(ApiKeyPolicies.CapabilityClaim, ApiKeyPolicies.Admin))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.Services.GetRequiredService<ApiKeyCredentials>();
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AbacEvaluator>().ValidateRequiredPolicies();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().RequireAuthorization();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
