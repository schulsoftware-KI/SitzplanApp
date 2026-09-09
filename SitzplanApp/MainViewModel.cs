using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SitzplanApp.Models;

namespace SitzplanApp.ViewModels;

// ── Anzeigemodell für einen Sitzplatz auf dem Canvas ─────────────────────────
public partial class SitzplatzVM : ObservableObject
{
    private const double KARTE_B = 120.0;
    private const double KARTE_H = 88.0;
    private const double LUECKE  = 20.0;

    public string  Gruppe        { get; }
    public int     PlatzNr       { get; }
    public string  Geschlecht    { get; private set; }
    public bool    HatVerletzung { get; private set; }
    public double  CanvasLeft    { get; }
    public double  CanvasTop     { get; }

    // Mutable für manuelle Verschiebung
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(IstFrei))]
    [NotifyPropertyChangedFor(nameof(Farbe))]
    private string _originalName = "";

    public string Name => string.IsNullOrEmpty(OriginalName) ? "[frei]" : KurzName(OriginalName);
    public bool   IstFrei => string.IsNullOrEmpty(OriginalName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Farbe))]
    [NotifyPropertyChangedFor(nameof(RandFarbe))]
    [NotifyPropertyChangedFor(nameof(RandStaerke))]
    private bool _istMarkiert;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Farbe))]
    [NotifyPropertyChangedFor(nameof(FixLabel))]
    private bool _fixiert;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FixLabel))]
    private int _fixiertInLoesung = 0;

    // Drag-Highlight
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RandFarbe))]
    [NotifyPropertyChangedFor(nameof(RandStaerke))]
    private bool _istDragZiel;

    // Notizen des Schülers (editierbar per Rechtsklick)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatNotiz))]
    [NotifyPropertyChangedFor(nameof(NotizTooltip))]
    private string _notizen = "";

    public bool   HatNotiz     => !string.IsNullOrWhiteSpace(Notizen);
    public string NotizTooltip => HatNotiz ? $"📝 {Notizen}" : "Rechtsklick: Notiz bearbeiten";

    public string FixLabel => FixiertInLoesung > 0 ? $"Fix L{FixiertInLoesung}" : "Fix";
    public string RandFarbe   => IstDragZiel ? "#27AE60" : _istMarkiert ? "#000000" : "Transparent";
    public double RandStaerke => IstDragZiel ? 3.0 : _istMarkiert ? 3.0 : 0.0;

    public string Farbe =>
        IstFrei             ? "#F5F5F5"
        : _hatPrio3Verletzung ? "#E74C3C"
        : _hatPrio2Verletzung ? "#FAD7A0"
        : _hatPrio1Verletzung ? "#FDEBD0"
        : "#D6E4F0";

    private bool _hatPrio3Verletzung = false;
    private bool _hatPrio2Verletzung = false;
    private bool _hatPrio1Verletzung = false;
    private string _farbeBase = "#D6E4F0"; // nicht mehr für Farbe verwendet, bleibt für Kompatibilität

    public SitzplatzVM(Sitzplatz p, Loesung? loesung)
    {
        Gruppe        = p.Gruppe.Name;
        PlatzNr       = p.PlatzNr;
        _originalName = p.Schueler?.Name ?? "";
        Geschlecht    = p.Schueler?.Geschlecht ?? "";
        _fixiert      = p.Schueler?.FixGruppenname != null;
        _notizen      = p.Schueler?.Notizen ?? "";

        HatVerletzung = loesung != null && p.Schueler != null &&
            loesung.Bewertungen.Any(b =>
                b.Schueler == p.Schueler && !b.Erfuellt &&
                b.Wunsch.Prio >= Prioritaet.Prio2);

        if (loesung != null && p.Schueler != null)
        {
            _hatPrio3Verletzung = loesung.Bewertungen.Any(b =>
                b.Schueler == p.Schueler && !b.Erfuellt &&
                b.Wunsch.Prio == Prioritaet.Prio3);
            _hatPrio2Verletzung = !_hatPrio3Verletzung && loesung.Bewertungen.Any(b =>
                b.Schueler == p.Schueler && !b.Erfuellt &&
                b.Wunsch.Prio == Prioritaet.Prio2);
            _hatPrio1Verletzung = !_hatPrio3Verletzung && !_hatPrio2Verletzung && loesung.Bewertungen.Any(b =>
                b.Schueler == p.Schueler && !b.Erfuellt &&
                b.Wunsch.Prio == Prioritaet.Prio1);
        }

        if (!p.Gruppe.IstVertikal)
        {
            CanvasLeft = (p.Gruppe.Spalte - 1) * (KARTE_B + LUECKE)
                         + (p.PlatzNr - 1) * KARTE_B;
            CanvasTop  = (p.Gruppe.Reihe  - 1) * (KARTE_H + LUECKE);
        }
        else
        {
            CanvasLeft = (p.Gruppe.Spalte - 1) * (KARTE_B + LUECKE);
            CanvasTop  = (p.Gruppe.Reihe  - 1) * (KARTE_H + LUECKE)
                         + (p.PlatzNr - 1) * KARTE_H;
        }
    }

    /// <summary>
    /// Aktualisiert HatVerletzung und Farbe anhand der neu berechneten Bewertungen.
    /// Muss nach jedem BewerteManuell-Aufruf für alle SitzplatzVMs aufgerufen werden.
    /// </summary>
    public void AktualisierVerletzung(Loesung loesung)
    {
        if (IstFrei)
        {
            _hatPrio3Verletzung = false;
            _hatPrio2Verletzung = false;
            _hatPrio1Verletzung = false;
            HatVerletzung = false;
            OnPropertyChanged(nameof(Farbe));
            return;
        }
        _hatPrio3Verletzung = loesung.Bewertungen.Any(b =>
            b.Schueler.Name == OriginalName && !b.Erfuellt &&
            b.Wunsch.Prio == Prioritaet.Prio3);
        _hatPrio2Verletzung = !_hatPrio3Verletzung && loesung.Bewertungen.Any(b =>
            b.Schueler.Name == OriginalName && !b.Erfuellt &&
            b.Wunsch.Prio == Prioritaet.Prio2);
        _hatPrio1Verletzung = !_hatPrio3Verletzung && !_hatPrio2Verletzung && loesung.Bewertungen.Any(b =>
            b.Schueler.Name == OriginalName && !b.Erfuellt &&
            b.Wunsch.Prio == Prioritaet.Prio1);
        HatVerletzung = _hatPrio3Verletzung || _hatPrio2Verletzung || _hatPrio1Verletzung;
        OnPropertyChanged(nameof(Farbe));
        OnPropertyChanged(nameof(HatVerletzung));
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

public class GruppeVM
{
    private const double KARTE_B = 120.0;
    private const double KARTE_H = 88.0;
    private const double LUECKE  = 20.0;

    public string Name        { get; }
    public double CanvasLeft  { get; }
    public double CanvasTop   { get; }
    public double Breite      { get; }
    public double Hoehe       { get; }
    public bool   IstVertikal { get; }

    public GruppeVM(Tischgruppe g)
    {
        Name        = g.Name;
        IstVertikal = g.IstVertikal;
        CanvasLeft  = (g.Spalte - 1) * (KARTE_B + LUECKE);
        CanvasTop   = (g.Reihe  - 1) * (KARTE_H + LUECKE) - 16;

        if (!g.IstVertikal)
        {
            Breite = g.Sitzplaetze * KARTE_B;
            Hoehe  = KARTE_H;
        }
        else
        {
            Breite = KARTE_B;
            Hoehe  = g.Sitzplaetze * KARTE_H;
        }
    }
}


// ── Anzeigemodell für eine Lösung (Tab) ──────────────────────────────────────
public partial class LoesungVM : ObservableObject
{
    public Loesung Loesung { get; }
    public string TabHeader =>
        $"Lösung {Loesung.Index}" +
        (Loesung.Prio3Vollstaendig ? "" : " ⚠");
    public string ScoreText   => Loesung.ScoreText;
    public bool   IstBeste    { get; set; }
    public string HintergrundFarbe =>
        IstBeste ? "#E2EFDA" : (Loesung.Prio3Vollstaendig ? "#FFFFFF" : "#FCE4D6");

    public ObservableCollection<SitzplatzVM> Sitzplaetze { get; } = new();
    public ObservableCollection<GruppeVM>   Gruppen     { get; } = new();
    public ObservableCollection<string>     Protokoll   { get; } = new();
    public ObservableCollection<string>     Warnungen   { get; } = new();
    public ObservableCollection<string>     Prio2Warnungen { get; } = new();

    // Wartezone für manuell verschobene Schüler
    public ObservableCollection<string> Wartezone { get; } = new();

    // Drag-State
    private string? _dragName;       // Name des gezogenen Schülers
    private bool    _dragAusWartezone; // kommt er aus der Wartezone?
    private bool    _istEchterDrag;    // true erst nach Schwellenwert-Überschreitung
    private string? _dragQuelleGruppe; // Gruppe des Quellplatzes (für Selbst-Drop-Erkennung)
    private int     _dragQuellePlatz;  // PlatzNr des Quellplatzes

    [ObservableProperty]
    private bool _istManuellVeraendert = false;

    // Markierter Schüler für Hervorhebung
    [ObservableProperty] private string _markierterName = "";

    // Info-Panel für markierten Schüler — aufgeteilt in Kopf (Name/Platz) und Wünsche
    [ObservableProperty] private string _markierungKopf     = "";
    [ObservableProperty] private string _markierungWuensche = "";
    [ObservableProperty] private string _markierungInfo     = "";
    [ObservableProperty] private bool   _zeigeMarkierung    = false;

    public LoesungVM(Loesung l, List<Tischgruppe> alleGruppen, bool istBeste)
    {
        Loesung  = l;
        IstBeste = istBeste;

        foreach (var p in l.Sitzplaetze)
            Sitzplaetze.Add(new SitzplatzVM(p, l));
        foreach (var g in alleGruppen)
            Gruppen.Add(new GruppeVM(g));
        foreach (var z in l.Protokoll)
            Protokoll.Add(z);
        foreach (var w in l.Warnungen)
            Warnungen.Add(w);
    }

    // Callbacks nach oben zum MainViewModel
    public Action<Schueler>?              OnSchuelerAuswaehlen { get; set; }
    public Action<Schueler, string, int>? OnFixieren           { get; set; }
    public Action<Schueler>?              OnFixierungAufheben  { get; set; }

    // Rechtsklick-Notiz: MainViewModel/MainWindow hängt hier den Dialog-Aufruf ein
    public Action<Schueler>? OnNotizBearbeiten { get; set; }

    /// <summary>Vom Code-Behind bei MouseRightButtonUp aufgerufen.</summary>
    public void SitzplatzRechtsklick(SitzplatzVM sp)
    {
        if (sp.IstFrei) return;
        var schueler = _alleSchueler.FirstOrDefault(s => s.Name == sp.OriginalName);
        if (schueler == null) return;
        OnNotizBearbeiten?.Invoke(schueler);
    }

    /// <summary>Synchronisiert Notizen-Property aller SitzplatzVMs dieses Schülers.</summary>
    public void AktualisierNotizen(string schuelerName, string neueNotiz)
    {
        foreach (var sp in Sitzplaetze)
            if (sp.OriginalName == schuelerName)
                sp.Notizen = neueNotiz;
    }

    // ── Drag & Drop manuell ───────────────────────────────────────────────────
    public void StarteDragVonPlatz(SitzplatzVM sp)
    {
        if (sp.IstFrei) return;
        _dragName          = sp.OriginalName;
        _dragAusWartezone  = false;
        _istEchterDrag     = true;
        _dragQuelleGruppe  = sp.Gruppe;
        _dragQuellePlatz   = sp.PlatzNr;
        AktualisiereDragZiele();
    }

    public bool IstSelbstDrop(SitzplatzVM ziel) =>
        !_dragAusWartezone &&
        ziel.Gruppe  == _dragQuelleGruppe &&
        ziel.PlatzNr == _dragQuellePlatz;

    public void StarteDragAusWartezone(string name)
    {
        _dragName = name;
        _dragAusWartezone = true;
        _istEchterDrag = true;
        AktualisiereDragZiele();
    }

    public void DropAufPlatz(SitzplatzVM ziel)
    {
        if (_dragName == null) return;

        string? verdrängter = null;

        // Zielplatz belegt → verdrängten Schüler in Wartezone
        if (!ziel.IstFrei)
            verdrängter = ziel.OriginalName;

        // Herkunft leeren
        if (!_dragAusWartezone)
        {
            var quelle = Sitzplaetze.FirstOrDefault(sp => sp.OriginalName == _dragName);
            if (quelle != null)
            {
                quelle.OriginalName = "";
                quelle.Fixiert      = false;
            }
        }
        else
        {
            Wartezone.Remove(_dragName);
        }

        // Ziel belegen
        ziel.OriginalName = _dragName;
        ziel.Fixiert      = false; // keine automatische Fixierung

        // Verdrängter → Wartezone
        if (verdrängter != null)
            Wartezone.Add(verdrängter);

        IstManuellVeraendert = true;
        AbschlussDrag();
    }

    public void DropInWartezone(string name)
    {
        if (_dragName == null) return;
        if (!_dragAusWartezone)
        {
            var quelle = Sitzplaetze.FirstOrDefault(sp => sp.OriginalName == _dragName);
            if (quelle != null)
            {
                quelle.OriginalName = "";
                quelle.Fixiert      = false;
            }
        }
        if (!Wartezone.Contains(_dragName))
            Wartezone.Add(_dragName);
        IstManuellVeraendert = true;
        AbschlussDrag();
    }

    public bool IstEchterDrag => _istEchterDrag;

    public void AbschlussDrag()
    {
        foreach (var sp in Sitzplaetze) sp.IstDragZiel = false;
        _dragName         = null;
        _istEchterDrag    = false;
        _dragQuelleGruppe = null;
        _dragQuellePlatz  = 0;
    }

    public void BrecheDragAb()
    {
        foreach (var sp in Sitzplaetze) sp.IstDragZiel = false;
        _dragName         = null;
        _istEchterDrag    = false;
        _dragQuelleGruppe = null;
        _dragQuellePlatz  = 0;
    }

    private void AktualisiereDragZiele()
    {
        foreach (var sp in Sitzplaetze)
            sp.IstDragZiel = true; // alle Plätze sind Ziel (auch belegte → Tausch)
    }

    // ── Manuelle Lösung prüfen ────────────────────────────────────────────────
    public Action<LoesungVM, List<Schueler>, ParameterSet>? OnPruefen { get; set; }

    [RelayCommand]
    private void ManuelleLoesungPruefen()
    {
        OnPruefen?.Invoke(this, _alleSchueler, _parameter);
    }

    // ── Diese Lösung speichern ────────────────────────────────────────────────
    // Wird vom MainViewModel gesetzt und sichert genau diese Lösung in der Excel-Datei.
    public Action<LoesungVM>? OnSpeichern { get; set; }

    [RelayCommand]
    private void LoesungSpeichern()
    {
        OnSpeichern?.Invoke(this);
    }

    private List<Schueler>  _alleSchueler = new();
    private ParameterSet    _parameter    = new();

    public void SetzeKontext(List<Schueler> schueler, ParameterSet parameter)
    {
        _alleSchueler = schueler;
        _parameter    = parameter;
    }

    [RelayCommand]
    private void SitzplatzDoppelklick(SitzplatzVM? sp)
    {
        if (sp == null || string.IsNullOrEmpty(sp.OriginalName)) return;
        var sitzplatz = Loesung.Sitzplaetze
            .FirstOrDefault(p => p.Schueler?.Name == sp.OriginalName);
        if (sitzplatz?.Schueler == null) return;

        var s = sitzplatz.Schueler;
        if (sp.Fixiert)
            OnFixierungAufheben?.Invoke(s);                 // bereits fixiert → lösen
        else
            OnFixieren?.Invoke(s, sp.Gruppe, sp.PlatzNr);   // sonst → fixieren
    }

    [RelayCommand]
    private void SitzplatzKlick(SitzplatzVM? sp)
    {
        if (sp == null || string.IsNullOrEmpty(sp.OriginalName)) return;
        var s = Loesung.Sitzplaetze
            .FirstOrDefault(p => p.Schueler?.Name == sp.OriginalName)
            ?.Schueler;
        if (s != null)
        {
            MarkiereSchueler(s);
            OnSchuelerAuswaehlen?.Invoke(s);
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

    public void MarkiereSchueler(Schueler? s)
    {
        if (s == null)
        {
            MarkierterName    = "";
            MarkierungKopf    = "";
            MarkierungWuensche = "";
            MarkierungInfo    = "";
            ZeigeMarkierung   = false;
            Prio2Warnungen.Clear();
            foreach (var sp in Sitzplaetze) sp.IstMarkiert = false;
            return;
        }

        MarkierterName  = s.Name;
        ZeigeMarkierung = true;

        var platz = Sitzplaetze.FirstOrDefault(sp => sp.OriginalName == s.Name);
        foreach (var sp in Sitzplaetze)
            sp.IstMarkiert = sp.OriginalName == s.Name;

        // Kopf: Name + Platz
        var kopf = new System.Text.StringBuilder();
        kopf.Append($"► {KurzName(s.Name)}");
        if (platz != null)
            kopf.Append($"  |  {platz.Gruppe} / Platz {platz.PlatzNr}");
        if (s.Sehschwaeche) kopf.Append("  |  Sehschw.");
        if (s.Linkshaender) kopf.Append("  |  Links");
        MarkierungKopf = kopf.ToString();

        // Wünsche: kompakt nebeneinander
        var wu = new System.Text.StringBuilder();
        foreach (var w in s.ZusammenMit)
        {
            var bew = Loesung.Bewertungen.FirstOrDefault(b => b.Schueler == s && b.Wunsch == w);
            string status = bew == null ? "?" : bew.Erfuellt ? "✓" : "✗";
            wu.Append($"  {status} +{KurzName(w.ZielName)}[Prio{(int)w.Prio}]");
        }
        foreach (var w in s.NichtNeben)
        {
            var bew = Loesung.Bewertungen.FirstOrDefault(b => b.Schueler == s && b.Wunsch == w);
            string status = bew == null ? "?" : bew.Erfuellt ? "✓" : "✗";
            wu.Append($"  {status} –{KurzName(w.ZielName)}[Prio{(int)w.Prio}]");
        }
        MarkierungWuensche = wu.ToString().TrimStart();

        // Verbote anhängen
        if (s.Verbote.Count > 0)
            MarkierungWuensche += (MarkierungWuensche.Length > 0 ? "  " : "") +
                "🚫 Verbote: " + string.Join(", ", s.Verbote.Select(v => v.ToString()));

        MarkierungInfo = MarkierungKopf + (MarkierungWuensche.Length > 0 ? "\n" + MarkierungWuensche : "");

        // Prio-2-Verletzungen dieses Schülers → orangenes Warnfeld
        Prio2Warnungen.Clear();
        foreach (var bew in Loesung.Bewertungen
            .Where(b => b.Schueler == s && !b.Erfuellt && b.Wunsch.Prio == Prioritaet.Prio2))
        {
            string richtung = bew.Wunsch.IstAntiWunsch ? "sitzt neben" : "sitzt nicht neben";
            Prio2Warnungen.Add($"⚠ Prio-2: {s.Name} {richtung} {bew.Wunsch.ZielName}");
        }
    }
}

// ── Hauptviewmodel ────────────────────────────────────────────────────────────
public partial class MainViewModel : ObservableObject
{
    private readonly ExcelImportService  _import    = new();
    private readonly ExcelExportService  _export    = new();
    private readonly OptimierungsService _optimizer = new();
    private readonly ExcelUpdateService  _updater   = new();

    [ObservableProperty] private string   _excelPfad    = "";
    [ObservableProperty] private string   _statusText   = "Excel-Datei laden um zu beginnen.";
    [ObservableProperty] private bool     _istGeladen   = false;
    [ObservableProperty] private bool     _istOptimiert = false;
    [ObservableProperty] private Schueler? _gewaehlterSchueler;
    [ObservableProperty] private string   _fixGruppenname = "";
    [ObservableProperty] private string   _fixPlatzNr     = "";
    [ObservableProperty] private int      _aktiveLoesung  = 0;
    [ObservableProperty] private Schueler? _markierterSchueler;

    public FixierungsVM  FixierungsVM  { get; }
    public RaumplanVM    RaumplanVM    { get; }
    public SchuelerVM    SchuelerVM    { get; }

    // Inline-Wünsche-Editor (Chips + Autocomplete)
    public WuenscheEditorVM WuenscheEditor { get; } = new();

    // Wunsch-Matrix (Bulk-Erfassung ganze Klasse)
    public WunschMatrixVM WunschMatrixVM { get; } = new();

    // Notiz-Dialog: wird vom MainWindow gesetzt und öffnet den NotizenEditDialog
    public Action<Schueler>? OnNotizDialogAnfordern { get; set; }

    public MainViewModel()
    {
        FixierungsVM = new FixierungsVM();
        RaumplanVM   = new RaumplanVM();
        SchuelerVM   = new SchuelerVM();

        // Grunddaten-Feld geändert → Lösung als veraltet markieren
        SchuelerVM.OnGeaendert = () =>
        {
            if (!IstGeladen) return;
            IstOptimiert = false;
            StatusText   = "Schülerdaten geändert – „Neuoptimierung“ ausführen, um sie anzuwenden.";
        };
        // Schüler hinzugefügt/gelöscht → alle abhängigen Ansichten neu aufbauen
        SchuelerVM.OnListeGeaendert = ReSyncNachSchuelerListe;
        // Umbenennung → Wunsch-Verweise nachziehen, dann re-synchronisieren
        SchuelerVM.OnUmbenannt = (alt, neu) =>
        {
            PropagiereUmbenennung(alt, neu);
            ReSyncNachSchuelerListe();
        };
        // Namensprüfung (nicht leer, nicht doppelt)
        SchuelerVM.NamePruefen = NameIstGueltig;
        // Zeile in der Schüler-Tabelle markiert → linkes Dialogfeld übernimmt ihn,
        // damit dort direkt Wünsche/Verbote bearbeitet werden können.
        SchuelerVM.OnSchuelerGewaehlt = s => GewaehlterSchueler = s;

        // Änderungen an den Gewichtungswerten überwachen (initiale Instanz).
        Parameter.PropertyChanged += ParameterWertGeaendert;

        // Wünsche geändert → aktuelle Lösung als veraltet markieren
        WuenscheEditor.OnGeaendert = () =>
        {
            IstOptimiert = false;
            StatusText   = "Wünsche geändert – neu optimieren, um sie anzuwenden.";
        };

        // Matrix-Änderung → Lösung veraltet + Chip-Panel des gewählten Schülers auffrischen
        WunschMatrixVM.OnGeaendert = () =>
        {
            IstOptimiert = false;
            StatusText   = "Wünsche (Matrix) geändert – neu optimieren, um sie anzuwenden.";
            WuenscheEditor.LadeSchueler(GewaehlterSchueler);
        };

        FixierungsVM.OnFixierungGeaendert = s =>
        {
            foreach (var lv in Loesungen)
                foreach (var sp in lv.Sitzplaetze)
                    if (sp.OriginalName == s.Name)
                    {
                        sp.Fixiert          = s.FixGruppenNamen.Count > 0;
                        sp.FixiertInLoesung = 0;
                    }
            if (GewaehlterSchueler == s)
            {
                FixGruppenname = string.Join("; ", s.FixGruppenNamen);
                FixPlatzNr     = s.FixSitzplatzNr?.ToString() ?? "";
            }
        };
    }
    public ObservableCollection<Models.Schueler> Schueler { get; } = new();
    public ObservableCollection<Tischgruppe> Gruppen   { get; } = new();
    public ObservableCollection<LoesungVM>   Loesungen { get; } = new();
    public ObservableCollection<DiagnoseZeile> Diagnose { get; } = new();

    private List<Schueler>    _alleSchueler = new();
    private List<Tischgruppe> _alleGruppen  = new();

    [ObservableProperty] private ParameterSet _parameter = ParameterSet.Standard();
    private OptimierungsErgebnis? _ergebnis;

    // Wird aufgerufen, wenn das ganze Parameter-Objekt getauscht wird (z. B. beim Laden).
    partial void OnParameterChanged(ParameterSet oldValue, ParameterSet newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= ParameterWertGeaendert;
        if (newValue != null) newValue.PropertyChanged += ParameterWertGeaendert;
    }

    // Wird aufgerufen, wenn ein einzelner Gewichtungswert im Parameter-Tab geändert wird.
    private void ParameterWertGeaendert(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IstGeladen) return;
        IstOptimiert = false;
        StatusText   = "Parameter geändert – „Neuoptimierung“ ausführen, um sie anzuwenden.";
    }

    partial void OnMarkierterSchuelerChanged(Schueler? value)
    {
        foreach (var l in Loesungen)
            l.MarkiereSchueler(value);
    }

    [RelayCommand]
    private void SchuelerMarkieren(Schueler? s)
    {
        MarkierterSchueler = s == MarkierterSchueler ? null : s;
    }

    // ── Laden ────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void ExcelLaden()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Excel-Datei wählen",
            Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ExcelPfad = dlg.FileName;
            (_alleSchueler, _alleGruppen, Parameter) = _import.LadeExcel(dlg.FileName);

            // Ggf. vorhandene gespeicherte Lösung aus einer früheren Datei verwerfen
            GespeicherteLoesung    = null;
            HatGespeicherteLoesung = false;

            Schueler.Clear();
            foreach (var s in _alleSchueler) Schueler.Add(s);
            Gruppen.Clear();
            foreach (var g in _alleGruppen) Gruppen.Add(g);

            WuenscheEditor.SetzeDatenquelle(_alleSchueler);
            WunschMatrixVM.Initialisiere(_alleSchueler);

            IstGeladen   = true;
            IstOptimiert = false;
            StatusText   = $"Geladen: {_alleSchueler.Count} Schüler, {_alleGruppen.Count} Gruppen – Optimierung läuft...";
            FixierungsVM.Initialisiere(_alleSchueler, _alleGruppen);
            RaumplanVM.Initialisiere(_alleGruppen, _alleSchueler);
            SchuelerVM.Initialisiere(_alleSchueler);
            RaumplanVM.OnGeaendert = () =>
            {
                // Lösungs-Tabs als veraltet markieren
                IstOptimiert = false;
                FixierungsVM.Initialisiere(_alleSchueler, _alleGruppen);
            };
            // Automatisch optimieren nach dem Laden
            Optimieren();

            // Falls die Datei eine gespeicherte Lösung enthält: wiederherstellen
            // und automatisch in den Vordergrund holen.
            LadeGespeicherteLoesungAusDatei(zeigeMeldung: false);
            if (HatGespeicherteLoesung)
            {
                AktiveLoesung = IndexGespeicherterTab;
                StatusText = "Gespeicherte Lösung wurde wiederhergestellt (Tab „★ Gespeicherte Lösung“).";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Tab-Index des Tabs „★ Gespeicherte Lösung“ (nach den 3 Lösungen).</summary>
    private const int IndexGespeicherterTab = 3;

    // ── Optimieren ───────────────────────────────────────────────────────────
    [RelayCommand]
    private void Optimieren()
    {
        if (_alleSchueler.Count == 0) return;
        try
        {
            _ergebnis = _optimizer.Optimiere(_alleSchueler, _alleGruppen, _parameter);
            var beste = _ergebnis.BesteLoesung;

            Loesungen.Clear();
            foreach (var l in _ergebnis.Loesungen)
                Loesungen.Add(ErzeugeLoesungVM(l, l == beste));

            // Eine ggf. zuvor geladene gespeicherte Lösung wird durch die
            // Neuoptimierung veraltet — sie bleibt als eigener Tab erhalten,
            // spiegelt aber weiter den gespeicherten Stand wider.

            AktualisiereDiagnose();

            IstOptimiert = true;
            AktiveLoesung = beste != null
                ? _ergebnis.Loesungen.IndexOf(beste)
                : 0;

            int warnungen = _ergebnis.Loesungen.Sum(l => l.Warnungen.Count);
            StatusText =
                $"Optimiert – Beste Lösung: Score {beste?.GesamtScore:F0} | " +
                $"P3: {beste?.Prio3Erfuellt}/{beste?.Prio3Gesamt} | " +
                $"Warnungen: {warnungen}";

            if (_ergebnis.Loesungen.Any(l => !l.Prio3Vollstaendig))
                MessageBox.Show(
                    "Achtung: Nicht alle Prio-3-Wünsche konnten erfüllt werden!\n" +
                    "Details in der Diagnose-Ansicht.",
                    "Prio-3-Konflikt", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Lösungs-Tab (VM) erzeugen und verdrahten ──────────────────────────────
    /// <summary>
    /// Baut ein <see cref="LoesungVM"/> für eine Lösung auf und hängt alle
    /// Callbacks (Auswahl, Prüfen, Fixieren, Notiz, Speichern) ein. Wird sowohl
    /// von der Optimierung als auch beim Laden einer gespeicherten Lösung genutzt.
    /// </summary>
    private LoesungVM ErzeugeLoesungVM(Loesung l, bool istBeste)
    {
        var lvm = new LoesungVM(l, _alleGruppen, istBeste);
        lvm.SetzeKontext(_alleSchueler, _parameter);

        lvm.OnSchuelerAuswaehlen = s =>
        {
            GewaehlterSchueler = _alleSchueler.FirstOrDefault(x => x.Name == s.Name);
        };
        lvm.OnPruefen = (loesungVm, schueler, param) =>
        {
            // Aktuelle manuelle Zuweisung aus VM in Loesung-Modell übertragen
            foreach (var sp in loesungVm.Sitzplaetze)
            {
                var sitzplatz = loesungVm.Loesung.Sitzplaetze
                    .FirstOrDefault(p => p.Gruppe.Name == sp.Gruppe && p.PlatzNr == sp.PlatzNr);
                if (sitzplatz != null)
                    sitzplatz.Schueler = string.IsNullOrEmpty(sp.OriginalName)
                        ? null
                        : schueler.FirstOrDefault(s => s.Name == sp.OriginalName);
            }
            // Neu bewerten
            var optimizer = new Services.OptimierungsService();
            optimizer.BewerteManuell(loesungVm.Loesung, schueler, param);
            // Warnungen aktualisieren
            loesungVm.Warnungen.Clear();
            foreach (var w in loesungVm.Loesung.Warnungen) loesungVm.Warnungen.Add(w);
            // Verletzungen auf Karten aktualisieren
            foreach (var sp in loesungVm.Sitzplaetze)
                sp.AktualisierVerletzung(loesungVm.Loesung);
            // Markierungsanzeige neu aufbauen falls ein Schüler markiert ist
            if (!string.IsNullOrEmpty(loesungVm.MarkierterName))
            {
                var markierterSchueler = loesungVm.Loesung.Sitzplaetze
                    .FirstOrDefault(p => p.Schueler?.Name == loesungVm.MarkierterName)
                    ?.Schueler;
                loesungVm.MarkiereSchueler(markierterSchueler);
            }
            StatusText = $"Lösung {loesungVm.Loesung.Index} geprüft: Score {loesungVm.Loesung.GesamtScore:F0}";
        };
        lvm.OnFixieren = (s, gruppenName, platzNr) =>
        {
            s.FixGruppenNamen.Clear();
            s.FixGruppenNamen.Add(gruppenName);
            s.FixSitzplatzNr = platzNr;
            FixierungsVM.AktualisierePlatz(s);
            // Alle SitzplatzVM in allen Lösungen (inkl. gespeicherter) aktualisieren
            foreach (var lv in AlleLoesungsVMs)
                foreach (var sp in lv.Sitzplaetze)
                    if (sp.OriginalName == s.Name)
                    {
                        sp.Fixiert = true;
                        sp.FixiertInLoesung = lvm.Loesung.Index;
                    }
            if (GewaehlterSchueler == s)
            {
                FixGruppenname = gruppenName;
                FixPlatzNr     = platzNr.ToString();
            }
            StatusText = $"Fixiert: {s.Name} → {gruppenName}/Platz {platzNr} (Lösung {lvm.Loesung.Index})";
        };
        lvm.OnFixierungAufheben = s =>
        {
            s.FixGruppenNamen.Clear();
            s.FixSitzplatzNr = null;
            FixierungsVM.AktualisierePlatz(s);
            // Alle SitzplatzVM in allen Lösungen (inkl. gespeicherter) zurücksetzen
            foreach (var lv in AlleLoesungsVMs)
                foreach (var sp in lv.Sitzplaetze)
                    if (sp.OriginalName == s.Name)
                    {
                        sp.Fixiert          = false;
                        sp.FixiertInLoesung = 0;
                    }
            if (GewaehlterSchueler == s)
            {
                FixGruppenname = "";
                FixPlatzNr     = "";
            }
            StatusText = $"Fixierung aufgehoben: {s.Name}";
        };
        lvm.OnNotizBearbeiten = s =>
        {
            OnNotizDialogAnfordern?.Invoke(s);
        };
        lvm.OnSpeichern = SpeichereLoesung;

        return lvm;
    }

    /// <summary>Alle sichtbaren Lösungs-Tabs inklusive gespeicherter Lösung.</summary>
    private IEnumerable<LoesungVM> AlleLoesungsVMs =>
        GespeicherteLoesung != null
            ? Loesungen.Append(GespeicherteLoesung!)
            : Loesungen;

    // ── Gespeicherte Lösung ───────────────────────────────────────────────────
    private readonly GespeicherteLoesungService _gespeicherteService = new();

    [ObservableProperty] private LoesungVM? _gespeicherteLoesung;
    [ObservableProperty] private bool        _hatGespeicherteLoesung;

    /// <summary>
    /// Sichert die vom Nutzer gewählte Lösung (den Stand des angeklickten Tabs,
    /// inkl. manueller Verschiebungen) als Blatt „Gespeicherte Lösung“ in der
    /// Excel-Datei, damit sie nach einem Neustart wieder geladen werden kann.
    /// </summary>
    private void SpeichereLoesung(LoesungVM lvm)
    {
        if (string.IsNullOrEmpty(ExcelPfad))
        {
            MessageBox.Show("Bitte zuerst eine Excel-Datei laden.", "Speichern",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            // Belegung aus dem VM lesen → erfasst auch manuelle Drag&Drop-Änderungen
            var belegung = lvm.Sitzplaetze
                .Where(sp => !sp.IstFrei)
                .Select(sp => new GespeicherteLoesungService.Belegung(
                    sp.Gruppe, sp.PlatzNr, sp.OriginalName))
                .ToList();

            _gespeicherteService.Speichern(
                ExcelPfad,
                lvm.Loesung.Bezeichnung,
                lvm.Loesung.GesamtScore,
                belegung);

            // Direkt als gespeicherte Lösung übernehmen, damit der Tab den
            // gesicherten Stand sofort widerspiegelt.
            LadeGespeicherteLoesungAusDatei(zeigeMeldung: false);

            StatusText = $"Lösung gespeichert ({belegung.Count} Plätze) in {ExcelPfad}";
            MessageBox.Show(
                "Die gewählte Lösung wurde in der Excel-Datei gespeichert.\n" +
                "Sie wird beim nächsten Laden dieser Datei automatisch wiederhergestellt.",
                "Lösung gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Speichern der Lösung:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Gespeicherte Lösung laden (Button) ────────────────────────────────────
    [RelayCommand]
    private void GespeicherteLoesungLaden()
    {
        if (string.IsNullOrEmpty(ExcelPfad)) return;
        LadeGespeicherteLoesungAusDatei(zeigeMeldung: true);
    }

    /// <summary>
    /// Liest eine ggf. vorhandene gespeicherte Lösung aus der Excel-Datei,
    /// bewertet sie und stellt sie im Tab „Gespeicherte Lösung“ dar.
    /// </summary>
    private void LadeGespeicherteLoesungAusDatei(bool zeigeMeldung)
    {
        try
        {
            var loesung = _gespeicherteService.Laden(
                ExcelPfad, _alleGruppen, _alleSchueler, out var hinweise);

            if (loesung == null)
            {
                if (zeigeMeldung)
                    MessageBox.Show("In dieser Datei ist keine gespeicherte Lösung vorhanden.",
                        "Keine gespeicherte Lösung",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Bewerten (setzt Scores/Verletzungen); leert dabei die Warnungen.
            _optimizer.BewerteManuell(loesung, _alleSchueler, _parameter);
            // Hinweise aus der Rekonstruktion nachträglich anhängen.
            foreach (var h in hinweise) loesung.Warnungen.Add(h);

            GespeicherteLoesung    = ErzeugeLoesungVM(loesung, istBeste: false);
            HatGespeicherteLoesung = true;

            if (zeigeMeldung)
                StatusText = $"Gespeicherte Lösung geladen: Score {loesung.GesamtScore:F0}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Laden der gespeicherten Lösung:\n{ex.Message}",
                "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Exportieren ──────────────────────────────────────────────────────────
    [RelayCommand]
    private void Exportieren()
    {
        if (_ergebnis == null || string.IsNullOrEmpty(ExcelPfad)) return;
        try
        {
            _export.ExportiereErgebnis(ExcelPfad, _ergebnis, _alleGruppen, _alleSchueler);
            StatusText = $"Exportiert in: {ExcelPfad}";
            MessageBox.Show(
                "Lösungen wurden in die Excel-Datei geschrieben.\n" +
                "Sheets: Lösung 1–3, Protokoll L1–L3, Diagnose & Vergleich",
                "Export erfolgreich", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Ausgewählten Schüler aus der Liste löschen ────────────────────────────
    [RelayCommand(CanExecute = nameof(KannGewaehltenLoeschen))]
    private void SchuelerAusListeLoeschen()
    {
        var s = GewaehlterSchueler;
        if (s == null || !IstGeladen) return;

        var res = MessageBox.Show(
            $"„{s.Name}“ wirklich löschen?\n\n" +
            "Wünsche anderer Schüler, die auf diesen Namen zeigen, werden ebenfalls entfernt.",
            "Schüler löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        string weg = s.Name;
        _alleSchueler.Remove(s);

        // Verweise anderer Schüler auf den gelöschten Namen entfernen
        foreach (var a in _alleSchueler)
        {
            a.ZusammenMit.RemoveAll(w =>
                string.Equals(w.ZielName, weg, StringComparison.OrdinalIgnoreCase));
            a.NichtNeben.RemoveAll(w =>
                string.Equals(w.ZielName, weg, StringComparison.OrdinalIgnoreCase));
        }

        GewaehlterSchueler = null;
        ReSyncNachSchuelerListe();
    }

    private bool KannGewaehltenLoeschen() => IstGeladen && GewaehlterSchueler != null;

    partial void OnIstGeladenChanged(bool value)
        => SchuelerAusListeLoeschenCommand.NotifyCanExecuteChanged();

    // ── Schülerdaten speichern ────────────────────────────────────────────────
    [RelayCommand]
    private void SchuelerSpeichern()
    {
        if (string.IsNullOrEmpty(ExcelPfad)) return;
        try
        {
            _updater.AllesSpeichern(ExcelPfad, _alleSchueler, _alleGruppen, Parameter);
            RaumplanVM.IstGeaendert = false;
            StatusText = $"Gespeichert: {ExcelPfad}";
            MessageBox.Show("Schülerdaten, Tischgruppen und Parameter wurden gespeichert.",
                "Gespeichert", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Speichern:\n{ex.Message}", "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    // ── Schülerliste hinzugefügt/gelöscht → alle Ansichten neu aufbauen ───────
    private void ReSyncNachSchuelerListe()
    {
        // Fortlaufende Nummerierung
        for (int i = 0; i < _alleSchueler.Count; i++)
            _alleSchueler[i].Nr = i + 1;

        // Linke Namensliste
        Schueler.Clear();
        foreach (var s in _alleSchueler) Schueler.Add(s);

        // Gewählten Schüler prüfen (evtl. gelöscht)
        if (GewaehlterSchueler != null && !_alleSchueler.Contains(GewaehlterSchueler))
            GewaehlterSchueler = null;

        // Abhängige Tabs / Editoren neu mit Datenquelle versorgen
        WuenscheEditor.SetzeDatenquelle(_alleSchueler);
        WuenscheEditor.LadeSchueler(GewaehlterSchueler);
        WunschMatrixVM.Initialisiere(_alleSchueler);
        FixierungsVM.Initialisiere(_alleSchueler, _alleGruppen);
        RaumplanVM.Initialisiere(_alleGruppen, _alleSchueler);
        SchuelerVM.Initialisiere(_alleSchueler);

        IstOptimiert = false;
        StatusText   = $"Schülerliste geändert ({_alleSchueler.Count}) – „Neuoptimierung“ ausführen.";
    }

    // ── Umbenennung auf Wunsch-Verweise übertragen ────────────────────────────
    private void PropagiereUmbenennung(string alt, string neu)
    {
        foreach (var s in _alleSchueler)
            foreach (var w in s.AlleWuensche)
                if (string.Equals(w.ZielName, alt, StringComparison.Ordinal))
                    w.ZielName = neu;
    }

    // ── Namensprüfung: nicht leer, nicht doppelt ──────────────────────────────
    private bool NameIstGueltig(SchuelerZeileVM zeile, string neu)
    {
        if (string.IsNullOrWhiteSpace(neu)) return false;
        return !_alleSchueler.Any(s =>
            s != zeile.Schueler &&
            string.Equals(s.Name, neu, StringComparison.OrdinalIgnoreCase));
    }

    // ── Parameter ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void ParameterZuruecksetzen()
    {
        Parameter.AufStandardSetzen();
        StatusText = "Parameter auf Standardwerte zurückgesetzt.";
    }

    [RelayCommand]
    private void ParameterAnwenden()
    {
        if (!IstGeladen) return;
        Optimieren();
    }

    [RelayCommand]
    private void FixierungSetzen()
    {
        if (GewaehlterSchueler == null) return;
        if (string.IsNullOrWhiteSpace(FixGruppenname))
        {
            GewaehlterSchueler.FixGruppenNamen.Clear();
            GewaehlterSchueler.FixSitzplatzNr = null;
            StatusText = $"Fixierung von {GewaehlterSchueler.Name} entfernt.";
            return;
        }

        // Mehrere Gruppen durch Semikolon getrennt
        var namen = FixGruppenname.Split(';')
            .Select(n => n.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        var gefunden = new List<string>();
        var nichtGefunden = new List<string>();
        foreach (var n in namen)
        {
            var g = _alleGruppen.FirstOrDefault(g =>
                g.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (g != null) gefunden.Add(g.Name);
            else nichtGefunden.Add(n);
        }

        if (nichtGefunden.Count > 0)
        {
            MessageBox.Show(
                $"Nicht gefunden: {string.Join(", ", nichtGefunden)}",
                "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        GewaehlterSchueler.FixGruppenNamen.Clear();
        GewaehlterSchueler.FixGruppenNamen.AddRange(gefunden);

        // Platz-Nr nur bei genau einer Gruppe
        GewaehlterSchueler.FixSitzplatzNr = gefunden.Count == 1 &&
            int.TryParse(FixPlatzNr.Trim(), out int nr) ? nr : null;

        string info = string.Join(" oder ", gefunden);
        StatusText = $"Fixiert: {GewaehlterSchueler.Name} → {info}" +
            (GewaehlterSchueler.FixSitzplatzNr.HasValue
                ? $"/Platz {GewaehlterSchueler.FixSitzplatzNr}" : "");
    }

    [RelayCommand]
    private void FixierungEntfernen()
    {
        if (GewaehlterSchueler == null) return;
        GewaehlterSchueler.FixGruppenNamen.Clear();
        GewaehlterSchueler.FixSitzplatzNr = null;
        FixGruppenname = "";
        FixPlatzNr     = "";
        StatusText = $"Fixierung von {GewaehlterSchueler.Name} entfernt.";
    }

    partial void OnGewaehlterSchuelerChanged(Schueler? value)
    {
        SchuelerAusListeLoeschenCommand.NotifyCanExecuteChanged();
        WuenscheEditor.LadeSchueler(value);
        if (value == null) return;
        FixGruppenname = string.Join("; ", value.FixGruppenNamen);
        FixPlatzNr     = value.FixSitzplatzNr?.ToString() ?? "";
    }

    // ── Diagnose ──────────────────────────────────────────────────────────────
    private void AktualisiereDiagnose()
    {
        Diagnose.Clear();
        if (_ergebnis == null) return;

        var loesungen = _ergebnis.Loesungen;
        var beste     = _ergebnis.BesteLoesung;

        void Add(string kriterium, Func<Loesung, string> wert) =>
            Diagnose.Add(new DiagnoseZeile
            {
                Kriterium = kriterium,
                Wert1     = loesungen.Count > 0 ? wert(loesungen[0]) : "-",
                Wert2     = loesungen.Count > 1 ? wert(loesungen[1]) : "-",
                Wert3     = loesungen.Count > 2 ? wert(loesungen[2]) : "-",
                BesterWert= beste != null ? wert(beste) : "-",
                BestIndex = beste != null ? loesungen.IndexOf(beste) : -1,
            });

        Add("Gesamtscore",           l => l.GesamtScore.ToString("F0"));
        Add("Prio-3 erfüllt",        l => $"{l.Prio3Erfuellt}/{l.Prio3Gesamt}");
        Add("Prio-3 vollständig",    l => l.Prio3Vollstaendig ? "JA" : "NEIN");
        Add("Prio-2 erfüllt",        l => $"{l.Prio2Erfuellt}/{l.Prio2Gesamt}");
        Add("Prio-1 erfüllt",        l => $"{l.Prio1Erfuellt}/{l.Prio1Gesamt}");
        Add("Sehschwäche vorne",     l => $"{l.SehschwaeacheVorne}/{l.SehschwaeacheGesamt}");
        Add("Geschlechterverteilung",l => l.GeschlechterScore.ToString("P0"));
        Add("Warnungen",             l => l.Warnungen.Count.ToString());
    }
}

// ── Diagnose-Zeile ────────────────────────────────────────────────────────────
public class DiagnoseZeile
{
    public string Kriterium  { get; set; } = "";
    public string Wert1      { get; set; } = "";
    public string Wert2      { get; set; } = "";
    public string Wert3      { get; set; } = "";
    public string BesterWert { get; set; } = "";
    public int    BestIndex  { get; set; }

    public string Farbe1 => BestIndex == 0 ? "#E2EFDA" : "#FFFFFF";
    public string Farbe2 => BestIndex == 1 ? "#E2EFDA" : "#FFFFFF";
    public string Farbe3 => BestIndex == 2 ? "#E2EFDA" : "#FFFFFF";
}
