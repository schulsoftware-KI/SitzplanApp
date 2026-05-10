using ClosedXML.Excel;

namespace SitzplanApp.Services;

public class ExcelImportService
{
    public (List<Schueler> schueler, List<Tischgruppe> gruppen, ParameterSet parameter) LadeExcel(string pfad)
    {
        using var wb  = new XLWorkbook(pfad);
        var schueler  = LadeSchueler(wb);
        var gruppen   = LadeGruppen(wb);
        var parameter = LadeParameter(wb);
        return (schueler, gruppen, parameter);
    }

    private ParameterSet LadeParameter(XLWorkbook wb)
    {
        var p = ParameterSet.Standard();
        if (!wb.TryGetWorksheet("Parameter", out var ws)) return p;

        for (int row = 4; row <= 30; row++)
        {
            var id  = ws.Cell(row, 1).GetString().Trim();
            var val = ws.Cell(row, 2).GetString().Trim();
            if (string.IsNullOrEmpty(id) || !double.TryParse(
                val, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                continue;

            switch (id)
            {
                case "W_PRIO2_ERF":    p.W_PRIO2_ERF    = v; break;
                case "W_PRIO2_STRAF":  p.W_PRIO2_STRAF  = v; break;
                case "W_PRIO1_ERF":    p.W_PRIO1_ERF    = v; break;
                case "W_PRIO1_STRAF":  p.W_PRIO1_STRAF  = v; break;
                case "W_SEH_VORNE":    p.W_SEH_VORNE    = v; break;
                case "W_GESCHLECHT":   p.W_GESCHLECHT   = v; break;
                case "W_LINKSHAENDER": p.W_LINKSHAENDER = v; break;
            }
        }
        return p;
    }

    // ── Schüler ──────────────────────────────────────────────────────────────
    private List<Schueler> LadeSchueler(XLWorkbook wb)
    {
        var liste = new List<Schueler>();
        IXLWorksheet? ws = null;
        foreach (var n in new[] { "Schueler", "Schüler", "Schüler" })
            if (wb.TryGetWorksheet(n, out ws)) break;
        if (ws == null) throw new Exception("Sheet 'Schueler' nicht gefunden.");

        for (int row = 4; row <= 120; row++)
        {
            var name = ws.Cell(row, 2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var s = new Schueler
            {
                Nr           = liste.Count + 1,
                Name         = name,
                Geschlecht   = ws.Cell(row, 3).GetString().Trim().ToLower(),
                Sehschwaeche = IsJa(ws.Cell(row, 4)),
                // Spalte M = Linkshänder, N = Notizen, O = Fixierung
                Linkshaender = IsJa(ws.Cell(row, 13)),
                Notizen      = ws.Cell(row, 14).GetString().Trim(),
            };

            // Spalte E: Zusammen 1, F: Prio Z1
            var zm1Name = ws.Cell(row, 5).GetString().Trim();
            var zm1Prio = ParsePrio(ws.Cell(row, 6));
            if (!string.IsNullOrEmpty(zm1Name))
                s.ZusammenMit.Add(new Wunsch
                {
                    ZielName      = zm1Name,
                    Prio          = zm1Prio,
                    IstAntiWunsch = false
                });

            // Spalte G: Zusammen 2, H: Prio Z2
            var zm2Name = ws.Cell(row, 7).GetString().Trim();
            var zm2Prio = ParsePrio(ws.Cell(row, 8));
            if (!string.IsNullOrEmpty(zm2Name))
                s.ZusammenMit.Add(new Wunsch
                {
                    ZielName      = zm2Name,
                    Prio          = zm2Prio,
                    IstAntiWunsch = false
                });

            // Spalte I: Nicht neben 1, J: Prio NN1
            var nn1Name = ws.Cell(row, 9).GetString().Trim();
            var nn1Prio = ParsePrio(ws.Cell(row, 10));
            if (!string.IsNullOrEmpty(nn1Name))
                s.NichtNeben.Add(new Wunsch
                {
                    ZielName      = nn1Name,
                    Prio          = nn1Prio,
                    IstAntiWunsch = true
                });

            // Spalte K: Nicht neben 2, L: Prio NN2
            var nn2Name = ws.Cell(row, 11).GetString().Trim();
            var nn2Prio = ParsePrio(ws.Cell(row, 12));
            if (!string.IsNullOrEmpty(nn2Name))
                s.NichtNeben.Add(new Wunsch
                {
                    ZielName      = nn2Name,
                    Prio          = nn2Prio,
                    IstAntiWunsch = true
                });

            // Spalte O: Fixierung (Format: "Gruppe A" oder "Gruppe A;Gruppe B" oder "Gruppe A/2")
            var fix = ws.Cell(row, 15).GetString().Trim();
            if (!string.IsNullOrEmpty(fix))
            {
                // Mehrere Gruppen durch Semikolon getrennt
                var gruppenTeile = fix.Split(';')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                foreach (var teil in gruppenTeile)
                {
                    var parts = teil.Split('/');
                    string gruppenName = parts[0].Trim();
                    s.FixGruppenNamen.Add(gruppenName);
                    // Platz-Nr nur bei genau einer Gruppe
                    if (gruppenTeile.Count == 1 && parts.Length > 1
                        && int.TryParse(parts[1].Trim(), out int nr))
                        s.FixSitzplatzNr = nr;
                }
            }

            // Spalte P: Verbote (Format: "Gruppe A" oder "Gruppe A/2; Gruppe B/1")
            var verboteRaw = ws.Cell(row, 16).GetString().Trim();
            if (!string.IsNullOrEmpty(verboteRaw))
            {
                foreach (var token in verboteRaw.Split(';')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t)))
                    s.Verbote.Add(PlatzVerbot.Parse(token));
            }

            liste.Add(s);
        }
        return liste;
    }

    // ── Tischgruppen ─────────────────────────────────────────────────────────
    private List<Tischgruppe> LadeGruppen(XLWorkbook wb)
    {
        var liste = new List<Tischgruppe>();
        if (!wb.TryGetWorksheet("Tischgruppen", out var ws))
            throw new Exception("Sheet 'Tischgruppen' nicht gefunden.");

        for (int row = 5; row <= 100; row++)
        {
            var nrStr = ws.Cell(row, 1).GetString().Trim();
            if (!int.TryParse(nrStr, out int nr)) continue;
            var sitzStr = ws.Cell(row, 5).GetString().Trim();
            if (!int.TryParse(sitzStr, out int sitze) || sitze <= 0) continue;

            liste.Add(new Tischgruppe
            {
                Nr          = nr,
                Name        = ws.Cell(row, 2).GetString().Trim(),
                Reihe       = int.TryParse(ws.Cell(row, 3).GetString(), out int r) ? r : 1,
                Spalte      = int.TryParse(ws.Cell(row, 4).GetString(), out int sp) ? sp : 1,
                Sitzplaetze = sitze,
                Form        = ws.Cell(row, 6).GetString().Trim(),
                Notizen     = ws.Cell(row, 7).GetString().Trim(),
                Farbe       = ws.Cell(row, 8).GetString().Trim(),
                // "horizontal" in Excel = Tische stehen horizontal = Plätze nebeneinander
                // "vertikal"   in Excel = Tische stehen vertikal   = Plätze hintereinander (untereinander)
                IstVertikal = ws.Cell(row, 9).GetString().Trim()
                                 .Equals("vertikal", StringComparison.OrdinalIgnoreCase),
            });
        }
        return liste;
    }

    private static bool IsJa(IXLCell cell) =>
        cell.GetString().Trim().Equals("ja", StringComparison.OrdinalIgnoreCase);

    private static Prioritaet ParsePrio(IXLCell cell)
    {
        var v = cell.GetString().Trim();
        return v switch
        {
            "1" => Prioritaet.Prio1,
            "2" => Prioritaet.Prio2,
            "3" => Prioritaet.Prio3,
            _   => Prioritaet.Prio1,
        };
    }
}
