# IntentClassifier

Classifies banking customer queries into the 77 Banking77 intents using OpenAI embeddings and cosine
similarity computed in Postgres with [pgvector](https://github.com/pgvector/pgvector).

On the held-out Banking77 test split (3,079 queries):

| Intent represented by | Top-1 | Top-3 | Ambiguous* |
|---|---|---|---|
| **Centroid** of its train query embeddings (default) | **88.9%** | **97.2%** | 9.3% |
| Embedding of its name only | 62.2% | 79.1% | 21.6% |

\* best intent less than 0.02 ahead of the runner-up (`ClassificationConfidence.AmbiguousMargin`). Top-1 drops to
49.8% on ambiguous classifications vs 92.9% on confident ones - that is where a second opinion (e.g. an LLM) would pay off.

## How it works

**Build once.** Five CLI commands, in order:

| Step | Command | What it does | Writes to |
|---|---|---|---|
| 1 | `seed` | Loads the Banking77 CSVs: ~13k example queries, 77 intents | `labelled_queries`, `intents` |
| 2 | `embed` | Gets an OpenAI vector for every query and every intent name (< 1 cent) | same tables, embedding column |
| 3 | `train` | Averages each intent's train-query vectors into one point per intent (its *centroid*) | `intents` |
| 4 | `classify-all` | Ranks the top 5 intents for every stored query, accuracy per split | `query_intent_candidates` |
| 5 | `evaluate` | Scores the test split, centroid vs name: top-1, top-3, ambiguous | nothing, prints only |

**Classify a query.** `POST /queries/classify` and `classify` run the same steps:

```mermaid
flowchart TD
  q["Customer query<br/>(API or CLI)"] --> n["Normalize text"]
  n --> k{"Seen this text before?"}
  k -- "yes" --> s["Reuse stored vector"]
  k -- "no" --> g["Get vector from OpenAI"]
  s --> r["Rank the 77 intents by<br/>cosine similarity to their centroid"]
  g --> r
  r --> a{"Top two within 0.02<br/>of each other?"}
  a -- "yes" --> amb["Too close to call<br/>(ambiguous)"]
  a -- "no" --> conf["Clear winner"]
  amb --> log[("Log in classified_queries")]
  conf --> log
  log --> resp["Return top candidates"]
  log -. "label: human confirms intent" .-> lab["Added to train examples"]
  lab -. "train" .-> cen["Centroids recomputed"]
```

## Dataset

Banking77 by PolyAI, from
[PolyAI-LDN/task-specific-datasets](https://github.com/PolyAI-LDN/task-specific-datasets/tree/master/banking_data)
(`banking_data/train.csv`, `test.csv`, `categories.json`), licensed CC BY 4.0. The files are committed unmodified in
`data/banking77/` - see [its README](data/banking77/README.md) for row counts, citation and import quirks.

## Layout

Dependencies point inwards: Domain ← Application ← Infrastructure, with Api and Cli as composition roots.

| Project | Responsibility |
|---|---|
| `src/IntentClassifier.Domain` | Model and rules (`QueryText`, `IntentId`, `Intent`, `Embedding`, `ClassificationConfidence`), repository interfaces |
| `src/IntentClassifier.Application` | Use-case handlers (classify, embed, train, classify-all, evaluate) and ports |
| `src/IntentClassifier.Infrastructure` | Dapper + Npgsql + pgvector, FluentMigrator migrations, Banking77 COPY importer, OpenAI embedder |
| `src/IntentClassifier.Api` | Minimal API: `POST /queries/classify` |
| `src/IntentClassifier.Cli` | `migrate`, `seed`, `embed`, `train`, `classify-all`, `evaluate`, `classify`, `label` |
| `tests/IntentClassifier.UnitTests` | Domain rules and use cases with in-memory fakes |
| `tests/IntentClassifier.IntegrationTests` | Full pipeline against a Testcontainers pgvector Postgres with a fake embedder |

Similarity is `1 - (a <=> b)` (pgvector cosine distance); a centroid is `avg(embedding)` of an intent's train queries.
Migrations use timestamp versions (`[Migration(202610161000)]`) - do not renumber them.

## Run

Requires the .NET 10 SDK and Docker.

```sh
docker compose up -d --wait      # pgvector Postgres on localhost:5435
dotnet user-secrets --project src/IntentClassifier.Cli set "OpenAi:ApiKey" "<key>"   # shared with the API

CLI="dotnet run --project src/IntentClassifier.Cli --"
$CLI migrate      # schema (idempotent)
$CLI seed         # load data/banking77/{train,test}.csv
$CLI embed        # ~13k queries + 77 intent names, < 1 cent
$CLI train        # intent centroids
$CLI classify-all # top-5 intents per query, accuracy per split
$CLI evaluate     # centroid vs name accuracy on the test split
```

```sh
$CLI classify "why did my cash get charged a fee that should not be there."
```
```
Query:      why did my cash get charged a fee that should not be there.
Normalized: why did my cash get charged a fee that should not be there.
Embedding:  Stored, by Centroid, AMBIGUOUS
   1. 0.8156  Cash withdrawal charge (cash_withdrawal_charge)
   2. 0.8034  Card payment fee charged (card_payment_fee_charged)
   3. 0.7340  Transfer fee charged (transfer_fee_charged)
```

`--top N` sets how many ranks print, `--by name` uses the name representation. Known queries reuse their stored
embedding; new ones are embedded via OpenAI (`Generated`). Exit codes: `0` ok, `1` usage error, `2` runtime or
config error (one-line message on stderr).

### Query log and labels

Every `classify` call (CLI or API) is logged in `classified_queries`, so a repeated new query reuses its embedding.
Once a human confirms the intent, promote it into the train split and retrain:

```sh
$CLI label "the barista charged me twice for one iced latte" transaction_charged_twice
$CLI train
```

`label` refuses queries never classified, unknown intents, test-split queries (would leak test data) and original
train rows. Relabelling a promoted query overwrites it.

## API

```sh
dotnet run --project src/IntentClassifier.Api --launch-profile http   # http://localhost:5235

curl -s -X POST http://localhost:5235/queries/classify \
  -H "Content-Type: application/json" -d '{"query": "where is the virtual card?"}'
```
```json
{
  "query": "where is the virtual card?",
  "normalizedQuery": "where is the virtual card?",
  "embeddingSource": "stored",
  "ambiguous": false,
  "candidates": [
    { "rank": 1, "intentId": "getting_virtual_card", "intentName": "Getting virtual card", "similarity": 0.9143 },
    { "rank": 2, "intentId": "virtual_card_not_working", "intentName": "Virtual card not working", "similarity": 0.7809 }
  ]
}
```

An optional `"top"` (default 3) sets how many candidates come back.
Errors are `ProblemDetails` from one `IExceptionHandler`: empty query or `top` below 1 → 400, new query with no OpenAI key → 503.
OpenAPI at `/openapi/v1.json` (Development); samples in `src/IntentClassifier.Api/IntentClassifier.Api.http`.
`Database:AutoMigrate=true` applies migrations on startup.

## Settings

`ConnectionStrings:Postgres`, `OpenAi:ApiKey`, `OpenAi:Model`. Precedence: appsettings < user-secrets < environment
variables (`OpenAi__ApiKey`, `ConnectionStrings__Postgres`); `OPENAI_API_KEY` is a fallback. Api and Cli share one
`UserSecretsId`. Never commit a key.

## Tests and formatting

```sh
dotnet test IntentClassifier.slnx   # integration tests need Docker
dotnet tool restore && dotnet csharpier check .
```
