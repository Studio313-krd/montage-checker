using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using MontageMonitor.Server.Features.Activity;
using MontageMonitor.Server.Features.Agents;
using MontageMonitor.Server.Features.Auth;
using MontageMonitor.Server.Features.Dashboard;
using MontageMonitor.Server.Features.Employees;
using MontageMonitor.Server.Features.Proxy;
using MontageMonitor.Server.Features.Rendering;
using MontageMonitor.Server.Features.Reports;
using MontageMonitor.Server.Features.Screenshots;
using MontageMonitor.Server.Features.Users;
using MontageMonitor.Server.Infrastructure.Persistence;
using MontageMonitor.Server.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("MontageMonitor.Server")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 21 * 1_024 * 1_024;
    options.MemoryBufferThreshold = 64 * 1_024;
});
builder.Services.Configure<ScreenshotStorageOptions>(
    builder.Configuration.GetSection(ScreenshotStorageOptions.SectionName));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Access-токен MontageMonitor. Вставьте только JWT без слова Bearer.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = [],
    });
});
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddMontageSecurity(builder.Configuration);
builder.Services.AddScoped<ActivityAggregationService>();
builder.Services.AddScoped<ExcelReportDataBuilder>();
builder.Services.AddSingleton<ExcelReportWorkbookWriter>();
builder.Services.AddHostedService<ActivityStaleSessionWorker>();
builder.Services.AddHostedService<ScreenshotRetentionWorker>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    // Server не публикует порт наружу и получает HTTP только от Caddy во внутренней Docker network.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "MontageMonitor — документация API";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MontageMonitor API v1");
    });
}

var webRootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var hasWebClient = Directory.Exists(webRootPath);

if (hasWebClient)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});
app.MapGet("/api/system/info", () => Results.Ok(new
{
    name = "MontageMonitor.Server",
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    environment = app.Environment.EnvironmentName,
}));

app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapAgentEndpoints();
app.MapAgentAdminEndpoints();
app.MapActivityEndpoints();
app.MapReportEndpoints();
app.MapRenderConfigurationEndpoints();
app.MapProxyConfigurationEndpoints();
app.MapScreenshotEndpoints();
app.MapScreenshotAdminEndpoints();
app.MapEmployeeEndpoints();
app.MapUserEndpoints();

if (hasWebClient)
{
    app.MapFallbackToFile("index.html");
}

await app.ApplyDatabaseMigrationsAsync();
await app.BootstrapOwnerAsync();
await app.RunAsync();

public partial class Program;
