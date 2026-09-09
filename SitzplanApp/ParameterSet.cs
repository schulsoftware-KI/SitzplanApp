using CommunityToolkit.Mvvm.ComponentModel;

namespace SitzplanApp.Models;

/// <summary>
/// Gewichtungsparameter aus dem Excel-Sheet "Parameter".
/// Alle Werte sind positive Zahlen — das Programm wendet Vorzeichen intern an.
/// ObservableObject: Änderungen im Parameter-Tab wirken direkt auf die Optimierung.
/// </summary>
public partial class ParameterSet : ObservableObject
{
    [ObservableProperty] private double _w_PRIO2_ERF    = 150;
    [ObservableProperty] private double _w_PRIO2_STRAF  = 200;
    [ObservableProperty] private double _w_PRIO1_ERF    = 50;
    [ObservableProperty] private double _w_PRIO1_STRAF  = 60;
    [ObservableProperty] private double _w_SEH_VORNE    = 100;
    [ObservableProperty] private double _w_GESCHLECHT   = 20;
    [ObservableProperty] private double _w_LINKSHAENDER = 30;

    /// <summary>Standardwerte — verwendet wenn kein Parameter-Sheet vorhanden.</summary>
    public static ParameterSet Standard() => new();

    /// <summary>Setzt alle Werte auf die Standardwerte zurück.</summary>
    public void AufStandardSetzen()
    {
        W_PRIO2_ERF    = 150;
        W_PRIO2_STRAF  = 200;
        W_PRIO1_ERF    = 50;
        W_PRIO1_STRAF  = 60;
        W_SEH_VORNE    = 100;
        W_GESCHLECHT   = 20;
        W_LINKSHAENDER = 30;
    }
}
