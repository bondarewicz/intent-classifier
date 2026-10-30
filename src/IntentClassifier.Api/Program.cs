using IntentClassifier.Api;
using IntentClassifier.Api.Endpoints;
using IntentClassifier.Application;
using IntentClassifier.Infrastructure;
using IntentClassifier.Infrastructure.Persistence;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// appsettings.json < OPENAI_API_KEY < user-secrets (shared with the CLI) < environment variables.
builder
    .Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["OpenAi:ApiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        }
    )
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables();

builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<ClassifyQueryExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Fail at startup, not on the first request, when ConnectionStrings:Postgres is missing.
_ = app.Services.GetRequiredService<NpgsqlDataSource>();

app.UseExceptionHandler();

if (app.Configuration.GetValue("Database:AutoMigrate", false))
    app.Services.GetRequiredService<DatabaseMigrator>().MigrateUp();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapClassifyQueryEndpoints();

app.Run();

// Exposed for WebApplicationFactory-based tests.
public partial class Program;
