using Arkki.Pdf;
using Arkki.Pdf.Reading;
using static Arkki.Tests.Assert;

namespace Arkki.Tests;

/// <summary>AES-256 encryption on save, and opening files other programs encrypted.</summary>
internal static class EncryptionTests
{
    public static void Run()
    {
        // Files qpdf encrypted, every standard algorithm: RC4 40 and 128, AES-128, AES-256.
        foreach (string name in new[] { "encrypted-rc4-40", "encrypted-rc4-128", "encrypted-aes-128", "encrypted" })
        {
            byte[] file = TestData.Pdf(name);
            PdfReader user = PdfReader.Open(file, "user");
            Check($"{name}: opens with the user password", user.IsEncrypted && user.PageCount == 2);
            Check($"{name}: text decrypts", user.ExtractText(0).Contains("First page"), user.ExtractText(0));
            Check($"{name}: user is limited or full", user.Permissions != PdfPermissions.None);
            PdfReader owner = PdfReader.Open(file, "owner");
            Equal($"{name}: owner has every permission", PdfPermissions.All, owner.Permissions);
            Throws<PdfPasswordException>($"{name}: a wrong password fails", () => PdfReader.Open(file, "wrong"));
            // Importing decrypts: the copy is a plain file.
            byte[] plain = PdfDocument.Merge([]).Pages.Count == 0 ? PdfDocumentFrom(file, "user") : [];
            Check($"{name}: imported copy is not encrypted", !PdfReader.Open(plain).IsEncrypted && PdfReader.Open(plain).ExtractText(1).Contains("Second page"));
            External.QpdfClean($"{name}: decrypted copy", plain);
        }
        PdfReader open = PdfReader.Open(TestData.Pdf("encrypted-empty-user"));
        Check("empty user password opens without one", open.IsEncrypted && open.PageCount == 2);
        Check("its permissions forbid printing", !open.Permissions.HasFlag(PdfPermissions.Print));

        // Encrypt on save.
        var doc = new PdfDocument { Encryption = new PdfEncryption("salainen", "omistaja", PdfPermissions.Print | PdfPermissions.Copy) };
        PdfFont font = doc.AddFont(TestData.Regular);
        doc.Info.Title = "Salainen raportti";
        PdfPage page = doc.AddPage();
        page.Canvas.Text(font, 12, 72, 72, "Luottamuksellinen sisältö");
        page.AddTextField("field", 72, 100, 100, 18, "arvo");
        doc.Attach("data.txt", "liite"u8.ToArray());
        byte[] pdf = doc.ToArray();
        Check("encrypted file hides its text", !System.Text.Encoding.Latin1.GetString(pdf).Contains("Salainen raportti"));
        Throws<PdfPasswordException>("our file needs the password", () => PdfReader.Open(pdf));
        PdfReader back = PdfReader.Open(pdf, "salainen");
        Check("our file opens with the user password", back.ExtractText(0).Contains("Luottamuksellinen sisältö"), back.ExtractText(0));
        Equal("our title decrypts", "Salainen raportti", back.Info.Title);
        Equal("our field value decrypts", "arvo", back.GetFormFields()[0].Value);
        Equal("user permissions as set", PdfPermissions.Print | PdfPermissions.Copy, back.Permissions);
        Equal("owner password gives all", PdfPermissions.All, PdfReader.Open(pdf, "omistaja").Permissions);
        External.QpdfClean("AES-256 file (user password)", pdf, "salainen");
        External.QpdfClean("AES-256 file (owner password)", pdf, "omistaja");
        var qpdfDecrypt = External.Run("qpdf", pdf, "--password=salainen", "--show-encryption", "{file}");
        if (qpdfDecrypt is not null)
            Check("qpdf sees AES-256 revision 6", qpdfDecrypt.Value.Output.Contains("R = 6") && qpdfDecrypt.Value.Output.Contains("AESv3"), qpdfDecrypt.Value.Output);
        string? poppler = External.PopplerText(pdf, "salainen");
        if (poppler is not null) Check("poppler decrypts our file", poppler.Contains("Luottamuksellinen sisältö"), poppler);

        // Re-encrypting an opened encrypted file with a new password.
        PdfDocument again = PdfDocument.Open(pdf, "omistaja");
        again.Encryption = new PdfEncryption("uusi");
        byte[] reencrypted = again.ToArray();
        Check("re-encrypted file opens with the new password", PdfReader.Open(reencrypted, "uusi").ExtractText(0).Contains("Luottamuksellinen"));
        Throws<PdfPasswordException>("the old password no longer works", () => PdfReader.Open(reencrypted, "salainen"));
        External.QpdfClean("re-encrypted file", reencrypted, "uusi");

        // Non-ASCII password.
        var nordic = new PdfDocument { Encryption = new PdfEncryption("sälasana€") };
        nordic.AddPage();
        Check("non-ASCII password round-trips", PdfReader.Open(nordic.ToArray(), "sälasana€").PageCount == 1);
    }

    private static byte[] PdfDocumentFrom(byte[] file, string password) => PdfDocument.Open(file, password).ToArray();
}
