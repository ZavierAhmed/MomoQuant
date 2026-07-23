using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MomoQuant.Api.Hubs;
using MomoQuant.Api.Services;
using MomoQuant.Application;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.Options;
using MomoQuant.Application.Startup;
using MomoQuant.Domain.Enums;
using MomoQuant.Infrastructure;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Seeding;
using Serilog;
using Serilog.Events;
using Serilog.Filters;

var builder = WebApplication.CreateBuilder(args);
var (logDirectory, logDirectoryFallbackWarning) = ResolveLogDirectory(builder.Configuration);

builder.Host.UseSerilog((context, services, configuration) =>
{
    var outputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] " +
        "benchmarkRunId={BenchmarkRunId} runItemId={RunItemId} strategyCode={StrategyCode} symbol={Symbol} timeframe={Timeframe} " +
        "{Message:lj}{NewLine}{Exception}";

    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "MOMO Quant API")
        .WriteTo.Console(outputTemplate: outputTemplate)
        .WriteTo.File(
            Path.Combine(logDirectory, "momo-quant-api-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true,
            outputTemplate: outputTemplate)
        .WriteTo.Logger(logger => logger
            .Filter.ByIncludingOnly(Matching.FromSource("MomoQuant.Application.StrategyBenchmarks"))
            .WriteTo.File(
                Path.Combine(logDirectory, "benchmark-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: outputTemplate))
        .WriteTo.Logger(logger => logger
            .Filter.ByIncludingOnly(logEvent =>
                Matching.FromSource("MomoQuant.Application.Backtesting")(logEvent)
                || Matching.FromSource("MomoQuant.Application.Strategies.FourHourRange")(logEvent)
                || Matching.FromSource("MomoQuant.Application.Strategies.Implementations.FourHourRangeReEntryStrategy")(logEvent))
            .WriteTo.File(
                Path.Combine(logDirectory, "backtest-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: outputTemplate))
        .WriteTo.File(
            Path.Combine(logDirectory, "errors-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true,
            restrictedToMinimumLevel: LogEventLevel.Error,
            outputTemplate: outputTemplate)
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddSignalR();
builder.Services.AddSingleton<ILiveMarketEventPublisher, LiveMarketSignalREventPublisher>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "MOMO Quant API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

StartupSecretsValidator.ValidateOrThrow(builder.Configuration, builder.Environment);

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AdminOnly, policy =>
        policy.RequireRole(nameof(UserRole.Admin)));

    options.AddPolicy(AuthorizationPolicies.AdminOrTrader, policy =>
        policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Trader)));

    options.AddPolicy(AuthorizationPolicies.ResearchRead, policy =>
        policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Trader), nameof(UserRole.Viewer)));

    options.AddPolicy(AuthorizationPolicies.ResearchExecute, policy =>
        policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Trader)));
});

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPersistence(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddPolicy("DashboardDev", policy =>
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(logDirectoryFallbackWarning))
{
    app.Logger.LogWarning("{FallbackWarning}", logDirectoryFallbackWarning);
}

await app.Services.ApplyMigrationsAsync();

using (var scope = app.Services.CreateScope())
{
    var identitySeeder = scope.ServiceProvider.GetRequiredService<IIdentityDataSeeder>();
    await identitySeeder.SeedAsync();

    var exchangeSeeder = scope.ServiceProvider.GetRequiredService<IExchangeDataSeeder>();
    await exchangeSeeder.SeedAsync();

    var strategySeeder = scope.ServiceProvider.GetRequiredService<IStrategyDataSeeder>();
    await strategySeeder.SeedAsync();

    var riskSeeder = scope.ServiceProvider.GetRequiredService<IRiskDataSeeder>();
    await riskSeeder.SeedAsync();
}

try
{
    using var healthScope = app.Services.CreateScope();
    var strategyLabService = healthScope.ServiceProvider.GetRequiredService<MomoQuant.Application.StrategyLab.IStrategyLabService>();
    var labHealth = await strategyLabService.GetStartupHealthAsync();
    if (labHealth.Succeeded && labHealth.Data is not null)
    {
        if (labHealth.Data.Healthy)
        {
            app.Logger.LogInformation("Strategy Laboratory startup check: Healthy");
        }
        else
        {
            app.Logger.LogWarning(
                "Strategy Laboratory startup check: {Status}. Issues: {Issues}",
                labHealth.Data.Status,
                string.Join("; ", labHealth.Data.Issues));
        }
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Strategy Laboratory startup check failed.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("DashboardDev");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<LiveMarketHub>("/hubs/live-market");

app.Run();

static (string LogDirectory, string? Warning) ResolveLogDirectory(IConfiguration configuration)
{
    var configured = configuration.GetValue<string>("Logging:FilePath");
    var preferredPath = string.IsNullOrWhiteSpace(configured)
        ? @"C:\momo_quants_logs"
        : configured.Trim();

    try
    {
        Directory.CreateDirectory(preferredPath);
        return (preferredPath, null);
    }
    catch (Exception ex)
    {
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, "logs");
        try
        {
            Directory.CreateDirectory(fallbackPath);
            return (
                fallbackPath,
                $"Failed to create log directory '{preferredPath}'. Falling back to '{fallbackPath}'. Reason: {ex.Message}");
        }
        catch (Exception fallbackEx)
        {
            var warning =
                $"Failed to create preferred log directory '{preferredPath}' ({ex.Message}) and fallback '{fallbackPath}' ({fallbackEx.Message}). " +
                "Continuing with default console logging only.";
            Console.Error.WriteLine(warning);
            return (AppContext.BaseDirectory, warning);
        }
    }
}

public partial class Program;
