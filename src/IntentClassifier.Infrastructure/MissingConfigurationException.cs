namespace IntentClassifier.Infrastructure;

// Required configuration (a connection string, an API setting) is missing or blank. Distinct
// from InvalidOperationException so callers can catch this specific, expected startup failure
// without also swallowing unrelated bugs that happen to throw the same base type.
public sealed class MissingConfigurationException(string message) : Exception(message);
