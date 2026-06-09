using Polyglot.Infrastructure.Services;

namespace Polyglot.Tests;

public class PostgresBackupServiceTests
{
    [Test]
    public async Task SelectKeysToPrune_KeepsNewestRetentionCount()
    {
        var keys = new[]
        {
            "postgres/polyglot-20260601-030000.dump",
            "postgres/polyglot-20260603-030000.dump",
            "postgres/polyglot-20260602-030000.dump",
            "postgres/polyglot-20260604-030000.dump",
        };

        var pruned = PostgresBackupService.SelectKeysToPrune(keys, retentionCount: 2);

        await Assert.That(pruned.Count).IsEqualTo(2);
        await Assert.That(pruned).Contains("postgres/polyglot-20260601-030000.dump");
        await Assert.That(pruned).Contains("postgres/polyglot-20260602-030000.dump");
    }

    [Test]
    public async Task SelectKeysToPrune_FewerThanRetention_PrunesNothing()
    {
        var keys = new[] { "postgres/polyglot-20260601-030000.dump" };

        var pruned = PostgresBackupService.SelectKeysToPrune(keys, retentionCount: 14);

        await Assert.That(pruned.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SelectKeysToPrune_ZeroRetention_PrunesEverything()
    {
        var keys = new[]
        {
            "postgres/polyglot-20260601-030000.dump",
            "postgres/polyglot-20260602-030000.dump",
        };

        var pruned = PostgresBackupService.SelectKeysToPrune(keys, retentionCount: 0);

        await Assert.That(pruned.Count).IsEqualTo(2);
    }
}
