namespace Arkki.Pdf.Signing;

/// <summary>
/// What a visible signature looks like on its page. With nothing set it is a few lines of text: who signed, when,
/// and the reason and location when the options give them. Add an image (a scanned signature, a logo, a stamp), your
/// own text and font, a border, or draw the whole box yourself with <see cref="Draw"/>.
/// <para>The picture is only that: what proves the signature is its cryptographic value, which every viewer checks
/// separately. A signature without an appearance is just as valid.</para>
/// </summary>
public sealed class PdfSignatureAppearance
{
    /// <summary>
    /// Left edge of the box on the page, in points from the page's left edge. The box is placed on the page as it is
    /// stored: on a page that viewers show rotated, the box turns with the page.
    /// </summary>
    public double X { get; init; }

    /// <summary>Top edge of the box, in points from the page's top edge (the canvas's coordinates: y grows down).</summary>
    public double Y { get; init; }

    /// <summary>
    /// Width of the box in points. Leave <see cref="Width"/> and <see cref="Height"/> at 0 when signing an existing
    /// signature field: the field's own box is used.
    /// </summary>
    public double Width { get; init; }

    /// <summary>Height of the box in points.</summary>
    public double Height { get; init; }

    /// <summary>
    /// The text in the box; lines are separated by '\n' and long lines wrap. Null writes the default description
    /// ("Digitally signed by …", the date, the reason and the location); an empty string writes no text.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// A TrueType or OpenType font file for the text, embedded in the signature. Null uses Helvetica, which every viewer
    /// has but which covers Western European characters only.
    /// </summary>
    public byte[]? Font { get; init; }

    /// <summary>The text size in points; 0 picks the largest size up to 12 at which the text fits the box.</summary>
    public double FontSize { get; init; }

    /// <summary>
    /// A PNG or JPEG picture, scaled to fit: the left part of the box when there is text, the whole box when
    /// <see cref="Text"/> is empty. A PNG's transparency is kept.
    /// </summary>
    public byte[]? Image { get; init; }

    /// <summary>Draws a thin grey frame around the box.</summary>
    public bool Border { get; init; }

    /// <summary>
    /// Draws on the box with the canvas after the image and text: the canvas is <see cref="Width"/> by
    /// <see cref="Height"/> points with its origin at the box's top-left corner. Add the fonts and images you draw with
    /// to the document passed in. Set <see cref="Text"/> to "" to draw everything yourself.
    /// </summary>
    public Action<PdfDocument, PdfCanvas>? Draw { get; init; }
}

/// <summary>What a certification signature lets others do to the document afterwards without breaking it (DocMDP).</summary>
public enum PdfCertificationLevel
{
    /// <summary>Not a certification: an ordinary approval signature.</summary>
    None = 0,
    /// <summary>No changes at all.</summary>
    NoChanges = 1,
    /// <summary>Filling in form fields and signing.</summary>
    FormFilling = 2,
    /// <summary>Filling in form fields, signing, and adding, changing and removing annotations (comments).</summary>
    FormFillingAndAnnotations = 3,
}
