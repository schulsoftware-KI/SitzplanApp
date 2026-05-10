using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SitzplanApp.ViewModels;

namespace SitzplanApp.Views;

public partial class RaumplanTab : UserControl
{
    private RaumplanVM VM => (RaumplanVM)DataContext;

    private RaumplanGruppeVM? _dragging;
    private bool _isDragging;

    public RaumplanTab()
    {
        InitializeComponent();
    }

    // ── Drag starten ──────────────────────────────────────────────────────────
    private void Gruppe_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not RaumplanGruppeVM vm) return;

        _dragging   = vm;
        _isDragging = false;

        // Offset: wo im Rechteck wurde geklickt
        var pos = e.GetPosition(cnvRaum);
        vm.DragOffsetX = pos.X - vm.CanvasLeft;
        vm.DragOffsetY = pos.Y - vm.CanvasTop;

        fe.CaptureMouse();
        e.Handled = true;
    }

    private void Gruppe_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe) return;

        var pos = e.GetPosition(cnvRaum);
        _isDragging = true;
        VM.BewegGruppe(_dragging, pos.X, pos.Y);
        e.Handled = true;
    }

    private void Gruppe_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        fe.ReleaseMouseCapture();

        if (_dragging != null)
        {
            if (_isDragging)
            {
                VM.DropGruppe(_dragging);
            }
            else
            {
                // Nur Klick → Editor öffnen/schließen
                var vm = _dragging;
                bool wasSelected = vm.IstAusgewaehlt;
                VM.WaehlGruppe(wasSelected ? null : vm);
            }
        }

        _dragging   = null;
        _isDragging = false;
        e.Handled   = true;
    }

    // ── Canvas-Klick (Hintergrund) → Auswahl aufheben ────────────────────────
    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == cnvRaum)
            VM.WaehlGruppe(null);
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        // Wird durch Gruppe_MouseMove behandelt
    }

    // ── Editor-Handler ────────────────────────────────────────────────────────
    private RaumplanGruppeVM? GetVmFromSender(object sender)
    {
        var fe = sender as FrameworkElement;
        while (fe != null)
        {
            if (fe.DataContext is RaumplanGruppeVM vm) return vm;
            fe = fe.Parent as FrameworkElement ??
                 (fe as FrameworkElement)?.TemplatedParent as FrameworkElement;
        }
        return null;
    }

    private void BtnPlus_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetVmFromEvent(sender);
        if (vm != null) VM.SitzplaetzeAendern(vm, +1);
    }

    private void BtnMinus_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetVmFromEvent(sender);
        if (vm != null) VM.SitzplaetzeAendern(vm, -1);
    }

    private void BtnAusrichtung_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetVmFromEvent(sender);
        if (vm != null) VM.AusrichtungToggle(vm);
    }

    private void BtnLoeschen_Click(object sender, RoutedEventArgs e)
    {
        var vm = GetVmFromEvent(sender);
        if (vm != null) VM.GruppeLoschenCommand.Execute(vm);
    }

    private void TbName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var vm = GetVmFromEvent(sender);
        if (vm != null) VM.NameAendern(vm, tb.Text);
    }

    // DataContext aus dem visuellen Baum holen
    private RaumplanGruppeVM? GetVmFromEvent(object sender)
    {
        var fe = sender as FrameworkElement;
        while (fe != null)
        {
            if (fe.DataContext is RaumplanGruppeVM vm) return vm;
            fe = VisualParent(fe);
        }
        return null;
    }

    private static FrameworkElement? VisualParent(FrameworkElement fe)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(fe);
        while (parent != null)
        {
            if (parent is FrameworkElement p) return p;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
