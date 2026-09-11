using ClosedXML.Excel;

namespace SitzplanApp.Services;

public class ExcelExportService
{
    private static readonly XLColor C_HEADER = XLColor.FromHtml("#1F3864");
    private static readonly XLColor C_SUB    = XLColor.FromHtml("#2E75B6");
    private static readonly XLColor C_ACCENT = XLColor.FromHtml("#D6E4F0");
    private static readonly XLColor C_YELLOW = XLColor.FromHtml("#FFF2CC");
    private static readonly XLColor C_RED    = XLColor.FromHtml("#FCE4D6");
    private static readonly XLColor C_GREEN  = XLColor.FromHtml("#E2EFDA");

    public void ExportiereErgebnis(
        string pfad,
        OptimierungsErgebnis ergebnis,
        List<Tischgruppe> gruppen,
        List<Schueler> schueler)
    {
        // In die geladene Excel-Datei schreiben
        string tempPfad = pfad + "_export.xlsx";
        using (var wb = new XLWorkbook(pfad))
        {
            // Alte Export-Sheets entfernen falls vorhanden
            foreach (var name in new[]
            {
                "Lösung 1", "Lösung 2", "Lösung 3",
                "Protokoll L1", "Protokoll L2", "Protokoll L3",
                "Diagnose & Vergleich", "Klassenplan (Beste)"
            })
                if (wb.TryGetWorksheet(name, out var old))
                    old.Delete();

            // 3 Lösungen + Protokolle
            foreach (var l in ergebnis.Loesungen)
            {
                ErstelleKlassenplan(wb, l, gruppen, $"Lösung {l.Index}");
                ErstelleProtokoll(wb, l);
            }

            ErstelleDiagnose(wb, ergebnis);
            wb.SaveAs(tempPfad);
        }

        System.IO.File.Delete(pfad);
        System.IO.File.Move(tempPfad, pfad);
    }

