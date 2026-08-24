using ClosedXML.Excel;

namespace SitzplanApp.Services;

public class ExcelUpdateService
{
    public void AllesSpeichern(string pfad, List<Schueler> schueler, List<Tischgruppe> gruppen)
    {
        string tempPfad = pfad + "_save.xlsx";
        using (var wb = new XLWorkbook(pfad))
        {
            // ── Schüler ───────────────────────────────────────────────────────
            IXLWorksheet? wsS = null;
            foreach (var n in new[] { "Schueler", "Schüler", "Schüler" })
                if (wb.TryGetWorksheet(n, out wsS)) break;
            if (wsS != null)
            {
                var C_SUB    = XLColor.FromHtml("#2E75B6");
                var C_NN     = XLColor.FromHtml("#B03A2F");
                var C_ZM     = XLColor.FromHtml("#1E6B34");
                var C_ACCENT = XLColor.FromHtml("#D6E4F0");

                (int col, string text, XLColor bg)[] headers =
                {
                    (1,  "Nr.",                    C_SUB),
                    (2,  "Name",                   C_SUB),
                    (3,  "Gesch.\n(m/w)",          C_SUB),
                    (4,  "Seh-\nschwäche\n(ja/n)", C_SUB),
                    (5,  "Zusammen 1\n(Name)",     C_ZM),
                    (6,  "Prio\nZ1\n(1/2/3)",      C_ZM),
                    (7,  "Zusammen 2\n(Name)",     C_ZM),
                    (8,  "Prio\nZ2\n(1/2/3)",      C_ZM),
                    (9,  "Nicht neben 1\n(Name)",  C_NN),
                    (10, "Prio\nNN1\n(1/2/3)",     C_NN),
                    (11, "Nicht neben 2\n(Name)",  C_NN),
                    (12, "Prio\nNN2\n(1/2/3)",     C_NN),
                    (13, "Links-\nhänder\n(ja/n)", C_SUB),
                    (14, "Notizen",                C_SUB),
                    (15, "Fixierung\n(Gruppe/Pl)", C_SUB),
                    (16, "Verbote\n(Gruppe[/Pl])", XLColor.FromHtml("#7030A0")),
                };
                foreach (var (col, text, bg) in headers)
                {
                    var cell = wsS.Cell(2, col);
                    cell.Value = text;
                    cell.Style.Font.Bold            = true;
                    cell.Style.Font.FontColor       = XLColor.White;
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                    cell.Style.Alignment.WrapText   = true;
                }
                wsS.Row(2).Height = 46;

                for (int i = 0; i < schueler.Count; i++)
                {
                    var s   = schueler[i];
                    int row = i + 4;
                    var bg  = row % 2 == 0 ? C_ACCENT : XLColor.White;

                    void Set(int col, object? val, bool center = true)
                    {
                        var cell = wsS.Cell(row, col);
                        cell.Value = val?.ToString() ?? "";
                        cell.Style.Fill.BackgroundColor = bg;
                        cell.Style.Alignment.Horizontal = center
                            ? XLAlignmentHorizontalValues.Center
                            : XLAlignmentHorizontalValues.Left;
                        cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                        cell.Style.Font.FontSize        = 9;
                    }

                    Set(1,  s.Nr);
                    Set(2,  s.Name, center: false);
                    Set(3,  s.Geschlecht);
                    Set(4,  s.Sehschwaeche ? "ja" : "nein");
                    Set(5,  s.ZusammenMit.Count > 0 ? s.ZusammenMit[0].ZielName : "", center: false);
                    Set(6,  s.ZusammenMit.Count > 0 ? (int)s.ZusammenMit[0].Prio : (object)"");
                    Set(7,  s.ZusammenMit.Count > 1 ? s.ZusammenMit[1].ZielName : "", center: false);
                    Set(8,  s.ZusammenMit.Count > 1 ? (int)s.ZusammenMit[1].Prio : (object)"");
                    Set(9,  s.NichtNeben.Count > 0 ? s.NichtNeben[0].ZielName : "", center: false);
                    Set(10, s.NichtNeben.Count > 0 ? (int)s.NichtNeben[0].Prio : (object)"");
                    Set(11, s.NichtNeben.Count > 1 ? s.NichtNeben[1].ZielName : "", center: false);
                    Set(12, s.NichtNeben.Count > 1 ? (int)s.NichtNeben[1].Prio : (object)"");
                    Set(13, s.Linkshaender ? "ja" : "nein");
                    Set(14, s.Notizen, center: false);
                    Set(15, s.FixGruppenNamen.Count > 0
                        ? string.Join(";", s.FixGruppenNamen) +
                          (s.FixSitzplatzNr.HasValue && s.FixGruppenNamen.Count == 1
                              ? $"/{s.FixSitzplatzNr}" : "")
                        : "", center: false);
                    Set(16, s.Verbote.Count > 0
                        ? string.Join("; ", s.Verbote.Select(v => v.ToString()))
                        : "", center: false);
                    wsS.Row(row).Height = 18;
                }

                int[] widths = { 5, 22, 9, 11, 20, 10, 20, 10, 20, 10, 20, 10, 11, 20, 22, 28 };
                for (int c = 0; c < widths.Length; c++)
                    wsS.Column(c + 1).Width = widths[c];
            }

            // ── Tischgruppen ──────────────────────────────────────────────────
            if (gruppen.Count > 0)
            {
                IXLWorksheet? wsT = null;
                foreach (var n in new[] { "Tischgruppen", "tischgruppen" })
                    if (wb.TryGetWorksheet(n, out wsT)) break;
                if (wsT != null)
                {
                    for (int r = 5; r <= 200; r++)
                        for (int c = 1; c <= 9; c++)
                            wsT.Cell(r, c).Value = "";

                    int row = 5;
                    foreach (var g in gruppen.OrderBy(g => g.Nr))
                    {
                        wsT.Cell(row, 1).Value = g.Nr;
                        wsT.Cell(row, 2).Value = g.Name;
                        wsT.Cell(row, 3).Value = g.Reihe;
                        wsT.Cell(row, 4).Value = g.Spalte;
                        wsT.Cell(row, 5).Value = g.Sitzplaetze;
                        wsT.Cell(row, 9).Value = g.IstVertikal ? "vertikal" : "horizontal";
                        row++;
                    }
                }
            }

            wb.SaveAs(tempPfad);
        }
        System.IO.File.Delete(pfad);
        System.IO.File.Move(tempPfad, pfad);
    }

    // Alte Methoden für Kompatibilität
    public void AktualisierSchuelerSheet(string pfad, List<Schueler> schueler)
        => AllesSpeichern(pfad, schueler, new List<Tischgruppe>());

    public void AktualisierTischgruppenSheet(string pfad, List<Tischgruppe> gruppen)
        => AllesSpeichern(pfad, new List<Schueler>(), gruppen);
}

