using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SitzplanApp.Models;

namespace SitzplanApp.Views;

public partial class NamensAuswahlWindow : Window
{
    public NamensAuswahlVM VM { get; }
    public List<string> GewaehlteNamen { get; private set; } = new();
    public List<(string Name, Prioritaet Prio)> GewaehlteNamenMitPrio { get; private set; } = new();

    public NamensAuswahlWindow(
        string titel,
        List<string> alleNamen,
        List<string> bereitsGewaehlt,
        int maxAuswahl = 2,
        Dictionary<string, Prioritaet>? prioBelegung = null)
    {
        InitializeComponent();
        VM = new NamensAuswahlVM(titel, alleNamen, bereitsGewaehlt, maxAuswahl, prioBelegung);
        DataContext = VM;
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        var ausgewaehlt = VM.Namen.Where(n => n.Gewaehlt).ToList();
        GewaehlteNamen        = ausgewaehlt.Select(n => n.Name).ToList();
        GewaehlteNamenMitPrio = ausgewaehlt.Select(n => (n.Name, n.Prio)).ToList();
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

// ── ViewModel ──────────────────────────────────────────────────────────────
public partial class NamensAuswahlVM : ObservableObject
{
    public string Titel { get; }
    public System.Collections.ObjectModel.ObservableCollection<NameEintrag> Namen { get; } = new();

    private readonly int _maxAuswahl;

    [ObservableProperty]
    private int _anzahlGewaehlt;

    public NamensAuswahlVM(string titel, List<string> alle, List<string> gewaehlt,
        int maxAuswahl = 2,
        Dictionary<string, Prioritaet>? prioBelegung = null)
    {
        Titel       = titel;
        _maxAuswahl = maxAuswahl;
        foreach (var n in alle)
        {
            var prio = prioBelegung != null && prioBelegung.TryGetValue(n, out var p)
                ? p : Prioritaet.Prio1;
            Namen.Add(new NameEintrag(n, gewaehlt.Contains(n), prio, this));
        }
        AktualisierAnzahl();
    }

    public void AktualisierAnzahl() =>
        AnzahlGewaehlt = Namen.Count(n => n.Gewaehlt);

    public bool DarfNochWaehlen => AnzahlGewaehlt < _maxAuswahl;

    [RelayCommand]
    private void Toggle(NameEintrag? eintrag)
    {
        if (eintrag == null) return;
        if (!eintrag.Gewaehlt && !DarfNochWaehlen)
            eintrag.Gewaehlt = false;
        AktualisierAnzahl();
    }
}

public partial class NameEintrag : ObservableObject
{
    private readonly NamensAuswahlVM _parent;

    public string Name { get; }

    [ObservableProperty]
    private bool _gewaehlt;

    [ObservableProperty]
    private Prioritaet _prio;

    public List<Prioritaet> AllePrios { get; } =
        new() { Prioritaet.Prio1, Prioritaet.Prio2, Prioritaet.Prio3 };

    public NameEintrag(string name, bool gewaehlt, Prioritaet prio, NamensAuswahlVM parent)
    {
        Name      = name;
        _gewaehlt = gewaehlt;
        _prio     = prio;
        _parent   = parent;
    }

    partial void OnGewaehltChanged(bool value)
    {
        if (value && !_parent.DarfNochWaehlen)
        {
            _gewaehlt = false;
            OnPropertyChanged(nameof(Gewaehlt));
            System.Windows.MessageBox.Show(
                $"Maximal {_parent.AnzahlGewaehlt} Namen auswählbar.",
                "Limit erreicht",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }
        _parent.AktualisierAnzahl();
    }
}
