using IntentClassifier.Application;
using IntentClassifier.Application.Abstractions;
using IntentClassifier.Cli;
using IntentClassifier.Domain.Queries;
using IntentClassifier.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using FluentMigratorException = FluentMigrator.Exceptions.FluentMigratorException;

if (!CliOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine(error);
    return 1;
}

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory }
);

// appsettings.json < OPENAI_API_KEY < user-secrets (shared with the API) < environment variables.
builder
    .Configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["OpenAi:ApiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        }
    )
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// The catch block below already reports a failed start as one line; don't also log its trace.
builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None);
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);

using var host = builder.Build();

try
{
    // Runs ValidateOnStart's options check; nothing else starts the host in a console app.
    await host.StartAsync();

    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;

    var exitCode = options!.Command switch
    {
        "migrate" => Commands.Migrate(services),
        "seed" => await Commands.Seed(services, options),
        "embed" => await Commands.Embed(services, options),
        "train" => await Commands.Train(services),
        "classify-all" => await Commands.ClassifyAll(services, options),
        "evaluate" => await Commands.Evaluate(services),
        "classify" when options.Positional.Count > 0 => await Commands.Classify(services, options),
        "label" when options.Positional.Count == 2 => await Commands.Label(services, options),
        _ => Usage(),
    };

    await host.StopAsync();
    return exitCode;
}
catch (Exception e)
    when (e
            is InvalidQueryException
                or EmbeddingUnavailableException
                or MissingConfigurationException
                or OptionsValidationException
                or NpgsqlException
                or FluentMigratorException
                or ArgumentException
                or FormatException
                or IOException
                or System.ClientModel.ClientResultException
                or System.Net.Http.HttpRequestException
    )
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

int Usage()
{
    Console.Error.WriteLine(CliOptions.Usage);
    return 1;
}
