using System.Collections.Concurrent;
using Bogus;

namespace Sava.Tests.Shared;

// Random test data that can be replayed. Every run picks a seed (or reads SAVA_TEST_SEED), and each
// test gets its own Faker seeded from that run seed plus the test's name. A test's data therefore
// doesn't depend on which other tests ran first or in parallel, and rerunning with the same seed
// regenerates exactly the same values.
internal static class TestData
{
    public const string SeedVariable = "SAVA_TEST_SEED";

    public static readonly int RunSeed = int.TryParse(
        Environment.GetEnvironmentVariable(SeedVariable),
        out var seed
    )
        ? seed
        : Random.Shared.Next();

    private static readonly ConcurrentDictionary<string, Faker> Fakers = new();

    // The Faker for the current test, or for the test class while a class fixture is being built.
    public static Faker Faker
    {
        get
        {
            var context = TestContext.Current;
            var key =
                context.Test?.TestDisplayName ?? context.TestClass?.TestClassName ?? "<no test>";
            return Fakers.GetOrAdd(key, Create);
        }
    }

    // A whole-second, Kind=Unspecified date, so it round-trips through MySQL and JSON unchanged.
    public static DateTime Date(int fromYear, int toYear)
    {
        var date = Faker.Date.Between(
            new DateTime(fromYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(toYear, 12, 31, 0, 0, 0, DateTimeKind.Unspecified)
        );
        return new DateTime(
            date.Year,
            date.Month,
            date.Day,
            date.Hour,
            date.Minute,
            date.Second,
            DateTimeKind.Unspecified
        );
    }

    private static Faker Create(string key)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Test data seed: {RunSeed}. Set {SeedVariable}={RunSeed} to replay this run."
        );
        return new Faker { Random = new Randomizer(unchecked(RunSeed * 31 + StableHash(key))) };
    }

    // string.GetHashCode() is randomized per process, so use FNV-1a to get the same value every run.
    private static int StableHash(string value)
    {
        var hash = 2166136261;
        foreach (var c in value)
        {
            hash = unchecked((hash ^ c) * 16777619);
        }
        return unchecked((int)hash);
    }
}
