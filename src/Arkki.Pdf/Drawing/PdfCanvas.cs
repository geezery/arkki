using Arkki.Pdf.Internal;

namespace Arkki.Pdf;

/// <summary>
/// Immediate-mode drawing surface for one page. The origin is the top-left corner of the page and
/// y grows downwards; units are points. Every call appends PDF operators directly to the page's
/// content stream, so there is no intermediate object model to allocate.
/// </summary>
public sealed class PdfCanvas
{
    private readonly ByteBuf _b = new(4096);
    private Color _fill = Color.Black, _stroke = Color.Black;
    private double _lineWidth = 1;
    private PdfFont? _font;
    private double _fontSize;
    private int _depth;

    private readonly PdfDocument _document;

    internal PdfCanvas(PdfDocument document, PdfPage page, double originX, double originY, double pageHeight)
    {
        _document = document;
        _page = page;
        // Flip the coordinate system once: top-left origin, y down.
        _b.Ascii("1 0 0 -1 ").Num(originX).Sp().Num(originY + pageHeight).Ascii(" cm\n");
        _initialLength = _b.Length;
    }

    private readonly PdfPage _page;

    /// <summary>Makes the rectangle a link to a web address; see <see cref="PdfPage.AddLink(double, double, double, double, Uri)"/>.</summary>
    public void Link(double x, double y, double width, double height, Uri target) => _page.AddLink(x, y, width, height, target);

    /// <summary>Makes the rectangle a link to a place in the document.</summary>
    public void Link(double x, double y, double width, double height, PdfPage target, double top = 0) => _page.AddLink(x, y, width, height, target, top);

    /// <summary>The page this canvas draws on.</summary>
    public PdfPage Page => _page;

    private readonly int _initialLength;

    /// <summary>True until something is drawn.</summary>
    internal bool IsEmpty => _b.Length == _initialLength;

    internal ReadOnlySpan<byte> Content => _b.Span;

