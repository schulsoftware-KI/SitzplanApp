using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SitzplanApp.Models;

namespace SitzplanApp.ViewModels;

// ── Eine Tischgruppe im Raumplan ──────────────────────────────────────────────
public partial class RaumplanGruppeVM : ObservableObject
{
    // Raster-Konstanten
    public const double ZELLE_B  = 140.0;
    public const double ZELLE_H  = 100.0;
    public const double PLATZ_B  = 116.0;
    public const double PLATZ_H  = 78.0;
    public const double OFFSET_X = 20.0;
    public const double OFFSET_Y = 60.0; // Platz für Tafel

    public Tischgruppe Gruppe { get; }

    // Rasterposition (1-basiert)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasLeft))]
    [NotifyPropertyChangedFor(nameof(CanvasTop))]
    private int _reihe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasLeft))]
    [NotifyPropertyChangedFor(nameof(CanvasTop))]
    private int _spalte;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breite))]
    [NotifyPropertyChangedFor(nameof(Hoehe))]
    [NotifyPropertyChangedFor(nameof(AusrichtungText))]
    private bool _istVertikal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Breite))]
    [NotifyPropertyChangedFor(nameof(Hoehe))]
    private int _sitzplaetze;

    [ObservableProperty] private string _name = "";

    // Canvas-Position
    public double CanvasLeft => OFFSET_X + (Spalte - 1) * ZELLE_B;
    public double CanvasTop  => OFFSET_Y + (Reihe  - 1) * ZELLE_H;

    // Größe abhängig von Ausrichtung und Sitzplatzzahl
    public double Breite => IstVertikal
        ? PLATZ_B + 8
        : Sitzplaetze * PLATZ_B + 8;
    public double Hoehe => IstVertikal
        ? Sitzplaetze * PLATZ_H + 8
        : PLATZ_H + 8;

    public string AusrichtungText => IstVertikal ? "↕ Vertikal" : "↔ Horizontal";

    // Auswahl/Bearbeitung
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RandFarbe))]
    [NotifyPropertyChangedFor(nameof(RandStaerke))]
    private bool _istAusgewaehlt;

    [ObservableProperty] private bool _zeigeEditor;
    [ObservableProperty] private bool _hatKollision;

    public string RandFarbe   => IstAusgewaehlt ? "#E67E22" : HatKollision ? "#C0392B" : "#2E75B6";
    public double RandStaerke => IstAusgewaehlt ? 3.0 : 1.5;

    // Drag-State
    public double DragOffsetX { get; set; }
    public double DragOffsetY { get; set; }

    public RaumplanGruppeVM(Tischgruppe g)
    {
        Gruppe      = g;
        _reihe      = g.Reihe;
        _spalte     = g.Spalte;
        _istVertikal = g.IstVertikal;
        _sitzplaetze = g.Sitzplaetze;
        _name       = g.Name;
    }

    // Änderungen ins Modell zurückschreiben
    public void SyncZuModell()
    {
        Gruppe.Reihe      = Reihe;
        Gruppe.Spalte     = Spalte;
        Gruppe.IstVertikal = IstVertikal;
        Gruppe.Sitzplaetze = Sitzplaetze;
        Gruppe.Name       = Name;
    }
}

// ── Haupt-ViewModel für den Raumplan-Tab ──────────────────────────────────────
public partial class RaumplanVM : ObservableObject
{
    public ObservableCollection<RaumplanGruppeVM> Gruppen { get; } = new();

    private List<Tischgruppe>    _alleGruppen  = new();
    private List<Schueler>       _alleSchueler = new();
    public  Action?              OnGeaendert   { get; set; }

    [ObservableProperty] private bool   _istGeaendert = false;
    [ObservableProperty] private string _kollisionsText = "";
    [ObservableProperty] private bool   _zeigeKollision = false;

    // Canvas-Gesamtgröße
    public double CanvasBreite => Gruppen.Count == 0 ? 800 :
        Gruppen.Max(g => g.CanvasLeft + g.Breite) + RaumplanGruppeVM.OFFSET_X + 20;
    public double CanvasHoehe => Gruppen.Count == 0 ? 600 :
        Gruppen.Max(g => g.CanvasTop + g.Hoehe) + RaumplanGruppeVM.OFFSET_Y + 20;

    public void Initialisiere(List<Tischgruppe> gruppen, List<Schueler> schueler)
    {
        _alleGruppen  = gruppen;
        _alleSchueler = schueler;
        Gruppen.Clear();
        foreach (var g in gruppen)
            Gruppen.Add(new RaumplanGruppeVM(g));
        IstGeaendert = false;
        PruefeKollisionen();
    }

    // ── Auswahl ───────────────────────────────────────────────────────────────
    public void WaehlGruppe(RaumplanGruppeVM? vm)
    {
        foreach (var g in Gruppen) { g.IstAusgewaehlt = false; g.ZeigeEditor = false; }
        if (vm != null) { vm.IstAusgewaehlt = true; vm.ZeigeEditor = true; }
    }

    // ── Drag & Drop ───────────────────────────────────────────────────────────
    public void BewegGruppe(RaumplanGruppeVM vm, double canvasX, double canvasY)
    {
        // Zur Rasterposition umrechnen
        double relX = canvasX - vm.DragOffsetX - RaumplanGruppeVM.OFFSET_X;
        double relY = canvasY - vm.DragOffsetY - RaumplanGruppeVM.OFFSET_Y;

        int neueSpalte = Math.Max(1, (int)Math.Round(relX / RaumplanGruppeVM.ZELLE_B) + 1);
        int neueReihe  = Math.Max(1, (int)Math.Round(relY / RaumplanGruppeVM.ZELLE_H) + 1);

        vm.Spalte = neueSpalte;
        vm.Reihe  = neueReihe;
    }

