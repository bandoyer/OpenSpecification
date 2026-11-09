using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OpenSpecification.EntityFrameworkCore;

namespace OpenSpecification.Spike.Engine;

internal interface ICompiledCore
{
    int ShapeCount { get; }
    IReadOnlyList<string> ShapeDescriptions { get; }
    Delegate? Selector { get; }
    Delegate Build(int shape, Operation operation, DbContext context);
}

/// <summary>The shapes of one compiled specification, built once when it's created.</summary>
internal sealed class CompiledCore<T, TResult> : ICompiledCore where T : class
{
    private readonly Registration _registration;
    private readonly IReadOnlyList<Leaf> _leaves;
    private readonly bool _projected;
    private readonly string _specName;

    private CompiledCore(Registration registration, IReadOnlyList<Leaf> leaves, bool projected)
    {
        _registration = registration;
        _leaves = leaves;
        _projected = projected;
        _specName = leaves[0].Specification.GetType().Name;
        Selector = Engine.Selector.Build(leaves, registration)?.Compile();
        ShapeDescriptions = leaves.Select(Describe).ToArray();
    }

    public int ShapeCount => _leaves.Count;
    public IReadOnlyList<string> ShapeDescriptions { get; }
    public Delegate? Selector { get; }

    public static CompiledCore<T, TResult> Create(Delegate factory, Type[] argTypes, Func<Registration, object> invoke, bool projected)
    {
        if (factory.Target is { } target && target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0)
        {
            throw new CompiledSpecificationNotSupportedException(
                "The factory captures values. They would be frozen into the compiled query. Pass them as arguments instead.");
        }

        foreach (var type in argTypes)
        {
            if (!IsSupportedArgument(type))
            {
                throw new CompiledSpecificationNotSupportedException(
                    $"An argument of type {TypeNames.Of(type)} can't be a compiled query parameter. Use a scalar type, its nullable form, or an array or List of one.");
            }
        }

        var names = factory.Method.GetParameters().Select(p => p.Name ?? $"arg{p.Position + 1}").ToArray();
        var registration = new Registration(argTypes, names);
        var leaves = Explorer.Run(registration, () => invoke(registration));

        foreach (var leaf in leaves)
        {
            var spec = (ISpecification<T>)leaf.Specification;
            if (spec.PostProcessingAction is not null || (spec is ISpecification<T, TResult> p && p.PostProcessingAction is not null))
            {
                throw new CompiledSpecificationNotSupportedException($"{spec.GetType().Name}: a compiled specification can't have a PostProcessingAction.");
            }

            if (spec.Items.Count > 0)
            {
                throw new CompiledSpecificationNotSupportedException($"{spec.GetType().Name}: a compiled specification can't use Items.");
            }
        }

        return new CompiledCore<T, TResult>(registration, leaves, projected);
    }

    public Delegate Build(int shape, Operation operation, DbContext context)
    {
        var spec = _leaves[shape].Specification;
        var evaluator = SpecificationEvaluator.Default;
        var set = context.Set<T>();

        var query = operation == Operation.Count
            ? evaluator.GetQuery(set, (ISpecification<T>)spec, evaluateCriteriaOnly: true).Expression
            : _projected
                ? evaluator.GetQuery(set, (ISpecification<T, TResult>)spec).Expression
                : evaluator.GetQuery(set, (ISpecification<T>)spec).Expression;

        return QueryBuilder.Compile(_registration, _specName, query, operation, typeof(T), typeof(TResult));
    }

    private string Describe(Leaf leaf)
        => leaf.Decisions.Count == 0
            ? "single shape"
            : string.Join(", ", leaf.Decisions.Select(d =>
                $"{_registration.ArgNames[d.Key.Slot]}{(d.Key.Kind == ReadKind.HasValue ? ".HasValue" : "")}={d.Domain[d.Choice] ?? "null"}"));

    private static bool IsSupportedArgument(Type type)
    {
        if (type.IsArray) return type.GetArrayRank() == 1 && IsScalar(type.GetElementType()!);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return IsScalar(type.GetGenericArguments()[0]);
        return IsScalar(type);
    }

    private static bool IsScalar(Type type)
    {
        var core = Nullable.GetUnderlyingType(type) ?? type;
        return core.IsPrimitive || core.IsEnum || core == typeof(string) || core == typeof(decimal) || core == typeof(DateTime)
            || core == typeof(DateTimeOffset) || core == typeof(DateOnly) || core == typeof(TimeOnly) || core == typeof(TimeSpan)
            || core == typeof(Guid);
    }
}
