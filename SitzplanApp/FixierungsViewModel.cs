using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SitzplanApp.Models;

namespace SitzplanApp.ViewModels;

// ── Ein Platz im Fixierungs-Canvas ───────────────────────────────────────────
public partial class FixierungsPlatzVM : ObservableObject
{
    private const double KARTE_B = 120.0;
    private const double KARTE_H = 88.0;
    private const double LUECKE  = 20.0;

    public Tischgruppe Gruppe   { get; }
    public int         PlatzNr  { get; }
    public double      CanvasLeft { get; }
    public double      CanvasTop  { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Farbe))]
    [NotifyPropertyChangedFor(nameof(AnzeigeText))]
    [NotifyPropertyChangedFor(nameof(IstBelegt))]
    private Schueler? _schueler;

    public bool   IstBelegt   => Schueler != null;
    public string AnzeigeText => Schueler != null
        ? $"{PlatzNr}\n{KurzName(Schueler.Name)}"
        : PlatzNr.ToString();

    public string Farbe => Schueler != null ? "#D6E4F0" : "#F5F5F5";

    // Ob dieser Platz ein gültiges Ziel für einen Drag ist
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RandFarbe))]
    [NotifyPropertyChangedFor(nameof(Opazitaet))]
    [NotifyPropertyChangedFor(nameof(IstKlickbar))]
    private bool _istZiel;

    // Ob eine Auswahl aktiv ist (von FixierungsVM gesetzt)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opazitaet))]
    [NotifyPropertyChangedFor(nameof(IstKlickbar))]
    private bool _auswahlAktiv;

    public string RandFarbe  => IstZiel ? "#27AE60" : "#AAAAAA";
    public double Opazitaet  => AuswahlAktiv && !IstZiel ? 0.3 : 1.0;
    public bool   IstKlickbar => !AuswahlAktiv || IstZiel;

    public FixierungsPlatzVM(Tischgruppe gruppe, int platzNr)
    {
        Gruppe  = gruppe;
        PlatzNr = platzNr;

        if (!gruppe.IstVertikal)
        {
            CanvasLeft = (gruppe.Spalte - 1) * (KARTE_B + LUECKE)
                         + (platzNr - 1) * KARTE_B;
            CanvasTop  = (gruppe.Reihe  - 1) * (KARTE_H + LUECKE);
        }
        else
        {
            CanvasLeft = (gruppe.Spalte - 1) * (KARTE_B + LUECKE);
            CanvasTop  = (gruppe.Reihe  - 1) * (KARTE_H + LUECKE)
                         + (platzNr - 1) * KARTE_H;
        }
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

// ── Ein Schüler in der Namensliste ────────────────────────────────────────────
public partial class FixierungsSchuelerVM : ObservableObject
{
    public Schueler Schueler { get; }
    public string   Name     => Schueler.Name;

    // Oder-Logik Gruppen als Hinweistext
    public string OderLogikHinweis => Schueler.FixGruppenNamen.Count > 1
        ? "(" + string.Join(" oder ", Schueler.FixGruppenNamen) + ")"
        : Schueler.FixGruppenNamen.Count == 1 && !Schueler.FixSitzplatzNr.HasValue
            ? $"(Gruppe: {Schueler.FixGruppenNamen[0]}, kein Platz)"
            : "";
    public bool HatOderLogik          => Schueler.FixGruppenNamen.Count > 1
                                         || (Schueler.FixGruppenNamen.Count == 1
                                             && !Schueler.FixSitzplatzNr.HasValue);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Farbe))]
    private bool _istFixiert;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Farbe))]
    private bool _istAusgewaehlt;

    public string Farbe => IstAusgewaehlt ? "#FFF2CC" : IstFixiert ? "#D6E4F0" : "#FFFFFF";

    public void AktualisierHinweis()
    {
        OnPropertyChanged(nameof(OderLogikHinweis));
        OnPropertyChanged(nameof(HatOderLogik));
    }

    public FixierungsSchuelerVM(Schueler s)
    {
        Schueler  = s;
        IstFixiert = s.FixGruppenNamen.Count > 0;
    }
}

