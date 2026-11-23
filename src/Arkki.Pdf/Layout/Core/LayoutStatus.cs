namespace Arkki.Pdf.Layout;

/// <summary>Outcome of asking an element to lay itself out in a given space.</summary>
public enum LayoutStatus
{
    /// <summary>Everything fitted.</summary>
    Full,
    /// <summary>Part of the element fitted; <see cref="LayoutResult.Remainder"/> continues on the next page.</summary>
    Partial,
    /// <summary>Nothing fitted; the element must move to the next page as a whole.</summary>
    None,
}
