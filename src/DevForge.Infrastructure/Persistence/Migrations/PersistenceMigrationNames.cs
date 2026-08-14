namespace DevForge.Infrastructure.Persistence.Migrations;

public static class PersistenceMigrationNames
{
    public const string HistoryTable = "SchemaMigrations";

    public const string InitialSchema = "20260810032526_InitialPersistenceSchema";

    public const string RetentionAndLookupIndexes = "20260810032719_AddRetentionAndLookupIndexes";

    public const string ExecutionCheckpoints = "20260811051654_AddExecutionCheckpoints";

    public const string RunPlanPreview = "20260813104744_PersistRunPlanPreview";
}