    private static double F(double v, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(v))] string? name = null) =>
        double.IsFinite(v) ? v : throw new ArgumentOutOfRangeException(name, v, "Coordinates and sizes must be finite numbers.");

    private static Color Valid(Color c, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(c))] string? name = null) =>
        double.IsNaN(c.R) || double.IsNaN(c.G) || double.IsNaN(c.B) ? throw new ArgumentOutOfRangeException(name, "A color component is NaN.")
        : new Color(Math.Clamp(c.R, 0, 1), Math.Clamp(c.G, 0, 1), Math.Clamp(c.B, 0, 1));

    private static double FontSize(double size) =>
        double.IsFinite(size) && size > 0 ? size : throw new ArgumentOutOfRangeException(nameof(size), size, "The font size must be a positive number.");

    /// <summary>Sets the colour that text and filled shapes are drawn in. Components are clamped to 0..1.</summary>
    public void SetFillColor(Color c)
    {
        c = Valid(c);
        if (c == _fill) return;
        _fill = c;
        _b.Num(c.R).Sp().Num(c.G).Sp().Num(c.B).Ascii(" rg\n");
    }

    /// <summary>Sets the colour of lines and outlines. Components are clamped to 0..1.</summary>
    public void SetStrokeColor(Color c)
    {
        c = Valid(c);
        if (c == _stroke) return;
        _stroke = c;
        _b.Num(c.R).Sp().Num(c.G).Sp().Num(c.B).Ascii(" RG\n");
    }

    /// <summary>Sets the width of lines and outlines in points; 0 is the thinnest line the device can draw.</summary>
    public void SetLineWidth(double width)
    {
        if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width), width, "The line width must be zero or more.");
        if (width == _lineWidth) return;
        _lineWidth = width;
        _b.Num(width).Ascii(" w\n");
    }

    /// <summary>Draws a straight line in the stroke colour.</summary>
    public void Line(double x1, double y1, double x2, double y2)
    {
        _b.Num(F(x1)).Sp().Num(F(y1)).Ascii(" m ").Num(F(x2)).Sp().Num(F(y2)).Ascii(" l S\n");
    }

    /// <summary>Fills a rectangle (top-left corner, width, height) in the fill colour.</summary>
    public void FillRect(double x, double y, double width, double height)
    {
        _b.Num(F(x)).Sp().Num(F(y)).Sp().Num(F(width)).Sp().Num(F(height)).Ascii(" re f\n");
    }

    /// <summary>Outlines a rectangle in the stroke colour.</summary>
    public void StrokeRect(double x, double y, double width, double height)
    {
        _b.Num(F(x)).Sp().Num(F(y)).Sp().Num(F(width)).Sp().Num(F(height)).Ascii(" re S\n");
    }

    /// <summary>Draws text with its baseline starting at (x, y).</summary>
    public void Text(PdfFont font, double size, double x, double y, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(font);
        FontSize(size); F(x); F(y);
        if (text.IsEmpty) return;
        BeginText(font, size);
        _b.Ascii("1 0 0 -1 ").Num(x).Sp().Num(y).Ascii(" Tm ");
        font.Encode(text, _b);
        _b.Ascii(" Tj ET\n");
    }

    /// <summary>Draws text aligned so that it ends at <paramref name="xRight"/>.</summary>
    public void TextRight(PdfFont font, double size, double xRight, double y, ReadOnlySpan<char> text) =>
        Text(font, size, xRight - font.Width(text, size), y, text);

    /// <summary>Draws text rotated counter-clockwise by <paramref name="degrees"/> around its baseline origin.</summary>
    public void TextRotated(PdfFont font, double size, double x, double y, double degrees, ReadOnlySpan<char> text)
    {
        ArgumentNullException.ThrowIfNull(font);
        FontSize(size); F(x); F(y); F(degrees);
        if (text.IsEmpty) return;
        double rad = degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        BeginText(font, size);
        _b.Num(cos).Sp().Num(-sin).Sp().Num(-sin).Sp().Num(-cos).Sp().Num(x).Sp().Num(y).Ascii(" Tm ");
        font.Encode(text, _b);
        _b.Ascii(" Tj ET\n");
    }

    private void BeginText(PdfFont font, double size)
    {
        _b.Ascii("BT ");
        if (!ReferenceEquals(font, _font) || size != _fontSize)
        {
            _font = font; _fontSize = size;
            _b.Byte((byte)'/').Ascii(font.ResourceName).Sp().Num(size).Ascii(" Tf ");
        }
    }

    /// <summary>Draws an image into the given rectangle.</summary>
    public void Image(PdfImage image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        F(x); F(y); F(width); F(height);
        _b.Ascii("q ").Num(width).Ascii(" 0 0 ").Num(-height).Sp().Num(x).Sp().Num(y + height)
            .Ascii(" cm /").Ascii(_document.ImageName(image)).Ascii(" Do Q\n");
    }

    /// <summary>
    /// Fills a run of bars. <paramref name="modules"/> holds alternating bar/space widths in
    /// modules, starting with a bar. All bars are emitted as one path and filled once.
    /// </summary>
    public void Bars(ReadOnlySpan<byte> modules, double x, double y, double moduleWidth, double height)
    {
        double cx = x;
        for (int i = 0; i < modules.Length; i++)
        {
            double w = modules[i] * moduleWidth;
            if ((i & 1) == 0) _b.Num(cx).Sp().Num(y).Sp().Num(w).Sp().Num(height).Ascii(" re ");
            cx += w;
        }
        _b.Ascii("f\n");
    }

    /// <summary>
    /// Draws a barcode's dark modules into the given rectangle, without its quiet zone. Each row's
    /// runs of dark modules become one rectangle; all of them are filled once.
    /// </summary>
    public void Barcode(Barcodes.BarcodeMatrix matrix, double x, double y, double width, double height)
    {
        double mw = width / matrix.Columns, mh = height / matrix.Rows;
        for (int row = 0; row < matrix.Rows; row++)
        {
            for (int col = 0; col < matrix.Columns;)
            {
                if (!matrix[col, row]) { col++; continue; }
                int start = col;
                while (col < matrix.Columns && matrix[col, row]) col++;
                _b.Num(x + start * mw).Sp().Num(y + row * mh).Sp().Num((col - start) * mw).Sp().Num(mh).Ascii(" re ");
            }
        }
        _b.Ascii("f\n");
    }

    /// <summary>
    /// Sets the opacity (0 transparent to 1 opaque) of everything drawn after it, until
    /// <see cref="Restore"/> or the next call. A watermark under <see cref="Save"/>/<see cref="Restore"/> is the typical use.
    /// </summary>
    public void SetOpacity(double opacity)
    {
        if (double.IsNaN(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity), "The opacity is NaN.");
        opacity = Math.Clamp(opacity, 0, 1);
        _b.Ascii("/").Ascii(_document.OpacityName(opacity)).Ascii(" gs\n");
    }

    /// <summary>Saves the graphics state (colours, line width, opacity, clipping) until the matching <see cref="Restore"/>.</summary>
    public void Save()
    {
        _depth++;
        _b.Ascii("q\n");
    }

    /// <summary>Restores the state saved by the last <see cref="Save"/>; does nothing without one.</summary>
    public void Restore()
    {
        if (_depth == 0) return;
        _depth--;
        _b.Ascii("Q\n");
        // Graphics state is restored by the viewer; forget our cached copy so the next call re-emits it.
        _fill = _stroke = new Color(-1, -1, -1);
        _lineWidth = -1;
        _font = null;
    }

    /// <summary>Limits drawing to the rectangle until the next <see cref="Restore"/>.</summary>
    public void ClipRect(double x, double y, double width, double height)
    {
        _b.Num(F(x)).Sp().Num(F(y)).Sp().Num(F(width)).Sp().Num(F(height)).Ascii(" re W n\n");
    }
}
