namespace Arkki.Pdf.Layout;

/// <summary>
/// A node of the document tree. Elements are plain data: they can be built in code, by a fluent
/// chain of modifiers, or from a stored template. Layout is a pure function of the element and
/// the space offered, which is what makes pagination and testing simple.
/// </summary>
public abstract class Element
{
    /// <summary>Lays the element out in the space offered (points). Returns what fits as a fragment and, when not all of it fits, the rest to continue on the next page.</summary>
    public abstract LayoutResult Layout(double width, double height);
}
