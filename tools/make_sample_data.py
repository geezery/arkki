#!/usr/bin/env python3
"""Generates the fictional sample data used by the invoice sample:
samples/data/finvoice-sample.xml (Finvoice 3.0) and samples/data/logo.png."""
from decimal import Decimal, ROUND_HALF_UP
from xml.sax.saxutils import escape
import os, sys

OUT = os.path.join(os.path.dirname(__file__), "..", "samples", "data")
D = Decimal

def q(v): return v.quantize(D("0.01"), rounding=ROUND_HALF_UP)
def fi(v, places="0.01"): return str(v.quantize(D(places), rounding=ROUND_HALF_UP)).replace(".", ",")

def national_reference(base):
    weights, total = (7, 3, 1), 0
    for i, ch in enumerate(reversed(base)):
        total += int(ch) * weights[i % 3]
    return base + str((10 - total % 10) % 10)

ITEMS = [
    ("KP-1001", "Koneistus, CNC-sorvaus", "h", "86.00", "25.5"),
    ("KP-1002", "Koneistus, 5-akselinen jyrsintä", "h", "112.00", "25.5"),
    ("KP-1010", "Hitsaustyö, MIG/MAG", "h", "74.50", "25.5"),
    ("KP-1011", "Hitsaustyö, TIG, ruostumaton teräs", "h", "82.00", "25.5"),
    ("MA-2001", "Teräslevy S355 10 mm, polttoleikattu", "kg", "2.35", "25.5"),
    ("MA-2002", "Pyörötanko 42CrMo4 Ø60", "m", "48.90", "25.5"),
    ("MA-2003", "Alumiiniprofiili 6082-T6 40x40", "m", "13.75", "25.5"),
    ("MA-2010", "Ruostumaton putki EN 1.4404 Ø48,3x2,6", "m", "27.40", "25.5"),
    ("OS-3001", "Kuulalaakeri 6208-2RS", "kpl", "18.60", "25.5"),
    ("OS-3002", "Kiilahihna SPA 1500", "kpl", "14.20", "25.5"),
    ("OS-3005", "Kuusioruuvi M12x60 10.9 sinkitty", "kpl", "0.42", "25.5"),
    ("OS-3006", "Lukkomutteri M12 DIN 985", "kpl", "0.18", "25.5"),
    ("PI-4001", "Pintakäsittely, jauhemaalaus RAL 7016", "m2", "24.00", "25.5"),
    ("PI-4002", "Pintakäsittely, kuumasinkitys", "kg", "0.95", "25.5"),
    ("SU-5001", "Suunnittelutyö, mekaniikkasuunnittelu", "h", "96.00", "25.5"),
    ("KU-6001", "Rahti ja pakkaus", "kpl", "65.00", "25.5"),
    ("KI-7001", "Ammattikirjallisuus: Hitsaajan käsikirja", "kpl", "42.00", "13.5"),
]
QTY = ["4", "2.5", "6", "3", "184", "3.2", "12", "8.5", "6", "4", "120", "120", "9.4", "212", "5", "1", "2"]

rows = []
for n in range(44):
    code, name, unit, price, rate = ITEMS[n % len(ITEMS)]
    qty = D(QTY[(n * 5 + 3) % len(QTY)])
    if unit == "kpl": qty = qty.quantize(D("1"), rounding=ROUND_HALF_UP) or D(1)
    net = q(qty * D(price))
    vat = q(net * D(rate) / 100)
    free = []
    if n == 2:
        free = ["Työ sisältää railon valmistelun, hitsauksen WPS 135-01 mukaisesti sekä silmämääräisen tarkastuksen "
                "(VT) standardin SFS-EN ISO 5817 hitsiluokan C mukaan. Tarkastuspöytäkirja toimitetaan erikseen sähköpostitse."]
    if n == 20:
        free = ["Toimitus: työnumero 2026-0412, piirustus KP-88213 rev. C"]
    rows.append(dict(code=code, name=name, unit=unit, price=D(price), rate=D(rate), qty=qty, net=net, vat=vat, free=free))

by_rate = {}
for r in rows:
    by_rate.setdefault(r["rate"], D(0))
    by_rate[r["rate"]] += r["net"]
specs = [(rate, base, q(base * rate / 100)) for rate, base in sorted(by_rate.items(), reverse=True)]
total_net = sum(s[1] for s in specs)
total_vat = sum(s[2] for s in specs)
total = total_net + total_vat

IBAN = "FI2112345600000785"      # the example account used throughout Finance Finland's guides
BIC = "NDEAFIHH"
INVOICE = "20261187"
REFERENCE = national_reference("1004" + INVOICE)
EUR = 'AmountCurrencyIdentifier="EUR"'

