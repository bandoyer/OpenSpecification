using OpenSpecification;

namespace GroupByApiPrototype.NegativeChecksA
{
    using GroupByApiPrototype.CandidateA;

#if NEG_A_ENTITY_WHERE_AFTER_GROUPBY
    public sealed class BadSpec : Specification<Store, CitySalesDto>
    {
        public BadSpec()
        {
            Query.GroupBy(s => s.City).Where(s => s.IsActive);
        }
    }
#endif

#if NEG_A_ENTITY_INCLUDE_AFTER_GROUPBY
    public sealed class BadSpec : Specification<Store, CitySalesDto>
    {
        public BadSpec()
        {
            Query.GroupBy(s => s.City).Include(s => s.Company);
        }
    }
#endif

#if NEG_A_THENBY_WITHOUT_ORDERBY
    public sealed class BadSpec : Specification<Store, CitySalesDto>
    {
        public BadSpec()
        {
            Query.GroupBy(s => s.City).ThenBy(g => g.Key);
        }
    }
#endif

#if NEG_A_THENBY_AFTER_HAVING
    public sealed class BadSpec : Specification<Store, CitySalesDto>
    {
        public BadSpec()
        {
            Query.GroupBy(s => s.City)
                .OrderBy(g => g.Key)
                .Having(g => g.Count() > 1)
                .ThenBy(g => g.Count());
        }
    }
#endif

#if NEG_A_GROUPBY_ON_ENTITY_SPEC
    public sealed class BadSpec : Specification<Store>
    {
        public BadSpec()
        {
            Query.GroupBy(s => s.City);
        }
    }
#endif

#if NEG_A_FORGOT_SELECT
    public sealed class MissingSelectSpec : Specification<Store, CitySalesDto>
    {
        public MissingSelectSpec()
        {
            // Compiles in Candidate A -> caught at runtime (SelectorNotFoundException)
            Query.GroupBy(s => s.City).Having(g => g.Count() >= 2);
        }
    }
#endif

#if NEG_A_SELECT_AGAIN_AFTER_SELECT
    public sealed class BadSpec : Specification<Store, CitySalesDto>
    {
        public BadSpec()
        {
            Query.GroupBy(s => s.City)
                .Select(g => new CitySalesDto { City = g.Key })
                .Select(s => new CitySalesDto { City = s.City });
        }
    }
#endif
}

namespace GroupByApiPrototype.NegativeChecksB
{
    using GroupByApiPrototype.CandidateB;

#if NEG_B_FORGOT_SELECT
    public sealed class BadSpec : Specification<Store, CitySalesDto>
    {
        public BadSpec()
        {
            // Compile-time error in Candidate B!
            Query.GroupBy(s => s.City, group => group.Having(g => g.Count() >= 2));
        }
    }
#endif

#if CANDIDATE_B_WITH_DIRECT_OVERLOAD
    // Tests whether having BOTH GroupBy(key, Func<IScopedGroupBuilder, IGroupedProjection>)
    // AND GroupBy(key, Expression<Func<IGrouping, TResult>>) compiles cleanly in isolation!
    public sealed class B_DirectOverloadTestSpec : Specification<Store, CitySalesDto>
    {
        public B_DirectOverloadTestSpec()
        {
            Query.GroupBy(
                s => s.City,
                g => new CitySalesDto { City = g.Key, StoreCount = g.Count() });
        }
    }

    public sealed class B_CallbackOverloadTestSpec : Specification<Store, CitySalesDto>
    {
        public B_CallbackOverloadTestSpec()
        {
            Query.GroupBy(
                s => s.City,
                group => group.Having(g => g.Count() >= 2).Select(g => new CitySalesDto { City = g.Key, StoreCount = g.Count() }));
        }
    }
#endif
}
