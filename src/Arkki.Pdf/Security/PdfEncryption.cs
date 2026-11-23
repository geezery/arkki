using System.Security.Cryptography;
using System.Text;

namespace Arkki.Pdf;

/// <summary>What a reader of an encrypted document may do without the owner password.</summary>
[Flags]
public enum PdfPermissions
{
    None = 0,
    Print = 1 << 2,
    Modify = 1 << 3,
    Copy = 1 << 4,
    Annotate = 1 << 5,
    FillForms = 1 << 8,
    ExtractForAccessibility = 1 << 9,
    Assemble = 1 << 10,
    PrintHighQuality = 1 << 11,
    All = Print | Modify | Copy | Annotate | FillForms | ExtractForAccessibility | Assemble | PrintHighQuality,
}

/// <summary>
/// Password protection with AES-256 (PDF 2.0 standard security handler, revision 6). The user password opens the
/// document; the owner password opens it with every permission. An empty user password lets anyone open the file but
/// keeps the permissions, which viewers honour.
/// </summary>
public sealed class PdfEncryption
{
    public PdfEncryption(string userPassword, string? ownerPassword = null, PdfPermissions permissions = PdfPermissions.All)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        UserPassword = userPassword;
        OwnerPassword = ownerPassword;
        Permissions = permissions;
    }

    public string UserPassword { get; }

    /// <summary>Null makes a random one: nobody can then lift the permissions.</summary>
    public string? OwnerPassword { get; }

    public PdfPermissions Permissions { get; }

    /// <summary>Encrypt the XMP metadata too (default). Search tools can index unencrypted metadata.</summary>
    public bool EncryptMetadata { get; init; } = true;

    internal Security.Encryptor CreateEncryptor(byte[] documentId, int dictionaryId)
    {
        byte[] fileKey = RandomNumberGenerator.GetBytes(32);
        byte[] user = Security.PasswordR6.Prepare(UserPassword);
        byte[] owner = OwnerPassword is null ? RandomNumberGenerator.GetBytes(32) : Security.PasswordR6.Prepare(OwnerPassword);

        byte[] uSalts = RandomNumberGenerator.GetBytes(16);
        byte[] u = [.. Security.PasswordR6.Hash(user, uSalts.AsSpan(0, 8), []), .. uSalts];
        byte[] ue = Security.PasswordR6.AesNoIv(Security.PasswordR6.Hash(user, uSalts.AsSpan(8, 8), []), fileKey, encrypt: true);

        byte[] oSalts = RandomNumberGenerator.GetBytes(16);
        byte[] o = [.. Security.PasswordR6.Hash(owner, oSalts.AsSpan(0, 8), u), .. oSalts];
        byte[] oe = Security.PasswordR6.AesNoIv(Security.PasswordR6.Hash(owner, oSalts.AsSpan(8, 8), u), fileKey, encrypt: true);

        int p = unchecked((int)0xFFFFF0C0) | (int)Permissions;
        byte[] perms = new byte[16];
        BitConverter.TryWriteBytes(perms, p);
        if (!BitConverter.IsLittleEndian) Array.Reverse(perms, 0, 4);
        perms[4] = perms[5] = perms[6] = perms[7] = 0xFF;
        perms[8] = (byte)(EncryptMetadata ? 'T' : 'F');
        perms[9] = (byte)'a'; perms[10] = (byte)'d'; perms[11] = (byte)'b';
        RandomNumberGenerator.Fill(perms.AsSpan(12));
        using (var aes = Aes.Create()) { aes.Key = fileKey; perms = aes.EncryptEcb(perms, PaddingMode.None); }

        var d = new StringBuilder("<< /Filter /Standard /V 5 /R 6 /Length 256 /CF << /StdCF << /AuthEvent /DocOpen /CFM /AESV3 /Length 32 >> >> /StmF /StdCF /StrF /StdCF");
        d.Append(" /O <").Append(Convert.ToHexString(o)).Append("> /U <").Append(Convert.ToHexString(u));
        d.Append("> /OE <").Append(Convert.ToHexString(oe)).Append("> /UE <").Append(Convert.ToHexString(ue));
        d.Append("> /P ").Append(p).Append(" /Perms <").Append(Convert.ToHexString(perms)).Append('>');
        if (!EncryptMetadata) d.Append(" /EncryptMetadata false");
        d.Append(" >>");
        return new Security.Encryptor(fileKey, Encoding.ASCII.GetBytes(d.ToString()));
    }
}

/// <summary>The password given does not open the encrypted document.</summary>
public sealed class PdfPasswordException(string message) : Exception(message);
