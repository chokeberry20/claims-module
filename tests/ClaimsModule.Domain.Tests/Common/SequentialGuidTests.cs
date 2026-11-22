using System.Data.SqlTypes;
using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Tests.Common;

public sealed class SequentialGuidTests
{
    [Fact]
    public void Generated_ids_sort_in_generation_order_under_sql_server_uniqueidentifier_comparison()
    {
        var generated = Enumerable.Range(0, 5_000).Select(_ => SequentialGuid.NewGuid()).ToList();

        // SqlGuid implements SQL Server's uniqueidentifier ordering.
        var sqlOrdered = generated.OrderBy(g => new SqlGuid(g)).ToList();

        sqlOrdered.ShouldBe(generated);
    }

    [Fact]
    public void Generated_ids_are_unique_across_threads()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        Parallel.For(0, 20_000, _ => ids.Add(SequentialGuid.NewGuid()));

        ids.Distinct().Count().ShouldBe(20_000);
    }

    [Fact]
    public void Generated_id_is_never_empty() => SequentialGuid.NewGuid().ShouldNotBe(Guid.Empty);
}
