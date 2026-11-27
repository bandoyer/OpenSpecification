// Negative compile-time checks for #99's isolated grouped builder stage. Every marked line must fail to compile:
// - GROUPBY_ON_ENTITY: Query.GroupBy(...) is unavailable on ISpecificationBuilder<T>.
// - WHERE_AFTER_GROUPBY / INCLUDE_AFTER_GROUPBY / ORDERBY_ENTITY_AFTER_GROUPBY / SELECT_ENTITY_AFTER_GROUPBY:
//   entity-stage clauses are unreachable on IGroupedSpecificationBuilder<T, TKey, TResult>.
// - SECOND_SELECT_AFTER_RESULT: a second Select(...) is unreachable on IGroupedResultSpecificationBuilder<T, TResult>.
using OpenSpecification;

static class GroupByCompileChecks
{
    sealed class Item
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public Item? Parent { get; set; }
    }

    static void InvalidCalls()
    {
        var entitySpec = new Specification<Item>();
        _ = entitySpec.Query.GroupBy(x => x.Category); // GROUPBY_ON_ENTITY

        var groupedSpec = new Specification<Item, string>();
        var grouped = groupedSpec.Query.GroupBy(x => x.Category);
        _ = grouped.Where(x => x.Name == "A"); // WHERE_AFTER_GROUPBY
        _ = grouped.Include(x => x.Parent); // INCLUDE_AFTER_GROUPBY
        _ = grouped.OrderBy(x => x.Name); // ORDERBY_ENTITY_AFTER_GROUPBY
        _ = grouped.Select(x => x.Name); // SELECT_ENTITY_AFTER_GROUPBY

        var result = grouped.Select(g => g.Key);
        _ = result.Select(g => g.Key); // SECOND_SELECT_AFTER_RESULT
    }
}
