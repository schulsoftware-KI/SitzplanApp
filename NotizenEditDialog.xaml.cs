using System.ComponentModel;
using System.Windows;

namespace SitzplanApp.Views;

/// <summary>
/// Dialog zum Anzeigen und Bearbeiten der Notizen eines Schülers.
/// Wird per Rechtsklick auf einen belegten Sitzplatz in den Lösungs-Tabs geöffnet.
/// Nach OK steht die geänderte Notiz in NeueNotiz.
/// MainWindow.ZeigeNotizenDialog() schreibt sie in Schueler.Notizen zurück.
/// Das Speichern in Excel erfolgt über den regulären Speichern-Button
/// (ExcelUpdateService.AllesSpeichern → Spalte 14).
/// </summary>
public partial class NotizenEditDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _notiz = "";
    public string Notiz
    {
        get => _notiz;
        set
        {
            if (_notiz == value) return;
            _notiz = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notiz)));
        }
    }

    public string Titel     { get; }
    public string NeueNotiz => Notiz;

    public NotizenEditDialog(string schuelerName, string aktuelleNotiz)
    {
        InitializeComponent();
        DataContext = this;
        Titel  = $"Notiz – {schuelerName}";
        Notiz  = aktuelleNotiz;

        Loaded += (_, _) =>
        {
            txtNotiz.Focus();
            txtNotiz.CaretIndex = txtNotiz.Text.Length;
        };
    }

    private void BtnSpeichern_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void BtnAbbrechen_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
