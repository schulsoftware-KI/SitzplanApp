using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SitzplanApp.Services;

/// <summary>
/// Rendert einen Sitzplan (<see cref="Loesung"/>) als Bild – exakt so, wie er
/// auf dem Bildschirm dargestellt wird – und schreibt ihn als PDF-Datei.
///
/// Das PDF wird ohne Zusatz-Bibliothek erzeugt: Der Sitzplan wird als JPEG in
/// eine minimale, gültige PDF-Struktur (ein Bild-XObject via DCTDecode)
/// eingebettet. Das Bild wird in DIN A4 quer eingepasst.
///
/// WICHTIG: <see cref="Render"/> nutzt <see cref="RenderTargetBitmap"/> und muss
/// daher auf dem UI-Thread aufgerufen werden (beim Klick auf „Lösung speichern"
/// ist das der Fall).
/// </summary>
public class KlassenplanImageService
{
    // Layout identisch zu SitzplatzVM/GruppeVM, damit das Bild dem Canvas entspricht.
    private const double KARTE_B = 120.0;
    private const double KARTE_H = 88.0;
    private const double LUECKE  = 20.0;
    private const double CARD_W  = 116.0;
    private const double CARD_H  = 78.0;

    private const double RAND     = 24.0;   // Außenrand
    private const double TITEL_H  = 30.0;
    private const double TAFEL_H  = 26.0;
    private const double LABEL_H  = 16.0;   // Platz über den Karten für Gruppen-Label

    private static readonly FontFamily FONT = new("Segoe UI");

    // ── Öffentlich ────────────────────────────────────────────────────────────

    /// <summary>Rendert den Sitzplan und speichert ihn als PDF unter <paramref name="pdfPfad"/>.</summary>
    public void SpeicherePdf(
        string pdfPfad, Loesung loesung, List<Tischgruppe> gruppen)
    {
        var bild = Render(loesung, gruppen);
        SchreibePdf(pdfPfad, bild);
    }

