using Xunit;

// Los tests comparten el override DOITNOW_DB_PATH, así que se ejecutan en serie.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
