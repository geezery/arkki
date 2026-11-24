namespace Arkki.Pdf.Layout;

/// <summary>Where a header or footer is drawn: the page number and how many pages there are.</summary>
/// <param name="PageNumber">The page number, from 1.</param>
/// <param name="PageCount">The number of pages in the flow.</param>
public readonly record struct PageContext(int PageNumber, int PageCount)
{
    /// <summary>True on the first page.</summary>
    public bool IsFirst => PageNumber == 1;
    /// <summary>True on the last page.</summary>
    public bool IsLast => PageNumber == PageCount;
}
