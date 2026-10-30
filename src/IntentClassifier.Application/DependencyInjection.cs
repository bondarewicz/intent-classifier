namespace IntentClassifier.Application;

using Classification;
using Embeddings;
using Evaluation;
using Intents;
using Microsoft.Extensions.DependencyInjection;
using Queries;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services
            .AddTransient<ClassifyQueryHandler>()
            .AddTransient<ConfirmLabelHandler>()
            .AddTransient<EmbedDatasetHandler>()
            .AddTransient<TrainIntentsHandler>()
            .AddTransient<ClassifyAllHandler>()
            .AddTransient<EvaluateHandler>();
}
