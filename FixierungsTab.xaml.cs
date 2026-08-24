using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SitzplanApp.ViewModels;

namespace SitzplanApp.Views;

public partial class FixierungsTab : UserControl
{
    private FixierungsVM VM => (FixierungsVM)DataContext;

    public Action<Models.Schueler?>? MarkierungCallback { get; set; }

    public FixierungsTab()
    {
        InitializeComponent();
    }

    // ── Klick auf Schüler in Liste ────────────────────────────────────────────
    private void ListSchueler_Click(object sender, MouseButtonEventArgs e)
    {
        if (lstSchueler.SelectedItem is not FixierungsSchuelerVM vm) return;
        VM.WaehlSchueler(vm);
    }

    // ── Doppelklick auf Schüler → im Sitzplan zeigen (nicht mehr aufheben) ─────
    private void ListSchueler_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (lstSchueler.SelectedItem is not FixierungsSchuelerVM vm) return;
        if (vm.IstFixiert)
            MarkierungCallback?.Invoke(vm.Schueler);
        // nicht fixiert → nichts (Aufheben passiert jetzt am Sitzplatz)
    }

    // ── Doppelklick auf Platz → fixieren / aufheben ───────────────────────────
    private void Platz_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;                       // nur Doppelklick
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not FixierungsPlatzVM platz) return;
        VM.PlatzGeklickt(platz);
    }

    // ── Doppelklick auf Gruppen-Überschrift → an ganze Gruppe fixieren/lösen ───
    private void Gruppe_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;                       // nur Doppelklick
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not GruppeVM g) return;

        var ergebnis = VM.GruppeGeklickt(g.Name);
        if (ergebnis == FixierungsVM.GruppeFixResultat.GruppeVoll)
            MessageBox.Show(
                $"Die Gruppe „{g.Name}“ hat nicht genügend Plätze – es sind bereits " +
                "so viele Schüler fixiert wie Plätze vorhanden sind.",
                "Gruppe voll", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── Löschbutton in Liste → Gruppen-/Oder-Fixierung aufheben ───────────────
    private void BtnAufheben_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;   // verhindert, dass zusätzlich der Listen-Klick feuert
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not FixierungsSchuelerVM vm) return;
        VM.FixierungAufheben(vm);
    }
}