    /// <summary>
    /// Zeichnet den visuellen Klassenplan (Karten-Raster) einer Lösung in ein
    /// neues Blatt <paramref name="sheetName"/>. Wird sowohl für die 3 Lösungen
    /// als auch für das manuelle Speichern („Sitzplan (Gespeichert)") genutzt.
    /// Ein evtl. gleichnamiges Blatt wird zuvor entfernt.
    /// </summary>
    public void ErstelleKlassenplan(
        XLWorkbook wb, Loesung loesung,
        List<Tischgruppe> gruppen, string sheetName)
    {
        if (wb.TryGetWorksheet(sheetName, out var alt)) alt.Delete();
        var ws = wb.Worksheets.Add(sheetName);
        ws.ShowGridLines = false;

        // Titel
        Merge(ws, $"A1:P1", $"SITZPLAN – {loesung.Bezeichnung.ToUpper()}  |  Score: {loesung.GesamtScore:F0}");
        ws.Cell("A1").Style.Font.FontColor = XLColor.White;
        ws.Cell("A1").Style.Fill.BackgroundColor = loesung.Prio3Vollstaendig ? C_HEADER : XLColor.FromHtml("#8B0000");
        ws.Row(1).Height = 28;

        // Score-Zeile
        Merge(ws, "A2:P2",
            $"P3: {loesung.Prio3Erfuellt}/{loesung.Prio3Gesamt} erfüllt  |  " +
            $"P2: {loesung.Prio2Erfuellt}/{loesung.Prio2Gesamt}  |  " +
            $"P1: {loesung.Prio1Erfuellt}/{loesung.Prio1Gesamt}  |  " +
            $"Sehschwäche vorne: {loesung.SehschwaeacheVorne}/{loesung.SehschwaeacheGesamt}  |  " +
            $"Geschlecht: {loesung.GeschlechterScore:P0}");
        ws.Cell("A2").Style.Fill.BackgroundColor = C_YELLOW;
        ws.Row(2).Height = 18;

        // Tafel
        Merge(ws, "D4:M4", "TAFEL / VORNE");
        ws.Cell("D4").Style.Fill.BackgroundColor = C_HEADER;
        ws.Cell("D4").Style.Font.FontColor = XLColor.White;
        ws.Cell("D4").Style.Font.Bold = true;

        const int ROW_OFF = 5;
        const int COL_OFF = 1;
        const double KARTE  = 122.0;
        const double LUECKE = 20.0;

        foreach (var gruppe in gruppen)
        {
            int baseRow = gruppe.Reihe + ROW_OFF;
            int baseCol = gruppe.Spalte + COL_OFF;

            var plaetze = loesung.Sitzplaetze
                .Where(p => p.Gruppe.Name == gruppe.Name).OrderBy(p => p.PlatzNr).ToList();

            for (int i = 0; i < plaetze.Count; i++)
            {
                var platz = plaetze[i];
                // Horizontal: Plätze nebeneinander (Spalte wächst)
                // Vertikal:   Plätze hintereinander (Zeile wächst)
                int row = gruppe.IstVertikal ? baseRow + i : baseRow;
                int col = gruppe.IstVertikal ? baseCol     : baseCol + i;

                var cell = ws.Cell(row, col);
                ws.Row(row).Height = 44;
                ws.Column(col).Width = 15;

                var s = platz.Schueler;
                if (s != null)
                {
                    string kurzName = KurzName(s.Name);
                    cell.Value = kurzName + (s.Geschlecht != "" ? $"\n({s.Geschlecht})" : "");

                    // Farbe: Verletzungen rot markieren
                    bool hatVerletzung = loesung.Bewertungen
                        .Any(b => b.Schueler == s && !b.Erfuellt && b.Wunsch.Prio >= Prioritaet.Prio2);

                    var bg = hatVerletzung ? C_RED
                           : s.Sehschwaeche ? XLColor.FromHtml("#FCE4D6")
                           : s.FixGruppenname != null ? XLColor.FromHtml("#E8D5FB")
                           : XLColor.FromHtml(string.IsNullOrEmpty(gruppe.Farbe)
                               ? "#D6E4F0" : "#" + gruppe.Farbe.TrimStart('#'));

                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.Bold = true;
                    if (s.FixGruppenname != null) cell.Style.Font.Italic = true;
                }
                else
                {
                    cell.Value = "[frei]";
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    cell.Style.Font.FontColor = XLColor.Gray;
                }

                cell.Style.Font.FontName = "Arial";
                cell.Style.Font.FontSize = 8;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText    = true;
                cell.Style.Border.OutsideBorder  = XLBorderStyleValues.Medium;
                cell.Style.Border.OutsideBorderColor = C_HEADER;
            }
        }

        // Warnungen
        if (loesung.Warnungen.Count > 0)
        {
            int wRow = gruppen.Max(g => g.Reihe) + ROW_OFF + 3;
            ws.Cell(wRow, 1).Value = "Warnungen:";
            ws.Cell(wRow, 1).Style.Font.Bold = true;
            ws.Cell(wRow, 1).Style.Font.FontColor = XLColor.Red;
            foreach (var w in loesung.Warnungen)
            {
                wRow++;
                ws.Range(wRow, 1, wRow, 10).Merge().Value = w;
                ws.Cell(wRow, 1).Style.Fill.BackgroundColor = C_RED;
                ws.Cell(wRow, 1).Style.Font.FontSize = 9;
            }
        }
    }