x = []
a = x.append
a('<?xml version="1.0" encoding="UTF-8"?>')
a('<Finvoice Version="3.0" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="Finvoice3.0.xsd">')
a('  <MessageTransmissionDetails>')
a('    <MessageSenderDetails><FromIdentifier SchemeID="0216">003712345671</FromIdentifier><FromIntermediator>NDEAFIHH</FromIntermediator></MessageSenderDetails>')
a('    <MessageReceiverDetails><ToIdentifier SchemeID="0216">003776543213</ToIdentifier><ToIntermediator>OKOYFIHH</ToIntermediator></MessageReceiverDetails>')
a(f'    <MessageDetails><MessageIdentifier>{INVOICE}</MessageIdentifier><MessageTimeStamp>2026-11-23T09:15:00+02:00</MessageTimeStamp></MessageDetails>')
a('  </MessageTransmissionDetails>')
a('  <SellerPartyDetails>')
a('    <SellerPartyIdentifier>1234567-1</SellerPartyIdentifier>')
a('    <SellerOrganisationName>Esimerkki Konepaja Oy</SellerOrganisationName>')
a('    <SellerOrganisationTaxCode>FI12345671</SellerOrganisationTaxCode>')
a('    <SellerPostalAddressDetails><SellerStreetName>Teollisuuskatu 12</SellerStreetName><SellerTownName>Tampere</SellerTownName><SellerPostCodeIdentifier>33100</SellerPostCodeIdentifier><CountryCode>FI</CountryCode><CountryName>Suomi</CountryName></SellerPostalAddressDetails>')
a('  </SellerPartyDetails>')
a('  <SellerContactPersonName>Maija Mallikas</SellerContactPersonName>')
a('  <SellerInformationDetails>')
a('    <SellerHomeTownName>Tampere</SellerHomeTownName>')
a('    <SellerVatRegistrationText>Alv.Rek.</SellerVatRegistrationText>')
a('    <SellerPhoneNumber>+358 40 123 4567</SellerPhoneNumber>')
a('    <SellerCommonEmailaddressIdentifier>laskutus@esimerkkikonepaja.example</SellerCommonEmailaddressIdentifier>')
a('    <SellerWebaddressIdentifier>www.esimerkkikonepaja.example</SellerWebaddressIdentifier>')
a(f'    <SellerAccountDetails><SellerAccountID IdentificationSchemeName="IBAN">{IBAN}</SellerAccountID><SellerBic IdentificationSchemeName="BIC">{BIC}</SellerBic></SellerAccountDetails>')
a('  </SellerInformationDetails>')
a('  <BuyerPartyDetails>')
a('    <BuyerPartyIdentifier>7654321-3</BuyerPartyIdentifier>')
a('    <BuyerOrganisationName>Mallitilaaja Oy Ab</BuyerOrganisationName>')
a('    <BuyerOrganisationTaxCode>FI76543213</BuyerOrganisationTaxCode>')
a('    <BuyerPostalAddressDetails><BuyerStreetName>Satamakatu 4 B 17</BuyerStreetName><BuyerTownName>Jyväskylä</BuyerTownName><BuyerPostCodeIdentifier>40100</BuyerPostCodeIdentifier><CountryCode>FI</CountryCode><CountryName>Suomi</CountryName></BuyerPostalAddressDetails>')
a('  </BuyerPartyDetails>')
a('  <BuyerContactPersonName>Åke Öhman</BuyerContactPersonName>')
a('  <InvoiceDetails>')
a('    <InvoiceTypeCode>INV01</InvoiceTypeCode>')
a('    <InvoiceTypeText>Lasku</InvoiceTypeText>')
a('    <OriginCode>Original</OriginCode>')
a(f'    <InvoiceNumber>{INVOICE}</InvoiceNumber>')
a('    <InvoiceDate Format="CCYYMMDD">20261123</InvoiceDate>')
a('    <OrderIdentifier>PO-2026-0457</OrderIdentifier>')
a('    <AgreementIdentifier>SOP-2025-018</AgreementIdentifier>')
a('    <BuyerReferenceIdentifier>Å. Öhman / linja 3 huoltoseisokki</BuyerReferenceIdentifier>')
a(f'    <InvoiceTotalVatExcludedAmount {EUR}>{fi(total_net)}</InvoiceTotalVatExcludedAmount>')
a(f'    <InvoiceTotalVatAmount {EUR}>{fi(total_vat)}</InvoiceTotalVatAmount>')
a(f'    <InvoiceTotalVatIncludedAmount {EUR}>{fi(total)}</InvoiceTotalVatIncludedAmount>')
for rate, base, vat in specs:
    a(f'    <VatSpecificationDetails><VatBaseAmount {EUR}>{fi(base)}</VatBaseAmount><VatRatePercent>{fi(rate, "0.1")}</VatRatePercent><VatCode>S</VatCode><VatRateAmount {EUR}>{fi(vat)}</VatRateAmount></VatSpecificationDetails>')
