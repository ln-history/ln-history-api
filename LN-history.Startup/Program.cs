using System.Text.Json.Serialization;
using Asp.Versioning;
using Bitcoin.Data;
using LN_history.Api;
using LN_history.Api.Instrumentation;
using LN_history.Api.SimpleApiKeyMiddleware;
using LN_history.Core;
using LN_history.Data;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// --- Observability ---
builder.Services.AddSingleton<AppMetrics>();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(AppMetrics.MeterName)
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
    options.AddOtlpExporter();
});

// --- Layers ---
builder.Services.AddLnHistoryDatabase(builder.Configuration);
builder.Services.AddBitcoinNode(builder.Configuration);
builder.Services.AddLightningNetworkServices(builder.Configuration);
builder.Services.AddApiServices();

// --- MVC / JSON ---
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower));
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.ReportApiVersions = true;
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Lightning Network History",
        Description = "Queries the ln-history PostgreSQL database and a Bitcoin Core node.",
        Version = "v1"
    });

    opt.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "x-api-key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API key required to access this API"
    });
    opt.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" },
                In = ParameterLocation.Header
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("ApiKeyMiddleware:Enabled"))
{
    app.UseMiddleware<SimpleApiKeyMiddleware>();
}

app.UseRouting();
app.MapControllers();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "Lightning Network History";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LN-history API V1");
});

app.Run();
