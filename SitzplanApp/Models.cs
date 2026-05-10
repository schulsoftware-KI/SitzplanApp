namespace SitzplanApp.Models;

// ── Priorität ────────────────────────────────────────────────────────────────
public enum Prioritaet
{
    Keine  = 0,
    Prio1  = 1,   // berücksichtigt wenn möglich
    Prio2  = 2,   // stark bevorzugt, bei Swap-Optimierung bevorzugt
    Prio3  = 3,   // zwingend – Verletzung verboten
}

// ── Wunsch/Antiwunsch ────────────────────────────────────────────────────────
public class Wunsch
{
    public string ZielName   { get; set; } = "";   // Name des anderen Schülers
    public Prioritaet Prio   { get; set; } = Prioritaet.Prio1;
    public bool IstAntiWunsch { get; set; }        // true = Nicht neben, false = Zusammen mit

    public override string ToString() =>
        $"{(IstAntiWunsch ? "≠" : "=")} {ZielName} [P{(int)Prio}]";
}

// ── Schüler ──────────────────────────────────────────────────────────────────
public class Schueler
{
    public int    Nr          { get; set; }
    public string Name        { get; set; } = "";
    public string Geschlecht  { get; set; } = "";   // "m" | "w" | ""
    public bool   Sehschwaeche { get; set; }
    public bool   Linkshaender { get; set; }
    public string Notizen     { get; set; } = "";

    // Bis zu 2 Nicht-neben-Wünsche (mit je eigener Prio)
    public List<Wunsch> NichtNeben   { get; set; } = new();

    // Bis zu 2 Zusammen-mit-Wünsche (mit je eigener Prio)
    public List<Wunsch> ZusammenMit  { get; set; } = new();

    // Fixierung — mehrere Gruppen möglich (Oder-Logik), kein Platz bei >1 Gruppe
    public List<string> FixGruppenNamen  { get; set; } = new();
    public int?         FixSitzplatzNr  { get; set; }   // nur bei genau 1 Gruppe

    // Kompatibilität: erster Gruppenname oder null
    public string? FixGruppenname =>
        FixGruppenNamen.Count > 0 ? FixGruppenNamen[0] : null;

    // Alle Wünsche zusammen (für einfache Iteration)
    public IEnumerable<Wunsch> AlleWuensche =>
        NichtNeben.Concat(ZusammenMit);

    // Verbote: bestimmte Gruppen oder einzelne Plätze sind für diesen Schüler gesperrt
    public List<PlatzVerbot> Verbote { get; set; } = new();

    // Helper für XAML-Binding
    public bool   HatVerbote  => Verbote.Count > 0;
    public string VerboteText => Verbote.Count > 0
        ? string.Join("  |  ", Verbote.Select(v => v.ToString()))
        : "";

    public override string ToString() => Name;
}

// ── Platzverbot ───────────────────────────────────────────────────────────────
/// <summary>
/// Verbietet einem Schüler eine ganze Gruppe oder einen einzelnen Platz darin.
/// GruppenName: Name der Tischgruppe (Pflicht).
/// PlatzNr: null = gesamte Gruppe verboten; 1..n = nur dieser Platz verboten.
/// Excel-Serialisierungsformat: "Gruppe A" oder "Gruppe A/2" (analog Fixierung),
/// mehrere Verbote durch Semikolon getrennt.
/// </summary>
public class PlatzVerbot
{
    public string GruppenName { get; set; } = "";
    public int?   PlatzNr     { get; set; }      // null = ganze Gruppe

    public override string ToString() =>
        PlatzNr.HasValue ? $"{GruppenName}/{PlatzNr}" : GruppenName;

    /// <summary>Parst einen Eintrag wie "Gruppe A" oder "Gruppe A/2".</summary>
    public static PlatzVerbot Parse(string token)
    {
        var parts = token.Trim().Split('/');
        var v = new PlatzVerbot { GruppenName = parts[0].Trim() };
        if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int nr))
            v.PlatzNr = nr;
        return v;
    }

    /// <summary>Prüft ob ein konkreter Platz durch dieses Verbot gesperrt ist.</summary>
    public bool TrifftZu(string gruppenName, int platzNr) =>
        GruppenName.Equals(gruppenName, StringComparison.OrdinalIgnoreCase) &&
        (!PlatzNr.HasValue || PlatzNr.Value == platzNr);
}

// ── Tischgruppe ──────────────────────────────────────────────────────────────
public class Tischgruppe
{
    public int    Nr          { get; set; }
    public string Name        { get; set; } = "";
    public int    Reihe       { get; set; }   // Rasterposition (1=vorne)
    public int    Spalte      { get; set; }   // Rasterposition (1=links)
    public int    Sitzplaetze { get; set; }
    public string Form        { get; set; } = "";
    public string Farbe       { get; set; } = "";
    public string Notizen     { get; set; } = "";
    public bool   IstVertikal { get; set; }   // true = Plätze untereinander