// ── Haupt-ViewModel für das Fixierungs-Tab ────────────────────────────────────
public partial class FixierungsVM : ObservableObject
{
    public ObservableCollection<FixierungsPlatzVM>    Plaetze      { get; } = new();
    public ObservableCollection<GruppeVM>             Gruppen      { get; } = new();
    public ObservableCollection<FixierungsSchuelerVM> SchuelerListe { get; } = new();

    private List<Tischgruppe> _alleGruppen  = new();
    private List<Schueler>    _alleSchueler = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatAusgewaehlt))]
    [NotifyPropertyChangedFor(nameof(AusgewaehltName))]
    private FixierungsSchuelerVM? _ausgewaehlt;

    public bool   HatAusgewaehlt => Ausgewaehlt != null;
    public string AusgewaehltName => Ausgewaehlt?.Name ?? "";

    public void Initialisiere(List<Schueler> schueler, List<Tischgruppe> gruppen)
    {
        _alleSchueler = schueler;
        _alleGruppen  = gruppen;

        Plaetze.Clear();
        Gruppen.Clear();
        SchuelerListe.Clear();
        Ausgewaehlt = null;

        foreach (var g in gruppen)
            Gruppen.Add(new GruppeVM(g));

        foreach (var g in gruppen.OrderBy(g => g.Reihe).ThenBy(g => g.Spalte))
            for (int p = 1; p <= g.Sitzplaetze; p++)
                Plaetze.Add(new FixierungsPlatzVM(g, p));

        foreach (var s in schueler.OrderBy(s => s.Name))
            SchuelerListe.Add(new FixierungsSchuelerVM(s));

        // Bereits fixierte Schüler vorbelegen (mit Platz-Nr)
        foreach (var s in schueler.Where(s =>
            s.FixGruppenNamen.Count == 1 && s.FixSitzplatzNr.HasValue))
        {
            var platz = Plaetze.FirstOrDefault(p =>
                p.Gruppe.Name == s.FixGruppenNamen[0] &&
                p.PlatzNr     == s.FixSitzplatzNr);
            if (platz != null)
            {
                platz.Schueler = s;
                var vm = SchuelerListe.FirstOrDefault(v => v.Schueler == s);
                if (vm != null) vm.IstFixiert = true;
            }
        }

        // Schüler mit nur Gruppen-Fixierung (ohne PlatzNr) als fixiert markieren
        foreach (var s in schueler.Where(s =>
            s.FixGruppenNamen.Count >= 1 && !s.FixSitzplatzNr.HasValue))
        {
            var vm = SchuelerListe.FirstOrDefault(v => v.Schueler == s);
            if (vm != null) vm.IstFixiert = true;
        }
    }

    // ── Schüler aus Liste wählen ──────────────────────────────────────────────
    public void WaehlSchueler(FixierungsSchuelerVM vm)
    {
        // Bereits ausgewählt → deselektieren
        if (Ausgewaehlt == vm)
        {
            Ausgewaehlt = null;
            vm.IstAusgewaehlt = false;
            AktualisiereZielplaetze(null);
            return;
        }

        // Vorherigen deselektieren
        if (Ausgewaehlt != null) Ausgewaehlt.IstAusgewaehlt = false;

        Ausgewaehlt = vm;
        vm.IstAusgewaehlt = true;
        AktualisiereZielplaetze(vm.Schueler);
    }

    // ── Platz angeklickt ──────────────────────────────────────────────────────
    public void PlatzGeklickt(FixierungsPlatzVM platz)
    {
        // Belegter Platz ohne ausgewählten Schüler → Schüler von Platz lösen
        if (platz.IstBelegt && Ausgewaehlt == null)
        {
            var s2 = platz.Schueler!;
            platz.Schueler = null;
            s2.FixGruppenNamen.Clear();
            s2.FixSitzplatzNr = null;
            var vm2 = SchuelerListe.FirstOrDefault(v => v.Schueler == s2);
            if (vm2 != null)
            {
                vm2.IstFixiert = false;
                vm2.AktualisierHinweis();
            }
            OnFixierungGeaendert?.Invoke(s2);
            return;
        }

        if (Ausgewaehlt == null) return;
        if (!platz.IstZiel) return;

        var s = Ausgewaehlt.Schueler;

        // Alten Platz freimachen
        var alterPlatz = Plaetze.FirstOrDefault(p => p.Schueler == s);
        if (alterPlatz != null) alterPlatz.Schueler = null;

        // Auf neuen Platz
        platz.Schueler = s;
        s.FixGruppenNamen.Clear();
        s.FixGruppenNamen.Add(platz.Gruppe.Name);
        s.FixSitzplatzNr = platz.PlatzNr;

        Ausgewaehlt.IstFixiert     = true;
        Ausgewaehlt.IstAusgewaehlt = false;
        Ausgewaehlt.AktualisierHinweis();
        OnFixierungGeaendert?.Invoke(s);
        Ausgewaehlt = null;

        AktualisiereZielplaetze(null);
    }

    // ── Fixierung aufheben ────────────────────────────────────────────────────
    public void FixierungAufheben(FixierungsSchuelerVM vm)
    {
        var platz = Plaetze.FirstOrDefault(p => p.Schueler == vm.Schueler);
        if (platz != null) platz.Schueler = null;

        vm.Schueler.FixGruppenNamen.Clear();
        vm.Schueler.FixSitzplatzNr = null;
        vm.IstFixiert     = false;
        vm.IstAusgewaehlt = false;
        vm.AktualisierHinweis();
        OnFixierungGeaendert?.Invoke(vm.Schueler);

        if (Ausgewaehlt == vm) Ausgewaehlt = null;
        AktualisiereZielplaetze(null);
    }

    public Action<Schueler>? OnFixierungGeaendert { get; set; }

    /// <summary>Wird aufgerufen wenn aus einem Lösungs-Tab ein Platz fixiert wurde.</summary>
    public void AktualisierePlatz(Schueler s)
    {
        // Alten Platz freimachen
        var alterPlatz = Plaetze.FirstOrDefault(p => p.Schueler == s);
        if (alterPlatz != null) alterPlatz.Schueler = null;

        // Neuen Platz belegen
        if (s.FixGruppenNamen.Count == 1 && s.FixSitzplatzNr.HasValue)
        {
            var neuerPlatz = Plaetze.FirstOrDefault(p =>
                p.Gruppe.Name == s.FixGruppenNamen[0] &&
                p.PlatzNr     == s.FixSitzplatzNr);
            if (neuerPlatz != null) neuerPlatz.Schueler = s;
        }

        // VM aktualisieren
        var vm = SchuelerListe.FirstOrDefault(v => v.Schueler == s);
        if (vm != null)
        {
            vm.IstFixiert = s.FixGruppenNamen.Count > 0;
            vm.AktualisierHinweis();
        }

        // Lösungs-Tabs benachrichtigen
        OnFixierungGeaendert?.Invoke(s);
    }

    // ── Erlaubte Zielplätze markieren ─────────────────────────────────────────
    private void AktualisiereZielplaetze(Schueler? s)
    {
        foreach (var p in Plaetze)
        {
            p.AuswahlAktiv = s != null;

            if (s == null) { p.IstZiel = false; continue; }

            p.IstZiel = s.FixGruppenNamen.Count == 0
                ? !p.IstBelegt
                : s.FixGruppenNamen.Any(n =>
                    n.Equals(p.Gruppe.Name, StringComparison.OrdinalIgnoreCase))
                  && !p.IstBelegt;
        }
    }
}
