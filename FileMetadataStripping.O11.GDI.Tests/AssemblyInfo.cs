using Xunit;

// Serial execution: this assembly forces GDI+ mode via a static ctor on the
// local `FileMetadataStripping` adapter type. The existing
// FileMetadataStripping.O11.Tests assembly leaves _magickBroken=0. Since both
// projects share the same CssFileMetadataStripping AppDomain state per-process
// during a `dotnet test` run of both projects, keeping tests serial inside this
// assembly prevents flaps if a future test resets the flag mid-run.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