    /// <summary>
    /// Gibt die Raster-Koordinate (RasterX, RasterY) eines einzelnen Platzes zurück.
    /// Horizontal: Plätze nebeneinander → X wächst mit PlatzNr
    /// Vertikal:   Plätze untereinander → Y wächst mit PlatzNr
    /// Einheit: 1 Einheit = 1 Tischplatz-Breite
    /// </summary>
    public (double X, double Y) PlatzKoordinate(int platzNr)
    {
        if (!IstVertikal)
            return (Spalte + (platzNr - 1), Reihe);      // horizontal: X wächst
        else
            return (Spalte, Reihe + (platzNr - 1) * 0.9); // vertikal: Y wächst
    }

    /// <summary>
    /// Euklidischer Abstand zwischen zwei einzelnen Plätzen.
    /// Ersetzt den alten Gruppen-Abstand komplett.
    /// </summary>
    public static double PlatzAbstand(
        Tischgruppe gA, int platzA,
        Tischgruppe gB, int platzB)
    {
        var (xA, yA) = gA.PlatzKoordinate(platzA);
        var (xB, yB) = gB.PlatzKoordinate(platzB);
        return Math.Sqrt(Math.Pow(xA - xB, 2) + Math.Pow(yA - yB, 2));
    }

    /// <summary>
    /// Minimaler Abstand zwischen irgendeinem Platz von Gruppe A
    /// und irgendeinem Platz von Gruppe B.
    /// Wird für die Scoring-Funktion verwendet.
    /// </summary>
    public static double MinAbstand(Tischgruppe gA, Tischgruppe gB)
    {
        double min = double.MaxValue;
        for (int a = 1; a <= gA.Sitzplaetze; a++)
            for (int b = 1; b <= gB.Sitzplaetze; b++)
            {
                double d = PlatzAbstand(gA, a, gB, b);
                if (d < min) min = d;
            }
        return min;
    }
}

// ── Sitzplatz ────────────────────────────────────────────────────────────────
public class Sitzplatz
{
    public Tischgruppe Gruppe   { get; set; } = null!;
    public int         PlatzNr  { get; set; }
    public Schueler?   Schueler { get; set; }
    public bool        IstFrei  => Schueler == null;

    // Rasterkoordinaten dieses Platzes
    public double RasterX => Gruppe.PlatzKoordinate(PlatzNr).X;
    public double RasterY => Gruppe.PlatzKoordinate(PlatzNr).Y;

    /// <summary>Euklidischer Abstand zu einem anderen Sitzplatz.</summary>
    public double AbstandZu(Sitzplatz other) =>
        Math.Sqrt(Math.Pow(RasterX - other.RasterX, 2) +
                  Math.Pow(RasterY - other.RasterY, 2));

    /// <summary>
    /// Nebeneinander sitzen = gleiche Tischgruppe + direkt benachbarte PlatzNr.
    /// Gilt für horizontale (parallel zur Tafel) und vertikale (senkrecht zur Tafel)
    /// Gruppen gleichermaßen — die Richtung ist unterschiedlich aber die Formel gleich.
    /// </summary>
    public bool SitzenZusammen(Sitzplatz other)
    {
        if (Gruppe != other.Gruppe) return false;
        return Math.Abs(PlatzNr - other.PlatzNr) <= 1;
    }
}

// ── Bewertung eines einzelnen Wunsches ───────────────────────────────────────
public class WunschBewertung
{
    public Schueler  Schueler  { get; set; } = null!;
    public Wunsch    Wunsch    { get; set; } = null!;
    public bool      Erfuellt  { get; set; }
    public int       Abstand   { get; set; }   // Gruppenabstand der beiden Schüler
}

// ── Ergebnis einer Lösung ────────────────────────────────────────────────────
public class Loesung
{
    public int             Index        { get; set; }   // 1, 2, 3
    public string          Bezeichnung  { get; set; } = "";
    public List<Sitzplatz> Sitzplaetze  { get; set; } = new();
    public List<string>    Protokoll    { get; set; } = new();
    public List<string>    Warnungen    { get; set; } = new();

    // Bewertungsdetails
    public List<WunschBewertung> Bewertungen { get; set; } = new();

    // Scores
    public int Prio3Gesamt    { get; set; }
    public int Prio3Erfuellt  { get; set; }
    public int Prio2Gesamt    { get; set; }
    public int Prio2Erfuellt  { get; set; }
    public int Prio1Gesamt    { get; set; }
    public int Prio1Erfuellt  { get; set; }
    public int SehschwaeacheVorne { get; set; }   // Anzahl korrekt vorne
    public int SehschwaeacheGesamt { get; set; }
    public double GeschlechterScore { get; set; } // 0-1, 1=perfekt
    public double GesamtScore { get; set; }

    public bool Prio3Vollstaendig => Prio3Gesamt == 0 || Prio3Erfuellt == Prio3Gesamt;

    public string ScoreText =>
        $"Prio3: {Prio3Erfuellt}/{Prio3Gesamt} | " +
        $"Prio2: {Prio2Erfuellt}/{Prio2Gesamt} | Score: {GesamtScore:F0}";
}

// ── Optimierungsergebnis (alle 3 Lösungen) ───────────────────────────────────
public class OptimierungsErgebnis
{
    public List<Loesung> Loesungen    { get; set; } = new();
    public Loesung?      BesteLoesung => Loesungen
        .Where(l => l.Prio3Vollstaendig)
        .OrderByDescending(l => l.GesamtScore)
        .FirstOrDefault()
        ?? Loesungen.OrderByDescending(l => l.GesamtScore).FirstOrDefault();
}
