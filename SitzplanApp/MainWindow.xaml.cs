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

    // ── Wünsche-Suchfeld: Tastatursteuerung (Autocomplete) ────────────────────
    private void WunschSuche_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not WunschListeVM liste) return;

        switch (e.Key)
        {
            case System.Windows.Input.Key.Down:
                liste.MarkierungRunter();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Up:
                liste.MarkierungHoch();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Enter:
            case System.Windows.Input.Key.Tab:
                if (liste.VorschlaegeOffen)
                {
                    liste.UebernehmenMarkiert();
                    e.Handled = true;
                }
                break;
            case System.Windows.Input.Key.Escape:
                liste.SuchfeldLeeren();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Back:
                if (string.IsNullOrEmpty(tb.Text))
                {
                    liste.LetztenChipEntfernen();
                    e.Handled = true;
                }
                break;
        }
    }

    // ── Schüler Doppelklick → im Sitzplan markieren ───────────────────────────
    private void ListSchueler_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        VM.SchuelerMarkierenCommand.Execute(VM.GewaehlterSchueler);
    }

    // ── Tastatur-Navigation nach Vorname ──────────────────────────────────────
    // Bei Sortierung nach Vorname (SchuelerSortModus == 2) springt ein Buchstabe
    // zum passenden Vornamen statt zum Nachnamen (WPF-Standard). Mehrfaches
    // Drücken desselben Buchstabens läuft zyklisch durch die Treffer.
    private void LstSchueler_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (VM.SchuelerSortModus != 2) return;                 // nur im Vorname-Modus
        if (string.IsNullOrEmpty(e.Text)) return;

        char c = char.ToLowerInvariant(e.Text[0]);
        if (!char.IsLetter(c)) return;

        int n = lb.Items.Count;
        if (n == 0) return;
        int start = lb.SelectedIndex;

        for (int off = 1; off <= n; off++)
        {
            int idx = (start + off) % n;                       // ab aktueller Auswahl, zyklisch
            if (lb.Items[idx] is Schueler s &&
                char.ToLowerInvariant(ErsterBuchstabeVorname(s.Name)) == c)
            {
                lb.SelectedIndex = idx;
                lb.ScrollIntoView(lb.Items[idx]);
                break;
            }
        }
        e.Handled = true;                                       // Standard-TextSearch unterdrücken
    }

    // Erster Buchstabe des Vornamens aus „Nachname, Vorname" (ohne Komma: ganzer Name).
    private static char ErsterBuchstabeVorname(string name)
    {
        int k = name.IndexOf(',');
        string vor = (k >= 0 ? name[(k + 1)..] : name).Trim();
        return vor.Length > 0 ? vor[0] : '\0';
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

    // ── Zoom des Sitzplans ────────────────────────────────────────────────────
    private const double ZoomMin = 0.4;
    private const double ZoomMax = 1.5;

    private void ZoomAus_Click(object sender, RoutedEventArgs e) => ZoomSchritt(sender, -0.1);
    private void ZoomEin_Click(object sender, RoutedEventArgs e) => ZoomSchritt(sender, +0.1);

    private void ZoomSchritt(object sender, double delta)
    {
        if (sender is FrameworkElement fe && GetLoesungVM(fe) is LoesungVM lvm)
            lvm.Zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, Math.Round(lvm.Zoom + delta, 2)));
    }

    private void Sitzplan_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        if (sender is FrameworkElement fe && GetLoesungVM(fe) is LoesungVM lvm)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            lvm.Zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, Math.Round(lvm.Zoom + delta, 2)));
            e.Handled = true; // verhindert gleichzeitiges Scrollen
        }
    }

    private void Einpassen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (GetLoesungVM(fe) is not LoesungVM lvm) return;

        // ScrollViewer dieses Lösungs-Tabs finden, um die sichtbare Fläche zu messen
        var root = GetLoesungRoot(fe);
        var sv   = FindDescendant<ScrollViewer>(root);
        if (sv == null || sv.ViewportWidth < 10 || sv.ViewportHeight < 10) return;

        double inhaltBreite = lvm.PlanBreite;
        double inhaltHoehe  = lvm.PlanHoehe + 34; // Tafel-Balken oben

        double faktor = Math.Min(sv.ViewportWidth  / inhaltBreite,
                                 sv.ViewportHeight / inhaltHoehe);
        // Nicht über 100 % hinaus „einpassen", nur verkleinern
        lvm.Zoom = Math.Max(ZoomMin, Math.Min(1.0, Math.Round(faktor, 2)));
    }

    // Äußerstes an diese LoesungVM gebundenes Element (Template-Wurzel des Tabs)
    private static FrameworkElement? GetLoesungRoot(FrameworkElement fe)
    {
        FrameworkElement? letzte = null;
        DependencyObject? cur = fe;
        while (cur != null)
        {
            if (cur is FrameworkElement p)
            {
                if (p.DataContext is LoesungVM) letzte = p;
                else if (letzte != null) break; // DataContext wechselt → über dem Template
            }
            cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
        }
        return letzte;
    }

    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root == null) return null;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T treffer) return treffer;
            var tiefer = FindDescendant<T>(c);
            if (tiefer != null) return tiefer;
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
