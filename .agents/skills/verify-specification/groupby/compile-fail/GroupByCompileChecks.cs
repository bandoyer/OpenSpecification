// Negative compile-time checks for #99 and #100's isolated grouped builder stages. Every marked line must fail to compile:
// - GROUPBY_ON_ENTITY: Query.GroupBy(...) is unavailable on ISpecificationBuilder<T>.
// - WHERE_AFTER_GROUPBY / INCLUDE_AFTER_GROUPBY / ORDERBY_ENTITY_AFTER_GROUPBY / SELECT_ENTITY_AFTER_GROUPBY:
//   entity-stage clauses are unreachable on IGroupedSpecificationBuilder<T, TKey, TResult>.
// - THENBY_WITHOUT_ORDERBY / THENBYDESC_WITHOUT_ORDERBY:
//   ThenBy/ThenByDescending are unreachable on IGroupedSpecificationBuilder without a preceding OrderBy/OrderByDescending.
// - THENBY_AFTER_HAVING / THENBYDESC_AFTER_HAVING:
//   Having returns IGroupedSpecificationBuilder, making ThenBy/ThenByDescending unreachable after Having.
// - WHERE_AFTER_ORDERED_GROUPBY / INCLUDE_AFTER_ORDERED_GROUPBY / SELECT_ENTITY_AFTER_ORDERED_GROUPBY:
//   entity-stage clauses remain unreachable on IOrderedGroupedSpecificationBuilder<T, TKey, TResult>.
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
        _ = grouped.ThenBy(g => g.Key); // THENBY_WITHOUT_ORDERBY
        _ = grouped.ThenByDescending(g => g.Key); // THENBYDESC_WITHOUT_ORDERBY

        var ordered = grouped.OrderBy(g => g.Key);
        _ = ordered.Where(x => x.Name == "A"); // WHERE_AFTER_ORDERED_GROUPBY
        _ = ordered.Include(x => x.Parent); // INCLUDE_AFTER_ORDERED_GROUPBY
        _ = ordered.Select(x => x.Name); // SELECT_ENTITY_AFTER_ORDERED_GROUPBY

        var afterHaving = ordered.Having(g => g.Count() > 0);
        _ = afterHaving.ThenBy(g => g.Count()); // THENBY_AFTER_HAVING
        _ = afterHaving.ThenByDescending(g => g.Count()); // THENBYDESC_AFTER_HAVING

        var result = grouped.Select(g => g.Key);
        _ = result.Select(g => g.Key); // SECOND_SELECT_AFTER_RESULT
    }
}