    public void DropGruppe(RaumplanGruppeVM vm)
    {
        vm.SyncZuModell();
        IstGeaendert = true;
        PruefeKollisionen();
        OnGeaendert?.Invoke();
        OnPropertyChanged(nameof(CanvasBreite));
        OnPropertyChanged(nameof(CanvasHoehe));
    }

    // ── Editor-Aktionen ───────────────────────────────────────────────────────
    public void AusrichtungToggle(RaumplanGruppeVM vm)
    {
        vm.IstVertikal = !vm.IstVertikal;
        vm.SyncZuModell();
        IstGeaendert = true;
        OnGeaendert?.Invoke();
    }

    public void SitzplaetzeAendern(RaumplanGruppeVM vm, int delta)
    {
        int neu = vm.Sitzplaetze + delta;
        if (neu < 1 || neu > 10) return;
        vm.Sitzplaetze = neu;
        vm.SyncZuModell();
        IstGeaendert = true;
        OnGeaendert?.Invoke();
    }

    public void NameAendern(RaumplanGruppeVM vm, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        vm.Name = name.Trim();
        vm.SyncZuModell();
        IstGeaendert = true;
        OnGeaendert?.Invoke();
    }

    // ── Neue Gruppe ───────────────────────────────────────────────────────────
    [RelayCommand]
    public void NeueGruppe()
    {
        // Freie Position finden
        int maxNr = _alleGruppen.Count > 0 ? _alleGruppen.Max(g => g.Nr) + 1 : 1;
        int freieReihe = 1, freieSpalte = 1;

        // Nächste freie Rasterposition suchen
        var belegte = Gruppen.Select(g => (g.Reihe, g.Spalte)).ToHashSet();
        while (belegte.Contains((freieReihe, freieSpalte)))
        {
            freieSpalte++;
            if (freieSpalte > 10) { freieSpalte = 1; freieReihe++; }
        }

        var neueGruppe = new Tischgruppe
        {
            Nr         = maxNr,
            Name       = $"Gruppe {maxNr}",
            Reihe      = freieReihe,
            Spalte     = freieSpalte,
            Sitzplaetze = 4,
            IstVertikal = false,
        };

        _alleGruppen.Add(neueGruppe);
        var vm = new RaumplanGruppeVM(neueGruppe);
        Gruppen.Add(vm);
        WaehlGruppe(vm);
        IstGeaendert = true;
        OnGeaendert?.Invoke();
        OnPropertyChanged(nameof(CanvasBreite));
        OnPropertyChanged(nameof(CanvasHoehe));
    }

    // ── Gruppe löschen ────────────────────────────────────────────────────────
    [RelayCommand]
    public void GruppeLoschen(RaumplanGruppeVM vm)
    {
        if (vm == null) return;

        // Prüfen ob Schüler auf diese Gruppe fixiert sind
        var betroffene = _alleSchueler
            .Where(s => s.FixGruppenNamen.Contains(vm.Name))
            .Select(s => s.Name)
            .ToList();

        if (betroffene.Count > 0)
        {
            KollisionsText = $"⚠ Fixierungen auf diese Gruppe: {string.Join(", ", betroffene)}. Trotzdem löschen?";
            ZeigeKollision = true;
            _loeschenVm = vm;
            return;
        }

        DoGruppeLoschen(vm);
    }

    private RaumplanGruppeVM? _loeschenVm;

    [RelayCommand]
    public void LoeschenBestaetigen()
    {
        if (_loeschenVm != null) DoGruppeLoschen(_loeschenVm);
        ZeigeKollision = false;
        _loeschenVm = null;
    }

    [RelayCommand]
    public void LoeschenAbbrechen()
    {
        ZeigeKollision = false;
        _loeschenVm = null;
    }

    private void DoGruppeLoschen(RaumplanGruppeVM vm)
    {
        _alleGruppen.Remove(vm.Gruppe);

        // Collection neu aufbauen damit Canvas sauber rendert
        var aktuell = Gruppen.Where(g => g != vm).ToList();
        Gruppen.Clear();
        foreach (var g in aktuell)
            Gruppen.Add(g);

        IstGeaendert = true;
        PruefeKollisionen();
        OnGeaendert?.Invoke();
        OnPropertyChanged(nameof(CanvasBreite));
        OnPropertyChanged(nameof(CanvasHoehe));
    }

    // ── Kollisionserkennung ───────────────────────────────────────────────────
    private void PruefeKollisionen()
    {
        foreach (var g in Gruppen) g.HatKollision = false;

        var konflikte = new List<string>();
        for (int i = 0; i < Gruppen.Count; i++)
            for (int j = i + 1; j < Gruppen.Count; j++)
            {
                var a = Gruppen[i];
                var b = Gruppen[j];
                if (a.Reihe == b.Reihe && a.Spalte == b.Spalte)
                {
                    a.HatKollision = true;
                    b.HatKollision = true;
                    konflikte.Add($"{a.Name} & {b.Name}");
                }
            }

        if (konflikte.Count > 0)
        {
            KollisionsText  = "⚠ Überlappung: " + string.Join(", ", konflikte);
            ZeigeKollision  = true;
        }
        else
        {
            ZeigeKollision  = false;
            KollisionsText  = "";
        }
    }
}
