namespace Arkki.Pdf.Layout;

public readonly record struct PageContext(int PageNumber, int PageCount)
{
    public bool IsFirst => PageNumber == 1;
    public bool IsLast => PageNumber == PageCount;
}
