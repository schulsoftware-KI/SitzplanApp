using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SitzplanApp.Models;

namespace SitzplanApp.ViewModels;

// ── Eine Zeile in der Schüler-Tabelle ────────────────────────────────────────
// Kapselt ein Schueler-Modell und macht die Grunddaten editierbar.
// Wünsche / Verbote / Fixierung bleiben als reine Info-Spalten (werden weiter
// über die spezialisierten Tabs bearbeitet).
public partial class SchuelerZeileVM : ObservableObject
{
    private bool _laden;

    public Schueler Schueler { get; }

    public int Nr => Schueler.Nr;

    // ── Name (manuell, damit vor dem Setzen validiert werden kann) ────────────
    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            var neu = (value ?? "").Trim();
            if (neu == _name) return;

            // Beim Laden ohne Prüfung übernehmen
            if (!_laden)
            {
                bool ok = NamePruefen?.Invoke(this, neu) ?? true;
                if (!ok)
                {
                    // Ungültig (leer oder doppelt) → Anzeige auf alten Wert zurück
                    OnPropertyChanged();
                    return;
                }
            }

            string alt = _name;
            _name = neu;
            Schueler.Name = neu;
            OnPropertyChanged();

            if (!_laden) OnUmbenannt?.Invoke(alt, neu);
        }
    }

    // ── Editierbare Grunddaten ────────────────────────────────────────────────
    [ObservableProperty] private string _geschlecht = "";
    [ObservableProperty] private bool   _sehschwaeche;
    [ObservableProperty] private bool   _linkshaender;
    [ObservableProperty] private string _notizen = "";

    partial void OnGeschlechtChanged(string value)
    {
        if (_laden) return;
        Schueler.Geschlecht = (value ?? "").Trim().ToLower();
        OnGeaendert?.Invoke();
    }
    partial void OnSehschwaecheChanged(bool value)
    {
        if (_laden) return;
        Schueler.Sehschwaeche = value;
        OnGeaendert?.Invoke();
    }
    partial void OnLinkshaenderChanged(bool value)
    {
        if (_laden) return;
        Schueler.Linkshaender = value;
        OnGeaendert?.Invoke();
    }
    partial void OnNotizenChanged(string value)
    {
        if (_laden) return;
        Schueler.Notizen = value ?? "";
        // Notizen sind für die Optimierung irrelevant → nur als geändert melden,
        // Neuoptimierung wird trotzdem angeboten (schadet nicht).
        OnGeaendert?.Invoke();
    }

    // ── Info-Spalten (read-only) ──────────────────────────────────────────────
    public string WuenscheText =>
        string.Join("   ", Schueler.AlleWuensche.Select(w => w.ToString()));

    public string FixierungText
    {
        get
        {
            if (Schueler.FixGruppenNamen.Count == 0) return "";
            string g = string.Join(" oder ", Schueler.FixGruppenNamen);
            return Schueler.FixSitzplatzNr.HasValue && Schueler.FixGruppenNamen.Count == 1
                ? $"{g}/Platz {Schueler.FixSitzplatzNr}"
                : g;
        }
    }

    public string VerboteText => Schueler.VerboteText;

    // ── Callbacks (werden vom SchuelerVM gesetzt) ─────────────────────────────
    public Func<SchuelerZeileVM, string, bool>? NamePruefen { get; set; }
    public Action<string, string>?              OnUmbenannt { get; set; }
    public Action?                              OnGeaendert { get; set; }

    public SchuelerZeileVM(Schueler s)
    {
        Schueler = s;
        _laden = true;
        _name        = s.Name;
        Geschlecht   = s.Geschlecht;
        Sehschwaeche = s.Sehschwaeche;
        Linkshaender = s.Linkshaender;
        Notizen      = s.Notizen;
        _laden = false;
    }

    /// <summary>Info-Spalten neu berechnen (z. B. nach Änderungen in anderen Tabs).</summary>
    public void AktualisiereInfo()
    {
        OnPropertyChanged(nameof(Nr));
        OnPropertyChanged(nameof(WuenscheText));
        OnPropertyChanged(nameof(FixierungText));
        OnPropertyChanged(nameof(VerboteText));
    }
}

