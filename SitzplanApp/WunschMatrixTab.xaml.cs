using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SitzplanApp.ViewModels;

namespace SitzplanApp.Views;

public partial class WunschMatrixTab : UserControl
{
    private WunschMatrixVM? VM => DataContext as WunschMatrixVM;

    public WunschMatrixTab()
    {
        InitializeComponent();
        // Beim Sichtbarwerden aus den aktuellen Daten neu aufbauen
        // (falls zwischenzeitlich im Chip-Panel geändert wurde)
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) VM?.AusDatenAktualisieren();
        };
    }

    // ── Header mit dem Zellbereich mitscrollen ────────────────────────────────
    private void svMain_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        svTop.ScrollToHorizontalOffset(e.HorizontalOffset);
        svLeft.ScrollToVerticalOffset(e.VerticalOffset);
    }

    // ── Zelle: links zyklen, rechts löschen ───────────────────────────────────
    private void Zelle_Left(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MatrixZelleVM z)
            VM?.ZelleGeklickt(z, links: true);
    }

    private void Zelle_Right(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is MatrixZelleVM z)
            VM?.ZelleGeklickt(z, links: false);
        e.Handled = true;
    }

    // ── Alle Wünsche löschen (mit Rückfrage) ──────────────────────────────────
    private void BtnAllesLoeschen_Click(object sender, RoutedEventArgs e)
    {
        if (VM == null) return;
        var r = MessageBox.Show(
            "Wirklich alle Zusammen- und Nicht-neben-Wünsche der ganzen Klasse löschen?",
            "Alle Wünsche löschen",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Yes) VM.LoescheAlles();
    }
}
