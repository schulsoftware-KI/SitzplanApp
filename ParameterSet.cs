namespace SitzplanApp.Models;

/// <summary>
/// Gewichtungsparameter aus dem Excel-Sheet "Parameter".
/// Alle Werte sind positive Zahlen — das Programm wendet Vorzeichen intern an.
/// </summary>
public class ParameterSet
{
    public double W_PRIO2_ERF    { get; set; } = 150;
    public double W_PRIO2_STRAF  { get; set; } = 200;
    public double W_PRIO1_ERF    { get; set; } = 50;
    public double W_PRIO1_STRAF  { get; set; } = 60;
    public double W_SEH_VORNE    { get; set; } = 100;
    public double W_GESCHLECHT   { get; set; } = 20;
    public double W_LINKSHAENDER { get; set; } = 30;

    /// <summary>Standardwerte — verwendet wenn kein Parameter-Sheet vorhanden.</summary>
    public static ParameterSet Standard() => new();
}
