using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using OpenSpecification.EntityFrameworkCore;

namespace OpenSpecification.Spike.Engine;

internal enum ReadKind { Value, HasValue }

internal readonly record struct VariantKey(int Slot, ReadKind Kind);

internal sealed class Decision(VariantKey key, int choice, object?[] domain)
{
    public VariantKey Key { get; } = key;
    public int Choice { get; } = choice;
    public object?[] Domain { get; } = domain;
    public Decision Next() => new(Key, Choice + 1, Domain);
}

/// <summary>One registration: the markers handed to the factory, and the run in progress while it explores.</summary>
internal sealed class Registration
{
    public Registration(Type[] argTypes, string[] argNames)
    {
        ArgTypes = argTypes;
        ArgNames = argNames;
        Markers = argTypes.Select((t, i) => new Marker(this, i, t)).ToArray();
    }

    public Type[] ArgTypes { get; }
    public string[] ArgNames { get; }
    public Marker[] Markers { get; }
    internal ExplorationRun? CurrentRun { get; set; }
}

/// <summary>The value a <see cref="QueryArg{T}"/> holds while the library builds a compiled query.</summary>
internal sealed class Marker(Registration registration, int slot, Type argType)
{
    public Registration Registration { get; } = registration;
    public int Slot { get; } = slot;
    public Type ArgType { get; } = argType;
    public string Name => Registration.ArgNames[Slot];

    public T ReadValue<T>()
    {
        var run = Registration.CurrentRun ?? throw OutsideConstructor();
        if (!Variants.IsVariantType(typeof(T)))
        {
            throw new CompiledSpecificationNotSupportedException(
                $"The constructor reads argument '{Name}' ({TypeNames.Of(typeof(T))}). Only bool and enum arguments, or " +
                "HasValue on any argument, can choose a compiled query's shape. Check the value before calling the query, " +
                "or use the argument only inside a query expression.");
        }

        return (T)run.Choose(new VariantKey(Slot, ReadKind.Value), typeof(T))!;
    }

    public bool ReadHasValue<T>()
    {
        // A non-nullable value type always has a value, on the normal path too, so it chooses nothing.
        if (default(T) is not null) return true;

        var run = Registration.CurrentRun ?? throw OutsideConstructor();
        return (bool)run.Choose(new VariantKey(Slot, ReadKind.HasValue), typeof(bool))!;
    }

    public Exception Misuse(string member) => new CompiledSpecificationNotSupportedException(
        $"The constructor calls {member} on argument '{Name}'. A compiled specification can use an argument inside a " +
        "query expression, or read a bool, an enum or HasValue to choose the query's shape.");

    private InvalidOperationException OutsideConstructor() => new(
        $"Argument '{Name}' is a compiled query placeholder. It has no value outside the specification's constructor.");
}

internal static class Variants
{
    public static bool IsVariantType(Type type)
    {
        var core = Nullable.GetUnderlyingType(type) ?? type;
        return core == typeof(bool) || core.IsEnum;
    }

    public static object?[] Domain(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var core = underlying ?? type;
        var values = new List<object?>();
        if (underlying is not null) values.Add(null);

        if (core == typeof(bool))
        {
            values.Add(false);
            values.Add(true);
            return values.ToArray();
        }

        if (core.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            throw new CompiledSpecificationNotSupportedException(
                $"{core.Name} is a [Flags] enum, so its combinations can't be listed. Use it only inside a query expression.");
        }

        var seen = new HashSet<object>();
        foreach (var value in Enum.GetValues(core))
        {
            var number = Convert.ChangeType(value, Enum.GetUnderlyingType(core));
            if (seen.Add(number)) values.Add(value);
        }

        return values.ToArray();
    }
}

/// <summary>One run of the factory, with the variant values forced by earlier runs.</summary>
internal sealed class ExplorationRun(List<Decision> forced)
{
    private readonly Dictionary<VariantKey, object?> _values = new();
    private int _index;

    public List<Decision> Made { get; } = new();
    public bool ConsumedAllForced => _index >= forced.Count;

    public object? Choose(VariantKey key, Type type)
    {
        if (_values.TryGetValue(key, out var known)) return known;

        var domain = Variants.Domain(type);
        var choice = 0;
        if (_index < forced.Count)
        {
            var d = forced[_index];
            if (d.Key != key)
            {
                throw new CompiledSpecificationNotSupportedException(
                    "The constructor read its arguments in a different order on two runs with the same values. " +
                    "It has to be deterministic.");
            }

            choice = d.Choice;
        }

        _index++;
        Made.Add(new Decision(key, choice, domain));
        var value = domain[choice];
        _values[key] = value;
        return value;
    }
}

internal sealed record Leaf(IReadOnlyList<Decision> Decisions, object Specification);

internal static class Explorer
{
    public const int MaxShapes = 64;

