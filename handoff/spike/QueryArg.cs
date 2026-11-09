using System.Linq.Expressions;
using OpenSpecification.Spike.Engine;

namespace OpenSpecification;

/// <summary>
/// An argument of a specification. On the normal path it holds a value. While the library builds a compiled query,
/// it holds a marker: using it inside a query expression makes it a SQL parameter, and reading a bool, an enum or
/// <see cref="HasValue"/> in the constructor chooses the query's shape.
/// </summary>
public readonly struct QueryArg<T> : IQueryArg
{
    private readonly T _value;
    private readonly Marker? _marker;

    public QueryArg(T value)
    {
        _value = value;
        _marker = null;
    }

    internal QueryArg(Marker marker)
    {
        _value = default!;
        _marker = marker;
    }

    Marker? IQueryArg.Marker => _marker;

    public T Value => _marker is null ? _value : _marker.ReadValue<T>();

    public bool HasValue => _marker is null ? _value is not null : _marker.ReadHasValue<T>();

    public static implicit operator QueryArg<T>(T value) => new(value);

    public static implicit operator T(QueryArg<T> arg) => arg.Value;

    public override bool Equals(object? obj)
        => _marker is null
            ? obj is QueryArg<T> other && ((IQueryArg)other).Marker is null && EqualityComparer<T>.Default.Equals(_value, other._value)
            : throw _marker.Misuse(nameof(Equals));

    public override int GetHashCode()
        => _marker is null ? (_value is null ? 0 : _value.GetHashCode()) : throw _marker.Misuse(nameof(GetHashCode));

    public override string ToString()
        => _marker is null ? _value?.ToString() ?? "" : throw _marker.Misuse(nameof(ToString));
}

internal interface IQueryArg
{
    Marker? Marker { get; }
}

/// <summary>
/// Simulates what the core builder would do when a spec stores a lambda: rewrite each QueryArg conversion to a read of
/// its value field, a plain member chain that EF reads without compiling anything. The compiled path's rewriter
/// recognizes the field too.
/// </summary>
public static class QueryArgs
{
    internal const string ValueFieldName = "_value";

    public static Expression<Func<T, bool>> Strip<T>(Expression<Func<T, bool>> predicate)
        => (Expression<Func<T, bool>>)StripVisitor.Instance.Visit(predicate);

    /// <summary>What the core builder would run on every lambda: a scan, and a rewrite only when a QueryArg is found.</summary>
    public static Expression<Func<T, bool>> StripIfNeeded<T>(Expression<Func<T, bool>> predicate)
        => Detector.Contains(predicate) ? Strip(predicate) : predicate;

    private sealed class Detector : System.Linq.Expressions.ExpressionVisitor
    {
        [ThreadStatic] private static Detector? _instance;
        private bool _found;

        public static bool Contains(System.Linq.Expressions.Expression expression)
        {
            var detector = _instance ??= new Detector();
            detector._found = false;
            detector.Visit(expression);
            return detector._found;
        }

        public override System.Linq.Expressions.Expression? Visit(System.Linq.Expressions.Expression? node)
            => _found || node is null ? node : base.Visit(node);

        protected override System.Linq.Expressions.Expression VisitUnary(System.Linq.Expressions.UnaryExpression node)
        {
            var type = node.Operand.Type;
            if (node.Method?.Name == "op_Implicit" && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(QueryArg<>))
            {
                _found = true;
                return node;
            }

            return base.VisitUnary(node);
        }
    }

    private sealed class StripVisitor : System.Linq.Expressions.ExpressionVisitor
    {
        public static readonly StripVisitor Instance = new();

        protected override System.Linq.Expressions.Expression VisitUnary(System.Linq.Expressions.UnaryExpression node)
        {
            var type = node.Operand.Type;
            if (node.NodeType == System.Linq.Expressions.ExpressionType.Convert && node.Method?.Name == "op_Implicit"
                && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(QueryArg<>))
            {
                var field = type.GetField(ValueFieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var read = System.Linq.Expressions.Expression.Field(node.Operand, field);
                return read.Type == node.Type ? read : System.Linq.Expressions.Expression.Convert(read, node.Type);
            }

            return base.VisitUnary(node);
        }
    }
}