    private void ErstelleProtokoll(XLWorkbook wb, Loesung loesung)
    {
        var ws = wb.Worksheets.Add($"Protokoll L{loesung.Index}");
        ws.ShowGridLines = false;

        Merge(ws, "A1:G1", $"PROTOKOLL – {loesung.Bezeichnung}");
        ws.Cell("A1").Style.Fill.BackgroundColor = C_HEADER;
        ws.Cell("A1").Style.Font.FontColor = XLColor.White;
        ws.Cell("A1").Style.Font.Bold = true;

        string[] headers = { "Nr.", "Schüler", "Gruppe", "Platz", "Erfüllte Wünsche", "Verletzte Wünsche", "Fix" };
        int[]    widths  = {    5,      22,       18,       8,           30,                    30,             8  };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(2, c + 1).Value = headers[c];
            ws.Cell(2, c + 1).Style.Fill.BackgroundColor = C_SUB;
            ws.Cell(2, c + 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(2, c + 1).Style.Font.Bold = true;
            ws.Column(c + 1).Width = widths[c];
        }

        int row = 3;
        foreach (var zeile in loesung.Protokoll)
        {
            var parts = zeile.Split('|');
            var bg = row % 2 == 0 ? C_ACCENT : XLColor.White;
            // Zeilen mit verletzten Wünschen rot
            if (parts.Length > 5 && !string.IsNullOrEmpty(parts[5]))
                bg = C_RED;

            for (int c = 0; c < Math.Min(parts.Length, 7); c++)
            {
                ws.Cell(row, c + 1).Value = parts[c].Trim();
                ws.Cell(row, c + 1).Style.Fill.BackgroundColor = bg;
                ws.Cell(row, c + 1).Style.Font.FontSize = 9;
            }
            row++;
        }
    }

