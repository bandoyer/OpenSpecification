using System.Data.Entity;

namespace OpenSpecification.EntityFramework6;

public class IncludeEvaluator : IEvaluator
{
    private IncludeEvaluator() { }
    public static IncludeEvaluator Instance { get; } = new IncludeEvaluator();

    public bool IsCriteriaEvaluator => false;

    /// <inheritdoc/>
    public IQueryable<T> GetQuery<T>(IQueryable<T> query, ISpecification<T> specification) where T : class
    {
        if (specification is Specification<T> spec)
        {
            if (spec.OneOrManyIncludeExpressions.IsEmpty) return query;
            if (spec.OneOrManyIncludeExpressions.SingleOrDefault is { Type: IncludeTypeEnum.Include } includeExpression)
            {
                return query.Include(GetPath(includeExpression.LambdaExpression));
            }
        }

        // EF6 has no ThenInclude, so each chain becomes one dotted path: an Include starts it, each ThenInclude extends
        // it, and the next Include, or the end, applies it. EF6 includes every navigation along a path.
        string? path = null;
        foreach (var includeExpression in specification.IncludeExpressions)
        {
            if (includeExpression.Type == IncludeTypeEnum.Include)
            {
                if (path is not null) query = query.Include(path);
                path = GetPath(includeExpression.LambdaExpression);
            }
            else if (includeExpression.Type == IncludeTypeEnum.ThenInclude)
            {
                path = path is null
                    ? throw new ArgumentException($"The include expression '{includeExpression.LambdaExpression}' is a ThenInclude with no Include before it.", nameof(specification))
                    : $"{path}.{GetPath(includeExpression.LambdaExpression)}";
            }
        }

        return path is null ? query : query.Include(path);
    }

    // The dotted path a selector such as x => x.Company.Country names: "Company.Country".
    private static string GetPath(LambdaExpression selector)
    {
        var expression = selector.Body;
        string? path = null;
        while (expression is MemberExpression member)
        {
            path = path is null ? member.Member.Name : $"{member.Member.Name}.{path}";
            expression = member.Expression;
        }

        return path is not null && expression is ParameterExpression
            ? path
            : throw new ArgumentException($"The include expression '{selector}' must name a navigation property, or a chain of them such as x => x.Company.Country.", nameof(selector));
    }
}
