using System.Windows;
using System.Windows.Controls;
using SitzplanApp.Models;
using SitzplanApp.ViewModels;
using SitzplanApp.Views;

namespace SitzplanApp;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        // Markierungs-Callback für Fixierungs-Tab setzen
        fixierungsTab.MarkierungCallback = s => VM.SchuelerMarkierenCommand.Execute(s);
        // Notiz-Dialog: MainWindow öffnet den Dialog (Owner bekannt)
        VM.OnNotizDialogAnfordern = schueler => ZeigeNotizenDialog(schueler);
        // Startbreite um 5% reduzieren, Höhe an Bildschirm anpassen
        Loaded += (_, _) =>
        {
            Width = Width * 0.95;
            var maxH = SystemParameters.WorkArea.Height - 20;
            if (Height > maxH)
            {
                Height = maxH;
                Top = 0;
            }
        };
    }

    // ── Nicht neben auswählen ─────────────────────────────────────────────────
    private void BtnNichtNeben_Click(object sender, RoutedEventArgs e)
    {
        var s = VM.GewaehlterSchueler;
        if (s == null) return;

        var alle      = VM.Schueler.Where(x => x != s).Select(x => x.Name).ToList();
        var gewaehlt  = s.NichtNeben.Select(w => w.ZielName).ToList();
        var prioBel   = s.NichtNeben.ToDictionary(w => w.ZielName, w => w.Prio);

        var dlg = new NamensAuswahlWindow(
            $"Nicht neben: {s.Name}", alle, gewaehlt,
            prioBelegung: prioBel) { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            s.NichtNeben.Clear();
            foreach (var (name, prio) in dlg.GewaehlteNamenMitPrio)
                s.NichtNeben.Add(new Wunsch { ZielName = name, Prio = prio, IstAntiWunsch = true });
            VM.GewaehlterSchueler = null;
            VM.GewaehlterSchueler = s;
        }
    }

    // ── Zusammen mit auswählen ────────────────────────────────────────────────
    private void BtnZusammen_Click(object sender, RoutedEventArgs e)
    {
        var s = VM.GewaehlterSchueler;
        if (s == null) return;

        var alle     = VM.Schueler.Where(x => x != s).Select(x => x.Name).ToList();
        var gewaehlt = s.ZusammenMit.Select(w => w.ZielName).ToList();
        var prioBel  = s.ZusammenMit.ToDictionary(w => w.ZielName, w => w.Prio);

        var dlg = new NamensAuswahlWindow(
            $"Zusammen mit: {s.Name}", alle, gewaehlt,
            prioBelegung: prioBel) { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            s.ZusammenMit.Clear();
            foreach (var (name, prio) in dlg.GewaehlteNamenMitPrio)
                s.ZusammenMit.Add(new Wunsch { ZielName = name, Prio = prio, IstAntiWunsch = false });
            VM.GewaehlterSchueler = null;
            VM.GewaehlterSchueler = s;
        }
    }

    // ── Schüler Doppelklick → im Sitzplan markieren ───────────────────────────
    private void ListSchueler_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SchuelerMarkierenCommand.Execute(VM.GewaehlterSchueler);
    }

    // ── Markierung aufheben ───────────────────────────────────────────────────
    private void BtnMarkierungAufheben_Click(object sender, RoutedEventArgs e)
    {
        VM.SchuelerMarkierenCommand.Execute(null);
    }
    // ── Fixierungsgruppen auswählen ───────────────────────────────────────────
    private void BtnFixGruppeAuswaehlen_Click(object sender, RoutedEventArgs e)
    {
        var alle = VM.Gruppen.Select(g => g.Name).ToList();
        var gewaehlt = VM.FixGruppenname
            .Split(';')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var dlg = new NamensAuswahlWindow(
            "Fixierungsgruppen wählen (Oder-Logik)", alle, gewaehlt,
            maxAuswahl: alle.Count)
        { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            VM.FixGruppenname = string.Join("; ", dlg.GewaehlteNamen);
            // Automatisch setzen nach Popup-Auswahl
            VM.FixierungSetzenCommand.Execute(null);
        }
    }

    // ── Auto-Setzen bei Fokusverlust ──────────────────────────────────────────
    private void FixGruppenname_LostFocus(object sender, RoutedEventArgs e)
    {
        if (VM.GewaehlterSchueler != null && !string.IsNullOrWhiteSpace(VM.FixGruppenname))
            VM.FixierungSetzenCommand.Execute(null);
    }

    private void FixPlatzNr_LostFocus(object sender, RoutedEventArgs e)
    {
        if (VM.GewaehlterSchueler != null && !string.IsNullOrWhiteSpace(VM.FixGruppenname))
            VM.FixierungSetzenCommand.Execute(null);
    }

    // ── Drag & Drop Sitzplan ──────────────────────────────────────────────────
    private LoesungVM? GetLoesungVM(FrameworkElement fe)
    {
        var parent = fe as DependencyObject;
        while (parent != null)
        {
            if (parent is FrameworkElement p && p.DataContext is LoesungVM lvm) return lvm;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private Point _dragStartPunkt;

    private void Sitzplatz_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
            _dragStartPunkt = e.GetPosition(fe);
    }

    private void Sitzplatz_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // nicht verwendet
    }

    private void Sitzplatz_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not SitzplatzVM sp) return;
        if (sp.IstFrei) return;

        var pos = e.GetPosition(fe);
        if (Math.Abs(pos.X - _dragStartPunkt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPunkt.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var lvm = GetLoesungVM(fe);
        if (lvm == null) return;

        lvm.StarteDragVonPlatz(sp);
        var result = DragDrop.DoDragDrop(fe, sp.OriginalName, DragDropEffects.Move);

        if (result == DragDropEffects.None)
            lvm.BrecheDragAb();
        else
            lvm.AbschlussDrag();
    }

    private void Sitzplatz_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // nicht verwendet
    }

    private void Sitzplatz_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not SitzplatzVM ziel) return;
        var lvm = GetLoesungVM(fe);
        if (lvm == null || !lvm.IstEchterDrag) return;
        // Loslassen auf dem eigenen Platz → kein Drop, nur Markierung/Auswahl
        if (lvm.IstSelbstDrop(ziel)) { lvm.BrecheDragAb(); return; }
        lvm.DropAufPlatz(ziel);
    }

    private void Sitzplatz_DragOver(object sender, DragEventArgs e)
    {
        var lvm = sender is FrameworkElement fe ? GetLoesungVM(fe) : null;
        if (lvm == null || !lvm.IstEchterDrag)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Wartezone_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var lvm = GetLoesungVM(fe);
        if (lvm == null || !lvm.IstEchterDrag) return;
        var name = e.Data.GetData(typeof(string)) as string ?? "";
        lvm.DropInWartezone(name);
    }

    private void Wartezone_DragOver(object sender, DragEventArgs e)
    {
        // Nur echte Drags akzeptieren – verhindert dass ein Klick den Schüler
        // in die Wartezone befördert wenn die Maus minimal bewegt wurde
        var lvm = sender is FrameworkElement fe ? GetLoesungVM(fe) : null;
        if (lvm == null || !lvm.IstEchterDrag)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void WartezoneItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not string name) return;

        var lvm = GetLoesungVM(fe);
        if (lvm == null) return;

        lvm.StarteDragAusWartezone(name);
        var result = DragDrop.DoDragDrop(fe, name, DragDropEffects.Move);

        if (result == DragDropEffects.None)
            lvm.BrecheDragAb();
        else
            lvm.AbschlussDrag();
    }

    private void WartezoneItem_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // nicht verwendet – Drag startet über MouseLeave
    }

    // ── Prio Nicht-neben ──────────────────────────────────────────────────────
    private void CbPrioNN_Changed(object sender, SelectionChangedEventArgs e)
    {
        var s = VM.GewaehlterSchueler;
        if (s == null || sender is not ComboBox cb) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;
        var prio = (Prioritaet)int.Parse(item.Tag?.ToString() ?? "1");
        foreach (var w in s.NichtNeben) w.Prio = prio;
    }

    // ── Prio Zusammen ─────────────────────────────────────────────────────────
    private void CbPrioZM_Changed(object sender, SelectionChangedEventArgs e)
    {
        var s = VM.GewaehlterSchueler;
        if (s == null || sender is not ComboBox cb) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;
        var prio = (Prioritaet)int.Parse(item.Tag?.ToString() ?? "1");
        foreach (var w in s.ZusammenMit) w.Prio = prio;
    }

    // ── Rechtsklick auf Sitzplatz → Notiz-Dialog ─────────────────────────────
    // In MainWindow.xaml auf dem Sitzplatz-Border/Grid eintragen:
    //     MouseRightButtonUp="Sitzplatz_RightClick"
    private void Sitzplatz_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not SitzplatzVM sp) return;
        if (sp.IstFrei) return;
        var lvm = GetLoesungVM(fe);
        if (lvm == null) return;
        lvm.SitzplatzRechtsklick(sp);
        e.Handled = true;
    }

    private void ZeigeNotizenDialog(Schueler schueler)
    {
        var dlg = new NotizenEditDialog(schueler.Name, schueler.Notizen) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            schueler.Notizen = dlg.NeueNotiz;
            // Alle SitzplatzVMs in allen Lösungs-Tabs synchronisieren
            foreach (var lv in VM.Loesungen)
                lv.AktualisierNotizen(schueler.Name, dlg.NeueNotiz);
            VM.StatusText = $"Notiz für {schueler.Name} geändert – wird beim Speichern in Excel geschrieben.";
        }
    }

    // ── Verbote bearbeiten ────────────────────────────────────────────────────
    private void BtnVerboteBearbeiten_Click(object sender, RoutedEventArgs e)
    {
        var s = VM.GewaehlterSchueler;
        if (s == null) return;

        var alleOptionen = new List<string>();
        foreach (var g in VM.Gruppen)
        {
            alleOptionen.Add(g.Name);
            for (int p = 1; p <= g.Sitzplaetze; p++)
                alleOptionen.Add($"{g.Name}/{p}");
        }

        var bereitsVerboten = s.Verbote.Select(v => v.ToString()).ToList();

        var dlg = new VerboteAuswahlWindow(
            $"Verbote für: {s.Name}",
            alleOptionen,
            bereitsVerboten)
        { Owner = this };

        if (dlg.ShowDialog() == true)
        {
            s.Verbote.Clear();
            foreach (var token in dlg.GewaehlteOptionen)
                s.Verbote.Add(PlatzVerbot.Parse(token));

            var tmp = VM.GewaehlterSchueler;
            VM.GewaehlterSchueler = null;
            VM.GewaehlterSchueler = tmp;

            VM.StatusText = $"Verbote für {s.Name} gesetzt ({s.Verbote.Count}). Beim Speichern in Excel geschrieben.";
        }
    }
}
