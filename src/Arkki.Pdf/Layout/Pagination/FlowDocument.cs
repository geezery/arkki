namespace Arkki.Pdf.Layout;

/// <summary>
/// Paginates an element tree. Layout runs first and produces positioned fragments for every
/// page; drawing happens afterwards, when the total page count is known, so headers and footers
/// can say "page 2 of 5" without a second layout pass.
/// </summary>
public sealed class FlowDocument
{
    /// <summary>Page width in points (A4 by default).</summary>
    public double PageWidth { get; init; } = PageSize.A4.Width;
    /// <summary>Page height in points (A4 by default).</summary>
    public double PageHeight { get; init; } = PageSize.A4.Height;
    /// <summary>Space between the page edge and the content.</summary>
    public Margins Margins { get; init; } = Margins.Mm(20, 15, 15, 15);

    /// <summary>Builds the header for a page. Its height must not depend on <see cref="PageContext.PageCount"/>.</summary>
    public Func<PageContext, Element?>? Header { get; init; }

    /// <summary>Builds the footer for a page. Its height must not depend on <see cref="PageContext.PageCount"/>.</summary>
    public Func<PageContext, Element?>? Footer { get; init; }

    /// <summary>What flows over the pages.</summary>
    public required Element Content { get; init; }

    /// <summary>Gap between header and content, and between content and footer.</summary>
    public double HeaderGap { get; init; } = Unit.Mm(4);

    /// <summary>
    /// Extra space kept free at the bottom of the last page (below the footer), for a form such
    /// as a payment slip that <see cref="LastPageOverlay"/> draws at a fixed position.
    /// </summary>
    public double LastPageReserve { get; init; }

    /// <summary>Draws directly on every page after its content, header and footer, e.g. a watermark.</summary>
    public Action<PdfCanvas, PageContext>? PageOverlay { get; init; }

    /// <summary>Draws directly on the last page after its content, e.g. a pre-printed style form.</summary>
    public Action<PdfCanvas, PageContext>? LastPageOverlay { get; init; }

    private sealed record PagePlan(Fragment? Content, double ContentTop);

    /// <summary>Lays out the content and adds as many pages to <paramref name="document"/> as it needs.</summary>
    public void RenderTo(PdfDocument document)
    {
        double contentWidth = PageWidth - Margins.Left - Margins.Right;
        var plans = new List<PagePlan>();
        Element? pending = Content;

        while (pending is not null)
        {
            int pageNumber = plans.Count + 1;
            var probe = new PageContext(pageNumber, 0);
            double headerHeight = Measure(Header?.Invoke(probe), contentWidth);
            double footerHeight = Measure(Footer?.Invoke(probe), contentWidth);
            double top = Margins.Top + headerHeight + (headerHeight > 0 ? HeaderGap : 0);
            double available = PageHeight - Margins.Bottom - footerHeight - (footerHeight > 0 ? HeaderGap : 0) - top;

            LayoutResult r = pending.Layout(contentWidth, available);
            if (r.Status == LayoutStatus.Full && LastPageReserve > 0 && r.Fragment!.Height > available - LastPageReserve)
            {
                // Everything fits on this page, but not together with the reserved area:
                // lay out again in the reduced space and let the rest flow to one more page.
                r = pending.Layout(contentWidth, available - LastPageReserve);
                if (r.Status == LayoutStatus.None) r = new LayoutResult(LayoutStatus.Partial, null, pending);
            }
            else if (r.Status == LayoutStatus.None)
            {
                if (plans.Count > 0 && plans[^1].Content is null)
                    throw new InvalidOperationException("Content does not fit on an empty page; an unbreakable element is taller than the page.");
                r = new LayoutResult(LayoutStatus.Partial, null, pending);
            }

            plans.Add(new PagePlan(r.Fragment, top));
            pending = r.Status == LayoutStatus.Full ? null : r.Remainder;
        }

        for (int i = 0; i < plans.Count; i++)
        {
            var context = new PageContext(i + 1, plans.Count);
            PdfCanvas canvas = document.AddPage(PageWidth, PageHeight).Canvas;

            if (Header?.Invoke(context) is { } header && header.Layout(contentWidth, PageHeight).Fragment is { } hf)
                hf.Draw(canvas, Margins.Left, Margins.Top);

            plans[i].Content?.Draw(canvas, Margins.Left, plans[i].ContentTop);

            if (Footer?.Invoke(context) is { } footer && footer.Layout(contentWidth, PageHeight).Fragment is { } ff)
            {
                double bottom = PageHeight - Margins.Bottom - (context.IsLast ? LastPageReserve : 0);
                ff.Draw(canvas, Margins.Left, bottom - ff.Height);
            }

            if (context.IsLast) LastPageOverlay?.Invoke(canvas, context);
            PageOverlay?.Invoke(canvas, context);
        }
    }

    private double Measure(Element? element, double width) =>
        element?.Layout(width, PageHeight).Fragment?.Height ?? 0;
}