a('    <InvoiceFreeText>Kiitos tilauksestanne. Huomautukset kahdeksan päivän kuluessa laskun päiväyksestä.</InvoiceFreeText>')
a('    <PaymentTermsDetails>')
a('      <PaymentTermsFreeText>14 päivää netto</PaymentTermsFreeText>')
a('      <InvoiceDueDate Format="CCYYMMDD">20261207</InvoiceDueDate>')
a('      <PaymentOverDueFineDetails><PaymentOverDueFineFreeText>Viivästyskorko 10,0 %</PaymentOverDueFineFreeText><PaymentOverDueFinePercent>10,0</PaymentOverDueFinePercent></PaymentOverDueFineDetails>')
a('    </PaymentTermsDetails>')
a('  </InvoiceDetails>')
for r in rows:
    a('  <InvoiceRow>')
    a(f'    <ArticleIdentifier>{r["code"]}</ArticleIdentifier>')
    a(f'    <ArticleName>{escape(r["name"])}</ArticleName>')
    a(f'    <DeliveredQuantity QuantityUnitCode="{r["unit"]}">{fi(r["qty"], "0.1") if r["qty"] % 1 else str(int(r["qty"]))}</DeliveredQuantity>')
    a(f'    <UnitPriceAmount {EUR}>{fi(r["price"])}</UnitPriceAmount>')
    for t in r["free"]:
        a(f'    <RowFreeText>{escape(t)}</RowFreeText>')
    a(f'    <RowVatRatePercent>{fi(r["rate"], "0.1")}</RowVatRatePercent>')
    a(f'    <RowVatAmount {EUR}>{fi(r["vat"])}</RowVatAmount>')
    a(f'    <RowVatExcludedAmount {EUR}>{fi(r["net"])}</RowVatExcludedAmount>')
    a(f'    <RowAmount {EUR}>{fi(r["net"] + r["vat"])}</RowAmount>')
    a('  </InvoiceRow>')
a('  <EpiDetails>')
a(f'    <EpiIdentificationDetails><EpiDate Format="CCYYMMDD">20261123</EpiDate><EpiReference>{INVOICE}</EpiReference></EpiIdentificationDetails>')
a('    <EpiPartyDetails>')
a(f'      <EpiBfiPartyDetails><EpiBfiIdentifier IdentificationSchemeName="BIC">{BIC}</EpiBfiIdentifier></EpiBfiPartyDetails>')
a(f'      <EpiBeneficiaryPartyDetails><EpiNameAddressDetails>Esimerkki Konepaja Oy</EpiNameAddressDetails><EpiBei>12345671</EpiBei><EpiAccountID IdentificationSchemeName="IBAN">{IBAN}</EpiAccountID></EpiBeneficiaryPartyDetails>')
a('    </EpiPartyDetails>')
a('    <EpiPaymentInstructionDetails>')
a(f'      <EpiRemittanceInfoIdentifier IdentificationSchemeName="SPY">{REFERENCE}</EpiRemittanceInfoIdentifier>')
a(f'      <EpiInstructedAmount {EUR}>{fi(total)}</EpiInstructedAmount>')
a('      <EpiCharge ChargeOption="SHA">SHA</EpiCharge>')
a('      <EpiDateOptionDate Format="CCYYMMDD">20261207</EpiDateOptionDate>')
a('    </EpiPaymentInstructionDetails>')
a('  </EpiDetails>')
a('</Finvoice>')

os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, "finvoice-sample.xml"), "w", encoding="utf-8") as f:
    f.write("\n".join(x) + "\n")
print("rows", len(rows), "net", total_net, "vat", total_vat, "total", total, "ref", REFERENCE)

# ---- fictional logo with a transparent background ------------------------------------------------
from PIL import Image, ImageDraw, ImageFont
W, H, S = 1240, 360, 4
img = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
navy, teal = (29, 53, 87, 255), (42, 157, 143, 255)
cx, cy, R = 180 * S, 180 * S, 150 * S
import math
pts = []
for i in range(24):                       # a simple cog outline
    ang = math.pi * 2 * i / 24
    rad = R if i % 2 == 0 else R * 0.86
    for da in (-0.07, 0.07):
        pts.append((cx + rad * math.cos(ang + da), cy + rad * math.sin(ang + da)))
d.polygon(pts, fill=navy)
d.ellipse((cx - 92 * S, cy - 92 * S, cx + 92 * S, cy + 92 * S), fill=(255, 255, 255, 0))
d.ellipse((cx - 56 * S, cy - 56 * S, cx + 56 * S, cy + 56 * S), fill=teal)
bold = ImageFont.truetype("/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf", 118 * S)
regular = ImageFont.truetype("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", 76 * S)
d.text((372 * S, 52 * S), "ESIMERKKI", font=bold, fill=navy)
d.text((376 * S, 196 * S), "KONEPAJA OY", font=regular, fill=teal)
img = img.resize((W, H), Image.LANCZOS)
img.save(os.path.join(OUT, "logo.png"), optimize=True)
print("logo", img.size)
