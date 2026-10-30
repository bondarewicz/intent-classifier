namespace IntentClassifier.Domain.Intents;

// How an intent is represented in vector space when classifying a query against it.
public enum IntentRepresentation
{
    // Mean embedding of the intent's train queries (prototype). Default: far more accurate.
    Centroid,

    Name,
}
