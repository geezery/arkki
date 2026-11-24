using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Arkki.Pdf.Internal;
using Arkki.Pdf.Reading;

namespace Arkki.Pdf.Signing.Internal;

/// <summary>Finds the signatures of a document: every signature field with a value, by its full name.</summary>
internal static class SignatureFields
{
    public static List<(string Name, PdfDict Field, PdfDict Signature)> Find(PdfReader reader)
    {
        var found = new List<(string, PdfDict, PdfDict)>();
        if (reader.Resolve(reader.AcroForm?.Get("Fields")) is PdfArray fields)
            Collect(reader, fields, "", null, new HashSet<int>(), 0, found);
        return found;
    }

    private static void Collect(PdfReader reader, PdfArray kids, string prefix, string? inheritedType, HashSet<int> seen, int depth,
        List<(string, PdfDict, PdfDict)> found)
    {
        if (depth > 32) return;
        foreach (PdfValue item in kids.Items)
        {
            if (item is PdfRef r && !seen.Add(r.Number)) continue;
            if (reader.Resolve(item) is not PdfDict field) continue;
            string? partial = reader.Resolve(field.Get("T")) is PdfString t ? PdfSerializer.DecodeText(t.Bytes) : null;
            string name = partial is null ? prefix : prefix.Length == 0 ? partial : prefix + "." + partial;
            string? type = reader.Resolve(field.Get("FT")) is PdfName ft ? ft.Encoded : inheritedType;
            if (type == "Sig" && reader.Resolve(field.Get("V")) is PdfDict sig) found.Add((name, field, sig));
            if (reader.Resolve(field.Get("Kids")) is PdfArray children) Collect(reader, children, name, type, seen, depth + 1, found);
        }
    }

    /// <summary>True for a document timestamp: a signature whose value is an RFC 3161 token over the file.</summary>
    public static bool IsDocumentTimestamp(PdfReader reader, PdfDict signature) =>
        reader.Resolve(signature.Get("SubFilter")) is PdfName { Encoded: "ETSI.RFC3161" }
        || reader.Resolve(signature.Get("Type")) is PdfName { Encoded: "DocTimeStamp" };
}

/// <summary>
/// The document security store (DSS, PAdES part 4): the certificates, revocation lists and OCSP responses a file
/// carries so its signatures can be validated long after the authorities' own servers are gone.
/// </summary>
internal sealed class Dss
{
    private const int MaxItems = 2000;

    public List<X509Certificate2> Certificates { get; } = [];
    public List<byte[]> Crls { get; } = [];
    public List<byte[]> Ocsps { get; } = [];

    /// <summary>The references the store already lists, kept when it is extended.</summary>
    public List<PdfValue> CertificateRefs { get; } = [];
    public List<PdfValue> CrlRefs { get; } = [];
    public List<PdfValue> OcspRefs { get; } = [];

    public static Dss Read(PdfReader reader)
    {
        var dss = new Dss();
        if (reader.Resolve(reader.Catalog?.Get("DSS")) is not PdfDict dict) return dss;
        foreach (var (item, data) in Streams(reader, dict, "Certs", dss.CertificateRefs))
        {
            try { dss.Certificates.Add(X509CertificateLoader.LoadCertificate(data)); }
            catch (CryptographicException) { }
            _ = item;
        }
        foreach (var (_, data) in Streams(reader, dict, "CRLs", dss.CrlRefs)) dss.Crls.Add(data);
        foreach (var (_, data) in Streams(reader, dict, "OCSPs", dss.OcspRefs)) dss.Ocsps.Add(data);
        return dss;
    }

    private static IEnumerable<(PdfValue Item, byte[] Data)> Streams(PdfReader reader, PdfDict dss, string key, List<PdfValue> refs)
    {
        if (reader.Resolve(dss.Get(key)) is not PdfArray array) yield break;
        foreach (PdfValue item in array.Items.Take(MaxItems))
        {
            refs.Add(item);
            if (reader.Resolve(item) is not PdfStream stream) continue;
            byte[]? data = null;
            try { data = StreamDecoder.Decode(stream, reader.Resolve); }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException) { }
            if (data is not null) yield return (item, data);
        }
    }

    public RevocationData ToRevocationData()
    {
        var data = new RevocationData();
        foreach (byte[] crl in Crls) data.AddCrl(crl);
        foreach (byte[] ocsp in Ocsps) data.AddOcsp(ocsp);
        return data;
    }
}
