using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using OpenSpecification.EntityFrameworkCore;

namespace OpenSpecification.Spike.Engine;

internal enum Operation { List, First, Count }

internal static class QueryBuilder
{
    private static readonly MethodInfo _set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
    private static readonly MethodInfo _firstOrDefault = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.FirstOrDefault) && m.GetParameters().Length == 1);
    private static readonly MethodInfo _count = typeof(Queryable).GetMethods()
        .Single(m => m.Name == nameof(Queryable.Count) && m.GetParameters().Length == 1);

    /// <summary>
    /// Turns the evaluators' query for one shape into an EF compiled delegate whose parameters are the context and
    /// every argument, plus a cancellation token for single results.
    /// </summary>
    public static Delegate Compile(Registration registration, string specName, Expression query, Operation operation, Type entityType, Type resultType)
    {
        var context = Expression.Parameter(typeof(DbContext), "context");
        var args = registration.ArgTypes.Select((t, i) => Expression.Parameter(t, registration.ArgNames[i])).ToArray();

        var rewriter = new Rewriter(registration, specName, context, args);
        var body = rewriter.Visit(query)!;
        new Validator(specName, args).Visit(body);

        // EF gets only the arguments this shape's query uses. An argument that only chose the shape would cost a boxed
        // parameter value on every call.
        var used = args.Where(rewriter.Used.Contains).ToArray();
        var token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var scalar = operation != Operation.List;

        switch (operation)
        {
            case Operation.First:
                body = Expression.Call(_firstOrDefault.MakeGenericMethod(resultType), body);
                break;
            case Operation.Count:
                body = Expression.Call(_count.MakeGenericMethod(entityType), body);
                break;
        }

        var efParameters = new List<ParameterExpression> { context };
        efParameters.AddRange(used);
        if (scalar) efParameters.Add(token);

        var result = operation == Operation.Count ? typeof(int) : resultType;
        var efDelegate = CompileWithEf(Expression.Lambda(body, efParameters), used.Select(a => a.Type).ToArray(), result, scalar);
        if (used.Length == args.Length) return efDelegate;

        // An adapter with every argument, so all shapes of one handle share a delegate type.
        var adapterParameters = new List<ParameterExpression> { context };
        adapterParameters.AddRange(args);
        if (scalar) adapterParameters.Add(token);
        var invoke = Expression.Invoke(Expression.Constant(efDelegate), efParameters);
        return Expression.Lambda(invoke, adapterParameters).Compile();
    }

    private static Delegate CompileWithEf(LambdaExpression lambda, Type[] argTypes, Type result, bool scalar)
    {
        var method = typeof(EF).GetMethods()
            .Where(m => m.Name == nameof(EF.CompileAsyncQuery) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == argTypes.Length + 2)
            .SingleOrDefault(m =>
            {
                var func = m.GetParameters()[0].ParameterType.GetGenericArguments()[0];
                var funcArgs = func.GetGenericArguments();
                var last = funcArgs[^1];
                if (!scalar) return last.IsGenericType && last.GetGenericTypeDefinition() == typeof(IQueryable<>);
                return last.IsGenericParameter && funcArgs.Length >= 2 && funcArgs[^2] == typeof(CancellationToken);
            })
            ?? throw new CompiledSpecificationNotSupportedException(
                $"EF Core has no compiled query overload for {argTypes.Length} parameters{(scalar ? " with a cancellation token" : "")}.");

        var typeArgs = new List<Type> { typeof(DbContext) };
        typeArgs.AddRange(argTypes);
        typeArgs.Add(result);
        return (Delegate)method.MakeGenericMethod(typeArgs.ToArray()).Invoke(null, [lambda])!;
    }

    private static bool IsQueryArg(Type? t) => t is { IsGenericType: true } && t.GetGenericTypeDefinition() == typeof(QueryArg<>);

    /// <summary>Replaces the query root with context.Set&lt;T&gt;() and each argument marker with its parameter.</summary>
    private sealed class Rewriter(Registration registration, string specName, ParameterExpression context, ParameterExpression[] args) : ExpressionVisitor
    {
        public HashSet<ParameterExpression> Used { get; } = new();

        protected override Expression VisitExtension(Expression node)
        {
            if (node is EntityQueryRootExpression root)
            {
                if (root.EntityType.HasSharedClrType)
                {
                    throw new CompiledSpecificationNotSupportedException($"{specName}: shared-type entity {root.EntityType.Name} isn't supported.");
                }

                return Expression.Call(context, _set.MakeGenericMethod(root.ElementType));
            }

            return base.VisitExtension(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Convert && node.Method?.Name == "op_Implicit" && IsQueryArg(node.Operand.Type))
            {
                return MapLeaf(node.Operand, node.Type);
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (IsQueryArg(node.Expression?.Type))
            {
                if (node.Member.Name is nameof(QueryArg<int>.Value) or QueryArgs.ValueFieldName) return MapLeaf(node.Expression!, node.Type);
                throw new CompiledSpecificationNotSupportedException(
                    $"{specName}: {node.Member.Name} on an argument inside a query expression isn't supported. Use it in the constructor instead.");
            }

            return base.VisitMember(node);
        }

        private Expression MapLeaf(Expression receiver, Type type)
        {
            var boxed = ReadFieldChain(receiver);
            var marker = (boxed as IQueryArg)?.Marker;
            if (marker is null)
            {
                throw new CompiledSpecificationNotSupportedException(
                    $"{specName}: a QueryArg with a fixed value is captured ({receiver}). Its value would be frozen into the " +
                    "compiled query. Pass it as an argument of the compiled specification.");
            }

            if (!ReferenceEquals(marker.Registration, registration))
            {
                throw new CompiledSpecificationNotSupportedException($"{specName}: an argument from another compiled specification is captured.");
            }

            var parameter = args[marker.Slot];
            Used.Add(parameter);
            return parameter.Type == type ? parameter : Expression.Convert(parameter, type);
        }

        // Reads fields only, never property getters, from the captured closure or specification instance.
        private object? ReadFieldChain(Expression e) => e switch
        {
            ConstantExpression c => c.Value,
            MemberExpression { Member: FieldInfo f, Expression: { } inner } => f.GetValue(ReadFieldChain(inner)),
            _ => throw new CompiledSpecificationNotSupportedException(
                $"{specName}: an argument is reached through {e}. Only an argument captured directly by a query expression is supported."),
        };
    }

    /// <summary>Refuses anything left in the tree whose value could differ between calls.</summary>
    private sealed class Validator(string specName, ParameterExpression[] args) : ExpressionVisitor
    {
        private string _operator = "query";
        private LambdaExpression? _lambda;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var isOperator = node.Method.DeclaringType == typeof(Queryable) || node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions);
            if (!isOperator && node.Object is not null && IsCaptured(node.Object))
            {
                Refuse($"a method call on a captured value ({node.Object})");
            }

            if (!isOperator) return base.VisitMethodCall(node);

            // The source comes first in the chain, so visit it before naming this operator.
            Visit(node.Arguments[0]);
            var previous = _operator;
            _operator = node.Method.Name;
            foreach (var argument in node.Arguments.Skip(1)) Visit(argument);
            _operator = previous;
            return node;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var previous = _lambda;
            _lambda = node;
            var result = base.VisitLambda(node);
            _lambda = previous;
            return result;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is null || IsLiteral(node.Type) || IsFixedSearchTerm(node.Type)) return node;
            Refuse($"a captured {node.Type.Name}");
            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is null)
            {
                if (node.Member.DeclaringType == typeof(EF) && node.Member.Name == nameof(EF.Functions)) return node;
                if (node.Member is FieldInfo { IsInitOnly: true } f && IsLiteral(f.FieldType)) return node;
                Refuse($"static member {node.Member.DeclaringType?.Name}.{node.Member.Name}");
                return node;
            }

            if (IsCaptured(node))
            {
                if (IsFixedSearchTerm(node.Expression.Type)) return node;
                Refuse($"captured value '{node.Member.Name}'");
                return node;
            }

            return base.VisitMember(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (IsArithmetic(node.NodeType) && (IsArg(node.Left) || IsArg(node.Right)))
            {
                Refuse($"arithmetic on an argument ({node}). Compute the value before the call");
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked && IsArg(node.Operand))
            {
                Refuse($"arithmetic on an argument ({node}). Compute the value before the call");
            }

            return base.VisitUnary(node);
        }

        private bool IsArg(Expression e)
        {
            while (e is UnaryExpression { NodeType: ExpressionType.Convert } u) e = u.Operand;
            return e is ParameterExpression p && args.Contains(p);
        }

        private static bool IsArithmetic(ExpressionType t) => t is ExpressionType.Add or ExpressionType.AddChecked
            or ExpressionType.Subtract or ExpressionType.SubtractChecked or ExpressionType.Multiply
            or ExpressionType.MultiplyChecked or ExpressionType.Divide or ExpressionType.Modulo or ExpressionType.Power;

        // A member chain that ends in a constant: a closure field, a field of the specification, and so on.
        private static bool IsCaptured(Expression e) => e switch
        {
            ConstantExpression => true,
            MemberExpression { Expression: { } inner } => IsCaptured(inner),
            _ => false,
        };

        private static bool IsLiteral(Type t)
        {
            var core = Nullable.GetUnderlyingType(t) ?? t;
            return core.IsPrimitive || core.IsEnum || core == typeof(string) || core == typeof(decimal) || core == typeof(DateTime)
                || core == typeof(DateTimeOffset) || core == typeof(DateOnly) || core == typeof(TimeOnly) || core == typeof(TimeSpan)
                || core == typeof(Guid);
        }

        // SearchExtension holds a fixed search term in a private record so EF parameterizes it.
        private static bool IsFixedSearchTerm(Type t) => t.Name == "StringVar" && t.DeclaringType?.Name == "SearchExtension";

        private void Refuse(string what)
        {
            var where = _lambda is null ? "" : $" in {_lambda}";
            throw new CompiledSpecificationNotSupportedException($"{specName}, {_operator}: {what}{where}.");
        }
    }
}
