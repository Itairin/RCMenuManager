using Xunit;

// WPF types are thread-affine and one test class creates the shared Application
// instance. Running classes in parallel makes that a race, and the whole suite
// only takes a couple of seconds, so determinism wins over parallelism.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
