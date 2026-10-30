namespace IntentClassifier.Infrastructure.Persistence.Migrations;

using FluentMigrator;

// Renames the matching vocabulary to classification. The view stores the table by OID, so it
// keeps working across the table rename and only needs its own name changed.
[Migration(202610301000, "Rename query_intent_matches and query_match_report")]
public sealed class RenameMatchesToCandidates : Migration
{
    public override void Up() =>
        Execute.Sql(
            """
            ALTER TABLE query_intent_matches RENAME TO query_intent_candidates;
            ALTER TABLE query_intent_candidates RENAME CONSTRAINT pk_query_intent_matches TO pk_query_intent_candidates;
            ALTER TABLE query_intent_candidates RENAME CONSTRAINT fk_query_intent_matches_queries TO fk_query_intent_candidates_queries;
            ALTER TABLE query_intent_candidates RENAME CONSTRAINT fk_query_intent_matches_intents TO fk_query_intent_candidates_intents;
            ALTER INDEX ix_query_intent_matches_best_by_intent RENAME TO ix_query_intent_candidates_best_by_intent;
            ALTER VIEW query_match_report RENAME TO query_classification_report;
            """
        );

    public override void Down() =>
        Execute.Sql(
            """
            ALTER VIEW query_classification_report RENAME TO query_match_report;
            ALTER INDEX ix_query_intent_candidates_best_by_intent RENAME TO ix_query_intent_matches_best_by_intent;
            ALTER TABLE query_intent_candidates RENAME CONSTRAINT fk_query_intent_candidates_intents TO fk_query_intent_matches_intents;
            ALTER TABLE query_intent_candidates RENAME CONSTRAINT fk_query_intent_candidates_queries TO fk_query_intent_matches_queries;
            ALTER TABLE query_intent_candidates RENAME CONSTRAINT pk_query_intent_candidates TO pk_query_intent_matches;
            ALTER TABLE query_intent_candidates RENAME TO query_intent_matches;
            """
        );
}