    private void ErstelleDiagnose(XLWorkbook wb, OptimierungsErgebnis ergebnis)
    {
        var ws = wb.Worksheets.Add("Diagnose & Vergleich");
        ws.ShowGridLines = false;

        Merge(ws, "A1:H1", "DIAGNOSE & LÖSUNGSVERGLEICH");
        ws.Cell("A1").Style.Fill.BackgroundColor = C_HEADER;
        ws.Cell("A1").Style.Font.FontColor = XLColor.White;
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Row(1).Height = 28;

        // Header
        var headers = new[]
        {
            "Kriterium", "Lösung 1", "Lösung 2", "Lösung 3", "Beste"
        };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(2, c + 1).Value = headers[c];
            ws.Cell(2, c + 1).Style.Fill.BackgroundColor = C_SUB;
            ws.Cell(2, c + 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(2, c + 1).Style.Font.Bold = true;
        }
        ws.Column(1).Width = 30;
        for (int c = 2; c <= 5; c++) ws.Column(c).Width = 18;

        var beste = ergebnis.BesteLoesung;
        var loesungen = ergebnis.Loesungen;

        var zeilen = new[]
        {
            ("Gesamtscore",
                loesungen.Select(l => l.GesamtScore.ToString("F0")).ToArray(),
                beste?.GesamtScore.ToString("F0") ?? "-"),
            ("Prio-3 erfüllt",
                loesungen.Select(l => $"{l.Prio3Erfuellt}/{l.Prio3Gesamt}").ToArray(),
                beste != null ? $"{beste.Prio3Erfuellt}/{beste.Prio3Gesamt}" : "-"),
            ("Prio-2 erfüllt",
                loesungen.Select(l => $"{l.Prio2Erfuellt}/{l.Prio2Gesamt}").ToArray(),
                beste != null ? $"{beste.Prio2Erfuellt}/{beste.Prio2Gesamt}" : "-"),
            ("Prio-1 erfüllt",
                loesungen.Select(l => $"{l.Prio1Erfuellt}/{l.Prio1Gesamt}").ToArray(),
                beste != null ? $"{beste.Prio1Erfuellt}/{beste.Prio1Gesamt}" : "-"),
            ("Sehschwäche vorne",
                loesungen.Select(l => $"{l.SehschwaeacheVorne}/{l.SehschwaeacheGesamt}").ToArray(),
                beste != null ? $"{beste.SehschwaeacheVorne}/{beste.SehschwaeacheGesamt}" : "-"),
            ("Geschlechterverteilung",
                loesungen.Select(l => l.GeschlechterScore.ToString("P0")).ToArray(),
                beste?.GeschlechterScore.ToString("P0") ?? "-"),
            ("Prio-3 vollständig",
                loesungen.Select(l => l.Prio3Vollstaendig ? "JA" : "NEIN").ToArray(),
                beste != null ? (beste.Prio3Vollstaendig ? "JA" : "NEIN") : "-"),
            ("Warnungen",
                loesungen.Select(l => l.Warnungen.Count.ToString()).ToArray(),
                beste?.Warnungen.Count.ToString() ?? "-"),
        };

        for (int r = 0; r < zeilen.Length; r++)
        {
            int excelRow = r + 3;
            var (label, werte, bestWert) = zeilen[r];
            var bgRow = r % 2 == 0 ? C_ACCENT : XLColor.White;

            ws.Cell(excelRow, 1).Value = label;
            ws.Cell(excelRow, 1).Style.Fill.BackgroundColor = bgRow;
            ws.Cell(excelRow, 1).Style.Font.Bold = true;

            for (int c = 0; c < Math.Min(werte.Length, 3); c++)
            {
                var cell = ws.Cell(excelRow, c + 2);
                cell.Value = werte[c];
                cell.Style.Fill.BackgroundColor = bgRow;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Beste Lösung hervorheben
                if (beste != null && loesungen[c] == beste)
                    cell.Style.Fill.BackgroundColor = C_GREEN;
                // Prio-3 NEIN rot
                if (label == "Prio-3 vollständig" && werte[c] == "NEIN")
                    cell.Style.Fill.BackgroundColor = C_RED;
            }

            ws.Cell(excelRow, 5).Value = bestWert;
            ws.Cell(excelRow, 5).Style.Fill.BackgroundColor = C_GREEN;
            ws.Cell(excelRow, 5).Style.Font.Bold = true;
            ws.Cell(excelRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Detail-Bewertung pro Wunsch
        int detailRow = zeilen.Length + 5;
        Merge(ws, $"A{detailRow}:H{detailRow}", "WUNSCH-DETAIL (Beste Lösung)");
        ws.Cell(detailRow, 1).Style.Fill.BackgroundColor = C_SUB;
        ws.Cell(detailRow, 1).Style.Font.FontColor = XLColor.White;
        ws.Cell(detailRow, 1).Style.Font.Bold = true;
        detailRow++;

        if (beste != null)
        {
            string[] dHeaders = { "Schüler", "Wunsch", "Typ", "Prio", "Erfüllt", "Abstand" };
            int[]    dWidths  = {      22,      22,       12,     6,      10,         10     };
            for (int c = 0; c < dHeaders.Length; c++)
            {
                ws.Cell(detailRow, c + 1).Value = dHeaders[c];
                ws.Cell(detailRow, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#444444");
                ws.Cell(detailRow, c + 1).Style.Font.FontColor = XLColor.White;
                ws.Column(c + 1).Width = dWidths[c];
            }
            detailRow++;

            foreach (var bw in beste.Bewertungen.OrderBy(b => b.Schueler.Name))
            {
                var bg = bw.Erfuellt ? C_GREEN : (bw.Wunsch.Prio >= Prioritaet.Prio2 ? C_RED : C_YELLOW);
                string[] vals =
                {
                    bw.Schueler.Name,
                    bw.Wunsch.ZielName,
                    bw.Wunsch.IstAntiWunsch ? "Nicht neben" : "Zusammen mit",
                    $"Prio {(int)bw.Wunsch.Prio}",
                    bw.Erfuellt ? "JA" : "NEIN",
                    bw.Abstand < 0 ? "n/a" : bw.Abstand.ToString(),
                };
                for (int c = 0; c < vals.Length; c++)
                {
                    ws.Cell(detailRow, c + 1).Value = vals[c];
                    ws.Cell(detailRow, c + 1).Style.Fill.BackgroundColor = bg;
                    ws.Cell(detailRow, c + 1).Style.Font.FontSize = 9;
                }
                detailRow++;
            }
        }
    }

    private static void Merge(IXLWorksheet ws, string range, string value)
    {
        ws.Range(range).Merge().Value = value;
        ws.Cell(range.Split(':')[0]).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(range.Split(':')[0]).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
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