    /// <summary>Zeichnet den Sitzplan in ein Bitmap (2-fach für Druckqualität).</summary>
    public RenderTargetBitmap Render(Loesung loesung, List<Tischgruppe> gruppen)
    {
        // Ausdehnung des Plans bestimmen
        double planW = 0, planH = 0;
        foreach (var p in loesung.Sitzplaetze)
        {
            var (l, t) = KartenPos(p);
            planW = Math.Max(planW, l + KARTE_B);
            planH = Math.Max(planH, t + KARTE_H);
        }
        if (planW <= 0) planW = KARTE_B;
        if (planH <= 0) planH = KARTE_H;

        double contentTop = RAND + TITEL_H + 8 + TAFEL_H + 12 + LABEL_H;
        double totalW = RAND * 2 + planW;
        double totalH = contentTop + planH + RAND + 26; // 26 für Legende

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            // Titelzeile
            string titel = $"SITZPLAN – {loesung.Bezeichnung}   |   Score: {loesung.GesamtScore:F0}";
            var titelBrush = Farbe(loesung.Prio3Vollstaendig ? "#1F3864" : "#8B0000");
            dc.DrawRectangle(titelBrush, null, new Rect(RAND, RAND, planW, TITEL_H));
            var ftTitel = Text(titel, 14, Brushes.White, FontWeights.SemiBold);
            dc.DrawText(ftTitel, new Point(RAND + 10, RAND + (TITEL_H - ftTitel.Height) / 2));

            // TAFEL / VORNE
            double tafelY = RAND + TITEL_H + 8;
            double tafelW = Math.Min(planW, 520);
            double tafelX = RAND + (planW - tafelW) / 2;
            dc.DrawRoundedRectangle(Farbe("#1F3864"), null,
                new Rect(tafelX, tafelY, tafelW, TAFEL_H), 4, 4);
            var ftTafel = Text("TAFEL / VORNE", 11, Brushes.White, FontWeights.SemiBold);
            dc.DrawText(ftTafel,
                new Point(tafelX + (tafelW - ftTafel.Width) / 2, tafelY + (TAFEL_H - ftTafel.Height) / 2));

            double ox = RAND;
            double oy = contentTop;

            // Gruppen-Labels + Trennlinie
            foreach (var g in gruppen)
            {
                double gx = ox + (g.Spalte - 1) * (KARTE_B + LUECKE);
                double gy = oy + (g.Reihe - 1) * (KARTE_H + LUECKE) - LABEL_H;
                double breite = g.IstVertikal ? KARTE_B : g.Sitzplaetze * KARTE_B;

                var ftG = Text($"{g.Name} {(g.IstVertikal ? "↕" : "↔")}", 9, Farbe("#888888"), FontWeights.Normal);
                dc.DrawText(ftG, new Point(gx + 2, gy));
                dc.DrawRectangle(Farbe("#1F3864", 0.25), null,
                    new Rect(gx, gy + LABEL_H - 3, breite, 2));
            }

            // Sitzplatz-Karten
            foreach (var p in loesung.Sitzplaetze)
            {
                var (l, t) = KartenPos(p);
                double cx = ox + l + 2;
                double cy = oy + t + 2;
                var rect = new Rect(cx, cy, CARD_W, CARD_H);

                dc.DrawRoundedRectangle(Farbe(KartenFarbe(p, loesung)),
                    new Pen(Farbe("#1F3864"), 0.7), rect, 6, 6);

                // Kopf: Gruppe + Platz
                var ftKopf = Text($"{p.Gruppe.Name} P{p.PlatzNr}", 9, Farbe("#555555"), FontWeights.Normal);
                dc.DrawText(ftKopf, new Point(cx + (CARD_W - ftKopf.Width) / 2, cy + 6));

                if (p.Schueler != null)
                {
                    var ftName = Text(KurzName(p.Schueler.Name), 13, Brushes.Black,
                        FontWeights.SemiBold, CARD_W - 8, 2);
                    dc.DrawText(ftName, new Point(cx + 4, cy + 26));

                    if (!string.IsNullOrEmpty(p.Schueler.Geschlecht))
                    {
                        var ftG = Text(p.Schueler.Geschlecht, 10, Farbe("#555555"), FontWeights.Normal);
                        dc.DrawText(ftG, new Point(cx + (CARD_W - ftG.Width) / 2, cy + CARD_H - 16));
                    }

                    if (p.Schueler.FixGruppenname != null)
                    {
                        var ftFix = Text("Fix", 12, Brushes.Black, FontWeights.Bold);
                        dc.DrawText(ftFix, new Point(cx + CARD_W - ftFix.Width - 6, cy + CARD_H - ftFix.Height - 4));
                    }
                }
                else
                {
                    var ftFrei = Text("[frei]", 11, Farbe("#999999"), FontWeights.Normal);
                    dc.DrawText(ftFrei, new Point(cx + (CARD_W - ftFrei.Width) / 2, cy + CARD_H / 2 - 8));
                }
            }

            // Legende
            double ly = totalH - RAND - 4;
            double lx = RAND;
            lx = Legende(dc, lx, ly, "#E74C3C", "Prio-3 Verletzung");
            lx = Legende(dc, lx, ly, "#FAD7A0", "Prio-2 Verletzung");
            lx = Legende(dc, lx, ly, "#FDEBD0", "Prio-1 Verletzung");
            lx = Legende(dc, lx, ly, "#D6E4F0", "Belegt");
            _  = Legende(dc, lx, ly, "#F5F5F5", "Frei");
        }

        const double SCALE = 2.0;
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(totalW * SCALE),
            (int)Math.Ceiling(totalH * SCALE),
            96 * SCALE, 96 * SCALE, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ── PDF (minimal, ohne Zusatz-Bibliothek) ──────────────────────────────────

    private void SchreibePdf(string pfad, RenderTargetBitmap bild)
    {
        // Bild als JPEG kodieren
        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(bild));
        byte[] jpeg;
        using (var msImg = new MemoryStream())
        {
            enc.Save(msImg);
            jpeg = msImg.ToArray();
        }

        int iw = bild.PixelWidth, ih = bild.PixelHeight;

