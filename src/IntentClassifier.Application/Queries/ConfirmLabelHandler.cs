namespace IntentClassifier.Application.Queries;

using Domain.Intents;
using Domain.Queries;

public sealed record ConfirmLabelResult(QueryText Text, IntentId IntentId);

// Promotes a query from the classified_queries log into labelled_queries(train) once a human
// confirms its intent. Every rule check and both writes happen atomically in
// IClassifiedQueryLog.Confirm; this handler only normalizes input. Never recomputes centroids -
// `train` owns that.
public sealed class ConfirmLabelHandler(IClassifiedQueryLog log)
{
    public async Task<ConfirmLabelResult> Handle(
        string rawQuery,
        string rawIntentId,
        CancellationToken ct = default
    )
    {
        var text = QueryText.Create(rawQuery);
        var intentId = IntentId.Create(rawIntentId);

        await log.Confirm(text, intentId, ct);
        return new ConfirmLabelResult(text, intentId);
    }
}
