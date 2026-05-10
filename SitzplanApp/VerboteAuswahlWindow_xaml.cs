using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SitzplanApp.Views;

/// <summary>
/// Dialog zum Auswählen von Platzverboten für einen Schüler.
/// Zeigt alle Gruppen (fett, lila) und deren einzelne Plätze (normal) an.
/// Gefiltert per Suchfeld.
/// </summary>
public partial class VerboteAuswahlWindow : Window
{
    public VerboteAuswahlVM VM { get; }
    public List<string> GewaehlteOptionen { get; private set; } = new();

    public VerboteAuswahlWindow(string titel, List<string> alleOptionen, List<string> bereitsGewaehlt)
    {
        InitializeComponent();
        VM = new VerboteAuswahlVM(titel, alleOptionen, bereitsGewaehlt);
        DataContext = VM;
        Loaded += (_, _) => txtFilter.Focus();
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        GewaehlteOptionen = VM.AlleOptionen
            .Where(o => o.Gewaehlt)
            .Select(o => o.Wert)
            .ToList();
        DialogResult = true;
    }

    private void BtnAbbrechen_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void BtnAlleEntfernen_Click(object sender, RoutedEventArgs e)
    {
        foreach (var o in VM.AlleOptionen) o.Gewaehlt = false;
    }
}

// ── ViewModel ────────────────────────────────────────────────────────────────
public partial class VerboteAuswahlVM : ObservableObject
{
    public string Titel { get; }
    public List<VerbotOption> AlleOptionen { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GefilterteOptionen))]
    private string _filter = "";

    [ObservableProperty]
    private int _anzahlGewaehlt;

    public IEnumerable<VerbotOption> GefilterteOptionen =>
        string.IsNullOrWhiteSpace(Filter)
            ? AlleOptionen
            : AlleOptionen.Where(o =>
                o.Anzeigename.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    public VerboteAuswahlVM(string titel, List<string> alleOptionen, List<string> bereitsGewaehlt)
    {
        Titel = titel;
        foreach (var opt in alleOptionen)
        {
            var eintrag = new VerbotOption(opt, bereitsGewaehlt.Contains(opt), this);
            AlleOptionen.Add(eintrag);
        }
        AktualisierAnzahl();
    }

    public void AktualisierAnzahl() =>
        AnzahlGewaehlt = AlleOptionen.Count(o => o.Gewaehlt);
}

// ── Einzelner Eintrag ────────────────────────────────────────────────────────
public partial class VerbotOption : ObservableObject
{
    private readonly VerboteAuswahlVM _parent;

    /// <summary>Roher Wert z.B. "Gruppe A" oder "Gruppe A/2"</summary>
    public string Wert { get; }

    /// <summary>Anzeigename mit Einrückung für Plätze</summary>
    public string Anzeigename { get; }

    /// <summary>true wenn kein "/" enthalten → ganze Gruppe</summary>
    public bool IstGanzeGruppe { get; }

    // Darstellungs-Properties für XAML-Binding
    public string Schriftgewicht => IstGanzeGruppe ? "SemiBold" : "Normal";
    public double Schriftgroesse => IstGanzeGruppe ? 12.0 : 11.5;
    public string Farbe          => IstGanzeGruppe ? "#5A2D82" : "#333333";

    [ObservableProperty]
    private bool _gewaehlt;

    public VerbotOption(string wert, bool gewaehlt, VerboteAuswahlVM parent)
    {
        Wert           = wert;
        IstGanzeGruppe = !wert.Contains('/');
        Anzeigename    = IstGanzeGruppe ? $"📦 {wert}  (ganze Gruppe)" : $"    └ Platz {wert.Split('/')[1]}  ({wert.Split('/')[0]})";
        _gewaehlt      = gewaehlt;
        _parent        = parent;
    }

    partial void OnGewaehltChanged(bool value)
        => _parent.AktualisierAnzahl();
}