        // DIN A4 quer, Bild proportional einpassen
        const double PAGE_W = 842, PAGE_H = 595, M = 20;
        double availW = PAGE_W - 2 * M, availH = PAGE_H - 2 * M;
        double scale = Math.Min(availW / iw, availH / ih);
        double dw = iw * scale, dh = ih * scale;
        double dx = (PAGE_W - dw) / 2, dy = (PAGE_H - dh) / 2;

        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string content = $"q\n{F(dw)} 0 0 {F(dh)} {F(dx)} {F(dy)} cm\n/Im0 Do\nQ\n";
        byte[] contentBytes = Encoding.ASCII.GetBytes(content);

        using var fs = new FileStream(pfad, FileMode.Create, FileAccess.Write);
        var offsets = new long[6];
        long pos = 0;

        void W(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            fs.Write(b, 0, b.Length);
            pos += b.Length;
        }
        void WBytes(byte[] b)
        {
            fs.Write(b, 0, b.Length);
            pos += b.Length;
        }

        W("%PDF-1.4\n");
        WBytes(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }); // Binär-Marker

        offsets[1] = pos;
        W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = pos;
        W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = pos;
        W($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PAGE_W)} {F(PAGE_H)}] " +
          "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

        offsets[4] = pos;
        W($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        WBytes(contentBytes);
        W("endstream\nendobj\n");

        offsets[5] = pos;
        W($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {iw} /Height {ih} " +
          $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
        WBytes(jpeg);
        W("\nendstream\nendobj\n");

        long xref = pos;
        W("xref\n0 6\n");
        W("0000000000 65535 f\r\n");
        for (int i = 1; i <= 5; i++)
            W($"{offsets[i]:D10} 00000 n\r\n");
        W($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    // ── Hilfen ──────────────────────────────────────────────────────────────────

    private static (double left, double top) KartenPos(Sitzplatz p)
    {
        var g = p.Gruppe;
        if (!g.IstVertikal)
            return ((g.Spalte - 1) * (KARTE_B + LUECKE) + (p.PlatzNr - 1) * KARTE_B,
                    (g.Reihe - 1) * (KARTE_H + LUECKE));
        return ((g.Spalte - 1) * (KARTE_B + LUECKE),
                (g.Reihe - 1) * (KARTE_H + LUECKE) + (p.PlatzNr - 1) * KARTE_H);
    }

    // Farblogik identisch zu SitzplatzVM.Farbe
    private static string KartenFarbe(Sitzplatz p, Loesung loesung)
    {
        if (p.Schueler == null) return "#F5F5F5";
        var s = p.Schueler;
        bool P(Prioritaet prio) => loesung.Bewertungen.Any(b =>
            b.Schueler == s && !b.Erfuellt && b.Wunsch.Prio == prio);
        if (P(Prioritaet.Prio3)) return "#E74C3C";
        if (P(Prioritaet.Prio2)) return "#FAD7A0";
        if (P(Prioritaet.Prio1)) return "#FDEBD0";
        return "#D6E4F0";
    }

    private double Legende(DrawingContext dc, double x, double y, string hex, string text)
    {
        dc.DrawRoundedRectangle(Farbe(hex), new Pen(Farbe("#CCCCCC"), 0.5),
            new Rect(x, y - 11, 12, 12), 2, 2);
        var ft = Text(text, 10, Farbe("#333333"), FontWeights.Normal);
        dc.DrawText(ft, new Point(x + 16, y - 12));
        return x + 16 + ft.Width + 16;
    }

    private static FormattedText Text(
        string s, double size, Brush brush, FontWeight weight,
        double maxWidth = 0, int maxLines = 1)
    {
        var tf = new Typeface(FONT, FontStyles.Normal, weight, FontStretches.Normal);
        var ft = new FormattedText(s ?? "", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, tf, size, brush, 1.0);
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.MaxLineCount = maxLines;
            ft.Trimming     = TextTrimming.CharacterEllipsis;
            ft.TextAlignment = TextAlignment.Center;
        }
        return ft;
    }

    private static SolidColorBrush Farbe(string hex, double opacity = 1.0)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        var b = new SolidColorBrush(c) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    private static string KurzName(string vollName)
    {
        if (!vollName.Contains(',')) return vollName;
        var parts = vollName.Split(',');
        var nach  = parts[0].Trim();
        var vor   = parts[1].Trim();
        string nachKurz = nach.Length > 0 ? nach[0] + "." : "";
        return string.IsNullOrEmpty(vor) ? nach : $"{vor} {nachKurz}";
    }
}