// ── ViewModel für den Schüler-Tab (editierbare Tabelle) ──────────────────────
public partial class SchuelerVM : ObservableObject
{
    public ObservableCollection<SchuelerZeileVM> Zeilen { get; } = new();

    // In der Tabelle markierte Zeile → meldet den Schüler nach oben, damit das
    // linke Dialogfeld (Wünsche/Verbote) automatisch auf ihn springt.
    [ObservableProperty] private SchuelerZeileVM? _gewaehlteZeile;

    partial void OnGewaehlteZeileChanged(SchuelerZeileVM? value)
    {
        if (value != null) OnSchuelerGewaehlt?.Invoke(value.Schueler);
    }

    // Ausgewählte Zeile → MainViewModel setzt GewaehlterSchueler
    public Action<Schueler>? OnSchuelerGewaehlt { get; set; }

    // Gleiche Listen-Referenz wie im MainViewModel (_alleSchueler) → Add/Remove
    // wirkt direkt auf die Optimierungs-Datenbasis.
    private List<Schueler> _alle = new();

    [ObservableProperty] private bool _istGeladen;

    // ── Callbacks an das MainViewModel ────────────────────────────────────────
    // Grunddaten-Feld geändert (Geschlecht/Seh/Links/Notizen)
    public Action?                OnGeaendert      { get; set; }
    // Liste geändert (Schüler hinzugefügt/gelöscht) → volle Re-Synchronisation
    public Action?                OnListeGeaendert { get; set; }
    // Umbenennung (alt, neu) → Wunsch-Verweise nachziehen
    public Action<string, string>? OnUmbenannt     { get; set; }
    // Namensprüfung (nicht leer, nicht doppelt)
    public Func<SchuelerZeileVM, string, bool>? NamePruefen { get; set; }

    public void Initialisiere(List<Schueler> schueler)
    {
        _alle = schueler;
        BaueZeilen();
        IstGeladen = true;
    }

    private void BaueZeilen()
    {
        GewaehlteZeile = null;
        Zeilen.Clear();
        int i = 1;
        foreach (var s in _alle)
        {
            s.Nr = i++;                       // fortlaufend neu nummerieren
            var z = new SchuelerZeileVM(s)
            {
                NamePruefen = (zeile, neu) => NamePruefen?.Invoke(zeile, neu) ?? true,
                OnUmbenannt = (alt, neu)   => OnUmbenannt?.Invoke(alt, neu),
                OnGeaendert = ()           => OnGeaendert?.Invoke(),
            };
            Zeilen.Add(z);
        }
    }

    // ── Neuer Schüler ─────────────────────────────────────────────────────────
    [RelayCommand]
    private void NeuerSchueler()
    {
        if (!IstGeladen) return;

        string basis = "Neuer Schüler";
        string name  = basis;
        int n = 1;
        while (_alle.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{basis} {++n}";

        _alle.Add(new Schueler { Nr = _alle.Count + 1, Name = name });
        OnListeGeaendert?.Invoke();   // MainVM synchronisiert alle Ansichten neu
    }

    // ── Schüler löschen ───────────────────────────────────────────────────────
    [RelayCommand]
    private void SchuelerLoeschen(SchuelerZeileVM? zeile)
    {
        if (zeile == null || !IstGeladen) return;

        var res = MessageBox.Show(
            $"„{zeile.Schueler.Name}“ wirklich löschen?\n\n" +
            "Wünsche anderer Schüler, die auf diesen Namen zeigen, werden ebenfalls entfernt.",
            "Schüler löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        string weg = zeile.Schueler.Name;
        _alle.Remove(zeile.Schueler);

        // Verweise anderer Schüler auf den gelöschten Namen entfernen
        foreach (var s in _alle)
        {
            s.ZusammenMit.RemoveAll(w =>
                string.Equals(w.ZielName, weg, StringComparison.OrdinalIgnoreCase));
            s.NichtNeben.RemoveAll(w =>
                string.Equals(w.ZielName, weg, StringComparison.OrdinalIgnoreCase));
        }

        OnListeGeaendert?.Invoke();
    }
}
