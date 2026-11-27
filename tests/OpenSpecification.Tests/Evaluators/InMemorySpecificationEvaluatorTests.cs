namespace Tests.Evaluators;

public class InMemorySpecificationEvaluatorTests
{
    private static readonly InMemorySpecificationEvaluator _evaluator = InMemorySpecificationEvaluator.Default;

    public record Customer(int Id, string FirstName, string LastName);
    public record CustomerWithMails(int Id, string FirstName, string LastName, List<string> Emails);

    [Fact]
    public void Evaluate_ThrowsSelectorNotFoundException_GivenNoSelector()
    {
        var spec = new Specification<Customer, string>();

        var sut = () => _evaluator.Evaluate([], spec);

        sut.Should().Throw<SelectorNotFoundException>();
    }

    [Fact]
    public void Evaluate_ThrowsConcurrentSelectorsException_GivenSelectAndSelectMany()
    {
        var spec = new Specification<CustomerWithMails, string>();
        spec.Query.Select(x => x.FirstName);
        spec.Query.SelectMany(x => x.Emails);

        var sut = () => _evaluator.Evaluate([], spec);

        sut.Should().Throw<ConcurrentSelectorsException>();
    }

    [Fact]
    public void Evaluate_Filters_GivenSpec()
    {
        List<Customer> input =
        [
            new(1, "axxa", "axya"),
            new(2, "aaaa", "axya"),
            new(3, "aaaa", "axya"),
            new(4, "aaaa", "axya")
        ];

        List<Customer> expected =
        [
            new(3, "aaaa", "axya")
        ];

        var spec = new Specification<Customer>();
        spec.Query
            .Where(x => x.Id > 1)
            .Search(x => x.LastName, "%xy%")
            .OrderBy(x => x.Id)
            .Skip(1)
            .Take(1);

        var actual = _evaluator.Evaluate(input, spec).ToList();
        var actualFromSpec = spec.Evaluate(input).ToList();

        actual.Should().Equal(actualFromSpec);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Evaluate_Filters_GivenSpecWithSelect()
    {
        List<Customer> input =
        [
            new(1, "axxa", "axya"),
            new(2, "aaaa", "axya"),
            new(3, "vvvv", "axya"),
            new(4, "aaaa", "axya")
        ];

        List<string> expected = ["vvvv"];

        var spec = new Specification<Customer, string>();
        spec.Query
            .Where(x => x.Id > 1)
            .Search(x => x.LastName, "%xy%")
            .OrderBy(x => x.Id)
            .Skip(1)
            .Take(1)
            .Select(x => x.FirstName);

        var actual = _evaluator.Evaluate(input, spec).ToList();
        var actualFromSpec = spec.Evaluate(input).ToList();

        actual.Should().Equal(actualFromSpec);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Evaluate_Filters_GivenSpecWithSelectMany()
    {
        List<CustomerWithMails> input =
        [
            new(1, "axxa", "axya", []),
            new(2, "aaaa", "axya", []),
            new(3, "aaaa", "axya", ["zzz", "www"]),
            new(4, "aaaa", "axya", ["yyy"])
        ];

        // TODO: This is a known flaw. Pagination should be applied after SelectMany [Fati, 11/03/2025]
        // It's a major breaking change, so it will be postponed to v10.
        List<string> expected = ["zzz", "www", "yyy"];

        var spec = new Specification<CustomerWithMails, string>();
        spec.Query
            .Where(x => x.Id > 1)
            .Search(x => x.LastName, "%xy%")
            .OrderBy(x => x.Id)
            .Skip(1)
            .Take(2)
            .SelectMany(x => x.Emails);

        var actual = _evaluator.Evaluate(input, spec).ToList();
        var actualFromSpec = spec.Evaluate(input).ToList();

        actual.Should().Equal(actualFromSpec);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Evaluate_Filters_GivenSpecAndPostProcessingAction()
    {
        List<Customer> input =
        [
            new(1, "axxa", "axya"),
            new(2, "aaaa", "axya"),
            new(3, "aaaa", "axya"),
            new(4, "aaaa", "axya")
        ];

        List<Customer> expected =
        [
            new(3, "aaaa", "axya")
        ];

        var spec = new Specification<Customer>();
        spec.Query
            .Where(x => x.Id > 1)
            .PostProcessingAction(x => x.Where(c => c.Id == 3));

        var actual = _evaluator.Evaluate(input, spec).ToList();
        var actualFromSpec = spec.Evaluate(input).ToList();

        actual.Should().Equal(actualFromSpec);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Evaluate_Filters_GivenSpecWithSelectAndPostProcessingAction()
    {
        List<Customer> input =
        [
            new(1, "axxa", "axya"),
            new(2, "aaaa", "axya"),
            new(3, "vvvv", "axya"),
            new(4, "aaaa", "axya")
        ];

        List<string> expected = ["vvvv"];

        var spec = new Specification<Customer, string>();
        spec.Query
            .Where(x => x.Id > 1)
            .PostProcessingAction(x => x.Where(c => c == "vvvv"))
            .Select(x => x.FirstName);

        var actual = _evaluator.Evaluate(input, spec).ToList();
        var actualFromSpec = spec.Evaluate(input).ToList();

        actual.Should().Equal(actualFromSpec);
        actual.Should().Equal(expected);
    }

    [Fact]
    public void Evaluate_DoesNotFilter_GivenEmptySpec()
    {
        List<Customer> input =
        [
            new(1, "axxa", "axya"),
            new(2, "aaaa", "axya"),
            new(3, "aaaa", "axya"),
            new(4, "aaaa", "axya")
        ];

        List<Customer> expected =
        [
            new(1, "axxa", "axya"),
            new(2, "aaaa", "axya"),
            new(3, "aaaa", "axya"),
            new(4, "aaaa", "axya")
        ];

        var spec = new Specification<Customer>();

        var actual = _evaluator.Evaluate(input, spec).ToList();
        var actualFromSpec = spec.Evaluate(input).ToList();

        actual.Should().Equal(actualFromSpec);
        actual.Should().Equal(expected);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void Constructor_SetsProvidedEvaluators()
    {
        var evaluators = new List<IInMemoryEvaluator>
        {
            WhereEvaluator.Instance,
            OrderEvaluator.Instance,
            WhereEvaluator.Instance,
        };

        var evaluator = new InMemorySpecificationEvaluator(evaluators);

        var result = EvaluatorsOf(evaluator);
        result.Should().HaveSameCount(evaluators);
        result.Should().Equal(evaluators);
    }

    [Fact]
    public void DerivedSpecificationEvaluatorCanAlterDefaultEvaluator()
    {
        var evaluator = new SpecificationEvaluatorDerived();

        var result = EvaluatorsOf(evaluator);
        result.Should().HaveCount(7);
        result[0].Should().BeOfType<SearchMemoryEvaluator>();
        result[1].Should().BeOfType<WhereEvaluator>();
        result[2].Should().BeOfType<SearchMemoryEvaluator>();
        result[3].Should().BeOfType<DistinctEvaluator>();
        result[4].Should().BeOfType<OrderEvaluator>();
        result[5].Should().BeOfType<PaginationEvaluator>();
        result[6].Should().BeOfType<WhereEvaluator>();
    }

    [Fact]
    public void Evaluate_DeduplicatesEntityBeforeOrderAndPaging_GivenEntityDistinctSpec()
    {
        List<Customer> input =
        [
            new(3, "c", "xy"),
            new(1, "a", "xy"),
            new(2, "b", "xy"),
            new(1, "a", "xy"),
            new(3, "c", "xy"),
            new(4, "d", "zz"),
        ];

        var spec = new Specification<Customer>();
        spec.Query
            .Where(x => x.Id > 0)
            .Search(x => x.LastName, "%xy%")
            .Distinct()
            .OrderBy(x => x.Id)
            .Skip(1)
            .Take(2);

        var actual = spec.Evaluate(input).ToList();

        actual.Should().Equal([new Customer(2, "b", "xy"), new Customer(3, "c", "xy")]);
    }

    [Fact]
    public void Evaluate_DeduplicatesValueTypes_GivenEntityDistinctSpec()
    {
        var spec = new Specification<int>();
        spec.Query
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x);

        var actual = spec.Evaluate([3, 1, 2, 1, 3, -5]).ToList();

        actual.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public void Evaluate_DeduplicatesAfterSelectAndBeforeSkipTakeAndPostProcessing()
    {
        List<Customer> input =
        [
            new(1, "dup", "xy"),
            new(2, "dup", "xy"),
            new(3, "mid", "xy"),
            new(4, "end", "xy"),
            new(5, "end", "xy"),
        ];

        var spec = new Specification<Customer, string>();
        spec.Query
            .Distinct()
            .Skip(1)
            .Take(2)
            .PostProcessingAction(items => items.Select(x => x.ToUpperInvariant()))
            .Select(x => x.FirstName);

        var actual = spec.Evaluate(input).ToList();

        actual.Should().Equal(["MID", "END"]);
    }

    [Fact]
    public void Evaluate_DeduplicatesAfterSelectManyAndBeforeSkipTakeAndPostProcessing()
    {
        List<CustomerWithMails> input =
        [
            new(1, "a", "xy", ["m1", "m2"]),
            new(2, "b", "xy", ["m2", "m3"]),
            new(3, "c", "xy", ["m3", "m4"]),
        ];

        var spec = new Specification<CustomerWithMails, string>();
        spec.Query
            .Distinct()
            .Skip(1)
            .Take(2)
            .PostProcessingAction(items => items.Select(x => $"*{x}"))
            .SelectMany(x => x.Emails);

        var actual = spec.Evaluate(input).ToList();

        actual.Should().Equal(["*m2", "*m3"]);
    }

    [Fact]
    public void Evaluate_ThrowsNotSupportedException_GivenOrderByWithDistinctSelectProjection()
    {
        var spec = new Specification<Customer, string>();
        spec.Query
            .OrderBy(x => x.Id)
            .Distinct()
            .Select(x => x.FirstName);

        var sut = () => _evaluator.Evaluate([], spec).ToList();

        sut.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Evaluate_ThrowsNotSupportedException_GivenOrderByWithDistinctSelectManyProjection()
    {
        var spec = new Specification<CustomerWithMails, string>();
        spec.Query
            .OrderByDescending(x => x.Id)
            .Distinct()
            .SelectMany(x => x.Emails);

        var sut = () => _evaluator.Evaluate([], spec).ToList();

        sut.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void IsSatisfiedBy_RemainsEntityCriteriaOnly_GivenDistinctSpec()
    {
        var spec = new Specification<Customer, string>();
        spec.Query.Where(x => x.Id > 1).Distinct().Select(x => x.FirstName);

        spec.IsSatisfiedBy(new Customer(2, "a", "b")).Should().BeTrue();
        spec.IsSatisfiedBy(new Customer(1, "a", "b")).Should().BeFalse();
    }

    private class SpecificationEvaluatorDerived : InMemorySpecificationEvaluator
    {
        public SpecificationEvaluatorDerived()
        {
            Evaluators.Add(WhereEvaluator.Instance);
            Evaluators.Insert(0, SearchMemoryEvaluator.Instance);
        }
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "<Evaluators>k__BackingField")]
    public static extern ref List<IInMemoryEvaluator> EvaluatorsOf(InMemorySpecificationEvaluator @this);
#endif
}
