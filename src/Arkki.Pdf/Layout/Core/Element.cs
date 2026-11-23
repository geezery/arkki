namespace Arkki.Pdf.Layout;

/// <summary>
/// A node of the document tree. Elements are plain data: they can be built in code, by a fluent
/// chain of modifiers, or from a stored template. Layout is a pure function of the element and
/// the space offered, which is what makes pagination and testing simple.
/// </summary>
public abstract class Element
{
    public abstract LayoutResult Layout(double width, double height);
}
