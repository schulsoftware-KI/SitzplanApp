using ClosedXML.Excel;

namespace SitzplanApp.Services;

/// <summary>
/// Sichert eine vom Nutzer gewählte Lösung (Sitzplatz-Belegung) in einem
/// dedizierten Blatt "Gespeicherte Lösung" der geladenen Excel-Datei und
/// rekonstruiert sie beim nächsten Laden wieder.
///
/// Gespeichert wird nur die reine Zuordnung Schüler → Gruppe/Platz (per Name).
/// Schüler-, Gruppen- und Parameterdaten werden weiterhin aus ihren eigenen
/// Blättern geladen; die gespeicherte Lösung wird beim Laden darauf angewandt.
/// </summary>
public class GespeicherteLoesungService
{
    private const string SHEET = "Gespeicherte Lösung";

    /// <summary>Eine Zeile der gespeicherten Belegung.</summary>
    public record Belegung(string Gruppe, int PlatzNr, string Schueler);

    // ── Speichern ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Schreibt (bzw. überschreibt) das Blatt "Gespeicherte Lösung" mit der
    /// übergebenen Belegung. Nur belegte Plätze werden gespeichert.
    /// </summary>
    public void Speichern(
        string pfad,
        string bezeichnung,
        double score,
        List<Belegung> belegung)
    {
        var C_HEADER = XLColor.FromHtml("#1F3864");
        var C_SUB    = XLColor.FromHtml("#2E75B6");
        var C_ACCENT = XLColor.FromHtml("#D6E4F0");

        string tempPfad = pfad + "_loesung.xlsx";
        using (var wb = new XLWorkbook(pfad))
        {
            if (wb.TryGetWorksheet(SHEET, out var alt)) alt.Delete();
            var ws = wb.Worksheets.Add(SHEET);
            ws.ShowGridLines = false;

            // Titelzeile
            ws.Range("A1:C1").Merge().Value = "GESPEICHERTE LÖSUNG";
            ws.Cell("A1").Style.Fill.BackgroundColor = C_HEADER;
            ws.Cell("A1").Style.Font.FontColor = XLColor.White;
            ws.Cell("A1").Style.Font.Bold = true;
            ws.Row(1).Height = 22;

            // Metadaten (Zeile 2) — dienen nur der Information des Nutzers.
            // Maschinenlesbare Marker in Spalte A, Werte in Spalte B.
            ws.Cell(2, 1).Value = "Bezeichnung";
            ws.Cell(2, 2).Value = bezeichnung;
            ws.Cell(3, 1).Value = "Score";
            ws.Cell(3, 2).Value = score;
            ws.Cell(4, 1).Value = "Gespeichert am";
            ws.Cell(4, 2).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            for (int r = 2; r <= 4; r++)
                ws.Cell(r, 1).Style.Font.Bold = true;

            // Kopfzeile der Belegungstabelle (Zeile 6)
            const int KOPF = 6;
            string[] headers = { "Gruppe", "Platz", "Schüler" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(KOPF, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = C_SUB;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.Bold = true;
            }
            ws.Column(1).Width = 22;
            ws.Column(2).Width = 8;
            ws.Column(3).Width = 28;

            int row = KOPF + 1;
            foreach (var b in belegung
                         .Where(b => !string.IsNullOrWhiteSpace(b.Schueler))
                         .OrderBy(b => b.Gruppe).ThenBy(b => b.PlatzNr))
            {
                var bg = row % 2 == 0 ? C_ACCENT : XLColor.White;
                ws.Cell(row, 1).Value = b.Gruppe;
                ws.Cell(row, 2).Value = b.PlatzNr;
                ws.Cell(row, 3).Value = b.Schueler;
                for (int c = 1; c <= 3; c++)
                {
                    ws.Cell(row, c).Style.Fill.BackgroundColor = bg;
                    ws.Cell(row, c).Style.Font.FontSize = 10;
                }
                row++;
            }

            wb.SaveAs(tempPfad);
        }

        System.IO.File.Delete(pfad);
        System.IO.File.Move(tempPfad, pfad);
    }

    // ── Prüfen ────────────────────────────────────────────────────────────────
    /// <summary>True, wenn die Datei eine gespeicherte Lösung enthält.</summary>
    public bool HatGespeicherteLoesung(string pfad)
    {
        try
        {
            using var wb = new XLWorkbook(pfad);
            return wb.TryGetWorksheet(SHEET, out _);
        }
        catch
        {
            return false;
        }
    }

    // ── Laden ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Liest das Blatt "Gespeicherte Lösung" und rekonstruiert eine <see cref="Loesung"/>,
    /// indem die gespeicherten Schüler auf frische Sitzplätze der aktuellen Gruppen
    /// gesetzt werden. Gibt null zurück, wenn kein Blatt vorhanden ist.
    /// Die Bewertung (Scores/Verletzungen) muss der Aufrufer per
    /// <c>OptimierungsService.BewerteManuell</c> ergänzen.
    /// </summary>
    /// <param name="hinweise">
    /// Sammelt Hinweise auf Unstimmigkeiten (unbekannte Namen/Gruppen/Plätze),
    /// damit der Aufrufer sie NACH der Bewertung als Warnungen anhängen kann
    /// (BewerteManuell leert die Warnungsliste).
    /// </param>
    public Loesung? Laden(
        string pfad,
        List<Tischgruppe> gruppen,
        List<Schueler> schueler,
        out List<string> hinweise)
    {
        hinweise = new List<string>();

        using var wb = new XLWorkbook(pfad);
        if (!wb.TryGetWorksheet(SHEET, out var ws)) return null;

        // Frische Sitzplätze für alle aktuellen Gruppen aufbauen.
        var plaetze = new List<Sitzplatz>();
        foreach (var g in gruppen.OrderBy(g => g.Reihe).ThenBy(g => g.Spalte))
            for (int p = 1; p <= g.Sitzplaetze; p++)
                plaetze.Add(new Sitzplatz { Gruppe = g, PlatzNr = p });

        string bezeichnung = ws.Cell(2, 2).GetString().Trim();
        if (string.IsNullOrEmpty(bezeichnung)) bezeichnung = "Gespeicherte Lösung";

        var loesung = new Loesung
        {
            Index       = 0,
            Bezeichnung = bezeichnung,
            Sitzplaetze = plaetze,
        };

        var belegt = new HashSet<string>(); // bereits gesetzte Schülernamen

        // Datenzeilen ab Zeile 7 lesen (Kopf = Zeile 6).
        for (int row = 7; row <= 400; row++)
        {
            var gruppenName = ws.Cell(row, 1).GetString().Trim();
            var platzStr    = ws.Cell(row, 2).GetString().Trim();
            var name        = ws.Cell(row, 3).GetString().Trim();

            if (string.IsNullOrWhiteSpace(gruppenName) &&
                string.IsNullOrWhiteSpace(name))
                continue;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!int.TryParse(platzStr, out int platzNr)) continue;

            var s = schueler.FirstOrDefault(x =>
                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (s == null)
            {
                hinweise.Add($"Gespeicherte Lösung: Schüler „{name}“ nicht mehr vorhanden – übersprungen.");
                continue;
            }

            var platz = plaetze.FirstOrDefault(p =>
                p.Gruppe.Name.Equals(gruppenName, StringComparison.OrdinalIgnoreCase) &&
                p.PlatzNr == platzNr);
            if (platz == null)
            {
                hinweise.Add($"Gespeicherte Lösung: Platz „{gruppenName}/{platzNr}“ für {name} existiert nicht mehr – übersprungen.");
                continue;
            }
            if (!platz.IstFrei)
            {
                hinweise.Add($"Gespeicherte Lösung: Platz „{gruppenName}/{platzNr}“ doppelt belegt – {name} übersprungen.");
                continue;
            }

            platz.Schueler = s;
            belegt.Add(s.Name);
        }

        // Schüler, die in der gespeicherten Lösung fehlen (z. B. neu hinzugekommen).
        foreach (var s in schueler)
            if (!belegt.Contains(s.Name))
                hinweise.Add($"Gespeicherte Lösung: {s.Name} hatte keinen gespeicherten Platz – bleibt unplatziert.");

        return loesung;
    }
}
