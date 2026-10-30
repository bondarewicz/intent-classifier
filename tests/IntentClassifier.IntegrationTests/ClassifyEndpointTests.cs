namespace IntentClassifier.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using Api.Endpoints;
using Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Exercises the only HTTP surface end to end - a real ASP.NET Core pipeline (ClassifyQueryExceptionHandler,
// ProblemDetails, JSON contracts) - over the same Postgres container and seeded dataset as PipelineTests.
public sealed class ClassifyEndpointTests(PostgresFixture db) : IClassFixture<PostgresFixture>
{
    // A closed local port: Npgsql gets an immediate connection-refused, no timeout to wait out.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Pooling=false";
    private const string Endpoint = "/queries/classify";

    [Fact]
    public async Task Classify_returns_ranked_candidates_for_a_known_query()
    {
        using var api = CreateApi(swapGenerator: true);

        var response = await api.Client.PostAsJsonAsync(
            Endpoint,
            new ClassifyQueryRequest("My card has NOT arrived yet")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ClassifyQueryResponse>();
        Assert.NotNull(body);
        Assert.Equal("card_arrival", body!.Candidates[0].IntentId);
    }

    [Fact]
    public async Task Top_sets_how_many_candidates_come_back()
    {
        using var api = CreateApi(swapGenerator: true);

        var response = await api.Client.PostAsJsonAsync(
            Endpoint,
            new ClassifyQueryRequest("My card has NOT arrived yet", Top: 1)
        );

        var body = await response.Content.ReadFromJsonAsync<ClassifyQueryResponse>();
        Assert.Single(body!.Candidates);
    }

    [Fact]
    public async Task Omitted_top_means_three_candidates()
    {
        using var api = CreateApi(swapGenerator: true);

        var response = await api.Client.PostAsJsonAsync(
            Endpoint,
            new { query = "My card has NOT arrived yet" }
        );

        var body = await response.Content.ReadFromJsonAsync<ClassifyQueryResponse>();
        Assert.Equal(3, body!.Candidates.Count);
    }

    [Fact]
    public async Task Top_below_one_is_a_validation_problem()
    {
        using var api = CreateApi(swapGenerator: true);

        var response = await api.Client.PostAsJsonAsync(
            Endpoint,
            new ClassifyQueryRequest("My card has NOT arrived yet", Top: 0)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Classify_returns_a_validation_problem_for_an_empty_query()
    {
        using var api = CreateApi(swapGenerator: true);

        var response = await api.Client.PostAsJsonAsync(Endpoint, new ClassifyQueryRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Classify_returns_service_unavailable_for_a_new_query_with_no_api_key()
    {
        using var api = CreateApi(swapGenerator: false);

        var response = await api.Client.PostAsJsonAsync(
            Endpoint,
            new ClassifyQueryRequest("a brand new question never seen before")
        );

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Classify_returns_a_problem_details_500_when_the_database_is_unreachable()
    {
        using var api = CreateApi(
            swapGenerator: true,
            connectionString: UnreachableConnectionString
        );

        var response = await api.Client.PostAsJsonAsync(
            Endpoint,
            new ClassifyQueryRequest("where is the virtual card?")
        );

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain(UnreachableConnectionString, body);
    }

    // swapGenerator: true replaces OpenAI with the deterministic hashing generator (used for
    // every test except the one that needs a real EmbeddingUnavailableException); either way,
    // OpenAi:ApiKey is overridden in-memory rather than read from appsettings.
    private TestApi CreateApi(bool swapGenerator, string? connectionString = null)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString ?? db.ConnectionString,
        };
        if (!swapGenerator)
            overrides["OpenAi:ApiKey"] = "";

        var baseFactory = new WebApplicationFactory<Program>();
        var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(
                (_, config) => config.AddInMemoryCollection(overrides)
            );
            if (swapGenerator)
                builder.ConfigureServices(services =>
                    services.AddSingleton<IEmbeddingGenerator, HashingEmbeddingGenerator>()
                );
        });
        return new TestApi(baseFactory, factory);
    }

    // Bundles every disposable WebApplicationFactory.CreateClient() creates - the derived
    // factory, its underlying base factory, and the HttpClient - behind one using.
    private sealed class TestApi(
        WebApplicationFactory<Program> baseFactory,
        WebApplicationFactory<Program> factory
    ) : IDisposable
    {
        public HttpClient Client { get; } = factory.CreateClient();

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
            baseFactory.Dispose();
        }
    }
}