    public static List<Leaf> Run(Registration registration, Func<object> factory)
    {
        var leaves = new List<Leaf>();
        var forced = new List<Decision>();

        while (true)
        {
            var run = new ExplorationRun(forced);
            object spec;
            registration.CurrentRun = run;
            try
            {
                spec = factory() ?? throw new CompiledSpecificationNotSupportedException("The factory returned null.");
            }
            catch (CompiledSpecificationNotSupportedException ex)
            {
                throw Name(ex);
            }
            finally
            {
                registration.CurrentRun = null;
            }

            if (!run.ConsumedAllForced)
            {
                throw new CompiledSpecificationNotSupportedException(
                    "The constructor read fewer arguments on a second run with the same values. It has to be deterministic.");
            }

            leaves.Add(new Leaf(run.Made.ToList(), spec));
            if (leaves.Count > MaxShapes)
            {
                throw new CompiledSpecificationNotSupportedException(
                    $"The bool, enum and HasValue reads in {spec.GetType().Name} make more than {MaxShapes} query shapes.");
            }

            var made = run.Made;
            var i = made.Count - 1;
            while (i >= 0 && made[i].Choice == made[i].Domain.Length - 1) i--;
            if (i < 0) return leaves;

            forced = made.Take(i).ToList();
            forced.Add(made[i].Next());
        }
    }

    // Prefixes the message with the specification whose constructor threw, found from the stack trace.
    private static CompiledSpecificationNotSupportedException Name(CompiledSpecificationNotSupportedException ex)
    {
        var specType = new StackTrace(ex).GetFrames()
            .Select(f => f.GetMethod())
            .OfType<ConstructorInfo>()
            .Select(c => c.DeclaringType)
            .FirstOrDefault(t => t is not null && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISpecification<>)));
        return specType is null ? ex : new CompiledSpecificationNotSupportedException($"{specType.Name}: {ex.Message}", ex);
    }
}

/// <summary>Builds the function that maps call arguments to a shape index.</summary>
internal static class Selector
{
    private sealed class Node(VariantKey key, object?[] domain)
    {
        public VariantKey Key { get; } = key;
        public object?[] Domain { get; } = domain;
        public SortedDictionary<int, object> Children { get; } = new(); // Node or int leaf index
    }

    public static LambdaExpression? Build(IReadOnlyList<Leaf> leaves, Registration registration)
    {
        if (leaves.Count == 1) return null;

        var root = new Node(leaves[0].Decisions[0].Key, leaves[0].Decisions[0].Domain);
        for (var leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
        {
            var path = leaves[leafIndex].Decisions;
            var node = root;
            for (var j = 0; j < path.Count; j++)
            {
                var d = path[j];
                Debug.Assert(node.Key == d.Key);
                if (j == path.Count - 1)
                {
                    node.Children[d.Choice] = leafIndex;
                }
                else
                {
                    if (!node.Children.TryGetValue(d.Choice, out var child))
                    {
                        child = new Node(path[j + 1].Key, path[j + 1].Domain);
                        node.Children[d.Choice] = child;
                    }

                    node = (Node)child;
                }
            }
        }

        var args = registration.ArgTypes.Select((t, i) => Expression.Parameter(t, registration.ArgNames[i])).ToArray();
        return Expression.Lambda(BuildNode(root, args, registration), args);
    }

    private static readonly MethodInfo _unknown = typeof(Selector).GetMethod(nameof(Unknown), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static int Unknown(string name, object? value)
        => throw new ArgumentOutOfRangeException(name, value, $"Argument '{name}' has a value no compiled shape covers.");

    private static Expression BuildNode(Node node, ParameterExpression[] args, Registration registration)
    {
        var arg = args[node.Key.Slot];
        Expression value = node.Key.Kind == ReadKind.HasValue
            ? Expression.NotEqual(arg, Expression.Constant(null, arg.Type))
            : arg;

        Expression result = Expression.Call(_unknown, Expression.Constant(registration.ArgNames[node.Key.Slot]), Expression.Convert(value, typeof(object)));
        foreach (var (choice, child) in node.Children.Reverse())
        {
            var test = Expression.Equal(Normalize(value), Normalize(Expression.Constant(node.Domain[choice], value.Type)));
            var then = child is Node n ? BuildNode(n, args, registration) : Expression.Constant((int)child);
            result = Expression.Condition(test, then, result);
        }

        return result;
    }

    // Expression.Equal has no enum operator, so enums compare as their underlying number.
    private static Expression Normalize(Expression e)
    {
        var underlying = Nullable.GetUnderlyingType(e.Type);
        var core = underlying ?? e.Type;
        if (!core.IsEnum) return e;
        var number = Enum.GetUnderlyingType(core);
        return Expression.Convert(e, underlying is null ? number : typeof(Nullable<>).MakeGenericType(number));
    }
}

internal static class TypeNames
{
    public static string Of(Type t)
    {
        var u = Nullable.GetUnderlyingType(t);
        if (u is not null) return Of(u) + "?";
        if (t == typeof(int)) return "int";
        if (t == typeof(string)) return "string";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(long)) return "long";
        if (t.IsArray) return Of(t.GetElementType()!) + "[]";
        if (t.IsGenericType) return $"{t.Name[..t.Name.IndexOf('`')]}<{string.Join(", ", t.GetGenericArguments().Select(Of))}>";
        return t.Name;
    }
}
