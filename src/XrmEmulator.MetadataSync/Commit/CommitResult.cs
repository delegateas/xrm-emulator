namespace XrmEmulator.MetadataSync.Commit;

public record CommitResult(
    List<CommitItem> Committed,
    CommitItem? FailedItem,
    Exception? FailedException);
