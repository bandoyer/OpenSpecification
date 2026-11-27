namespace OpenSpecification;

/// <summary>
/// Represents a specification that can deduplicate query results with <c>Distinct()</c>.
/// </summary>
public interface IDistinctSpecification
{
    /// <summary>
    /// Returns whether the specification deduplicates query results.
    /// </summary>
    bool IsDistinct { get; }
}
