using System.Runtime.CompilerServices;

// WP-076 B9 (Y4 share, C-00 30.09): test-only internals (EnemyFactory.ResetSeedForTests) for the two test assemblies.
[assembly: InternalsVisibleTo("DD.Prototype.Tests")]
[assembly: InternalsVisibleTo("DD.Prototype.Tests.EditMode")]
