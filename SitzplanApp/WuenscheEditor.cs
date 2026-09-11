using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

// ─────────────────────────────────────────────────────────────────────────────
//  Inline-Wünsche-Eingabe: Chips + Autocomplete statt Modal-Dialog
//  Drop-in: eine Datei. VMs liegen in SitzplanApp.ViewModels, der Converter
//  in SitzplanApp (wie die übrigen Converter).
// ─────────────────────────────────────────────────────────────────────────────

namespace SitzplanApp.ViewModels
{
    // ── Ein Chip = ein Wunsch-Eintrag (Pille mit Prio-Badge und ×) ───────────
    public partial class WunschChipVM : ObservableObject
    {
        public Wunsch Wunsch { get; }
        private readonly WunschListeVM _liste;

        public string Name => Wunsch.ZielName;
        public bool   IstAntiWunsch => Wunsch.IstAntiWunsch;

        [ObservableProperty] private Prioritaet _prio;

        public WunschChipVM(Wunsch w, WunschListeVM liste)
        {
            Wunsch = w;
            _liste = liste;
            _prio  = w.Prio;
        }

        // Rundes Badge: nur die Ziffer …
        public string PrioText => ((int)Prio).ToString();

        // … Farbe des Badges nach App-Konvention (P1 grün, P2 orange, P3 rot)
        public string PrioFarbe => Prio switch
        {
            Prioritaet.Prio3 => "#C0392B",
            Prioritaet.Prio2 => "#E67E22",
            _                => "#27AE60",
        };

        public string PrioTooltip => Prio switch
        {
            Prioritaet.Prio3 => "Prio 3 – Zwingend (klicken für Prio 1)",
            Prioritaet.Prio2 => "Prio 2 – Stark (klicken für Prio 3)",
            _                => "Prio 1 – Wunsch (klicken für Prio 2)",
        };

        // Leichte Kategorie-Tönung des Chip-Körpers
        public string ChipHintergrund => IstAntiWunsch ? "#FBEAEA" : "#EAF3EC";
        public string ChipVordergrund => IstAntiWunsch ? "#922B21" : "#1E5631";

        partial void OnPrioChanged(Prioritaet value)
        {
            Wunsch.Prio = value;
            OnPropertyChanged(nameof(PrioText));
            OnPropertyChanged(nameof(PrioFarbe));
            OnPropertyChanged(nameof(PrioTooltip));
        }

        [RelayCommand]
        private void PrioZyklus()
        {
            Prio = Prio switch
            {
                Prioritaet.Prio1 => Prioritaet.Prio2,
                Prioritaet.Prio2 => Prioritaet.Prio3,
                _                => Prioritaet.Prio1,
            };
            _liste.PrioGeaendert(this);
        }

        [RelayCommand]
        private void Entfernen() => _liste.Entfernen(this);
    }

    // ── Eine Kategorie (Zusammen ODER Nicht neben) für den gewählten Schüler ──
    public partial class WunschListeVM : ObservableObject
    {
        public const int MaxProKategorie = 2;

        public bool   IstAntiWunsch { get; }
        public string Titel         { get; }
        public string TitelFarbe    => IstAntiWunsch ? "#C0392B" : "#1F7A4A";

        public ObservableCollection<WunschChipVM> Chips       { get; } = new();
        public ObservableCollection<string>       Vorschlaege { get; } = new();

        [ObservableProperty] private string _suchText = "";
        [ObservableProperty] private int    _highlightIndex = -1;
        [ObservableProperty] private bool   _vorschlaegeOffen;

        private Schueler? _schueler;
        private readonly WuenscheEditorVM _parent;

        public WunschListeVM(WuenscheEditorVM parent, bool istAnti, string titel)
        {
            _parent       = parent;
            IstAntiWunsch = istAnti;
            Titel         = titel;
        }

        public bool   IstVoll         => Chips.Count >= MaxProKategorie;
        public bool   KannTippen      => _schueler != null && !IstVoll;
        public string PlatzhalterText => _schueler == null ? "erst Schüler wählen"
                                        : IstVoll          ? "max. 2 erreicht"
                                                           : "Name tippen…";

        private List<Wunsch> ZielListe(Schueler s) => IstAntiWunsch ? s.NichtNeben : s.ZusammenMit;

        // Vom Editor aufgerufen, wenn ein anderer Schüler gewählt wird
        public void LadeFuer(Schueler? s)
        {
            _schueler = s;
            SuchText  = "";
            Vorschlaege.Clear();
            VorschlaegeOffen = false;
            HighlightIndex   = -1;

            Chips.Clear();
            if (s != null)
                foreach (var w in ZielListe(s))
                    Chips.Add(new WunschChipVM(w, this));

            NotifyZustand();
        }

        partial void OnSuchTextChanged(string value) => FiltereVorschlaege();

        private void FiltereVorschlaege()
        {
            Vorschlaege.Clear();
            HighlightIndex = -1;

            var q = SuchText?.Trim() ?? "";
            if (_schueler == null || IstVoll || q.Length == 0)
            {
                VorschlaegeOffen = false;
                return;
            }

            var schonDrin = new HashSet<string>(Chips.Select(c => c.Name));

            // Je nach Sortiermodus nach Vorname (2) oder Nachname (0/1) „springen":
            // Namen, deren Vor- bzw. Nachname mit dem Getippten beginnt, kommen zuerst.
            Func<string, string> schluessel = _parent.SortModus == 2
                ? WuenscheEditorVM.VornameVon
                : WuenscheEditorVM.NachnameVon;

            var treffer = _parent.AlleNamen
                .Where(n => n != _schueler.Name
                            && !schonDrin.Contains(n)
                            && n.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(n => schluessel(n).StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                .ThenBy(n => schluessel(n), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Take(6);

            foreach (var n in treffer) Vorschlaege.Add(n);

            VorschlaegeOffen = Vorschlaege.Count > 0;
            HighlightIndex   = Vorschlaege.Count > 0 ? 0 : -1;
        }

        // ── Tastatur-Navigation (aus dem Code-Behind aufgerufen) ─────────────
        public void MarkierungRunter()
        {
            if (Vorschlaege.Count == 0) return;
            HighlightIndex = (HighlightIndex + 1) % Vorschlaege.Count;
        }

        public void MarkierungHoch()
        {
            if (Vorschlaege.Count == 0) return;
            HighlightIndex = (HighlightIndex - 1 + Vorschlaege.Count) % Vorschlaege.Count;
        }

        public void UebernehmenMarkiert()
        {
            if (Vorschlaege.Count == 0) return;
            var idx = HighlightIndex >= 0 && HighlightIndex < Vorschlaege.Count ? HighlightIndex : 0;
            Hinzufuegen(Vorschlaege[idx]);
        }

        public void SuchfeldLeeren()
        {
            SuchText = "";
            VorschlaegeOffen = false;
        }

        public void LetztenChipEntfernen()
        {
            if (Chips.Count > 0) Entfernen(Chips[^1]);
        }

        // ── Hinzufügen / Entfernen / Prio ────────────────────────────────────
        [RelayCommand]
        private void Hinzufuegen(string? name)
        {
            if (_schueler == null || string.IsNullOrWhiteSpace(name) || IstVoll) return;
            if (Chips.Any(c => c.Name == name)) return;

            var prio = _parent.StandardPrio;
            var w = new Wunsch { ZielName = name!, Prio = prio, IstAntiWunsch = IstAntiWunsch };
            ZielListe(_schueler).Add(w);
            Chips.Add(new WunschChipVM(w, this));

            if (_parent.Gegenseitig)
                _parent.SetzeGegenwunsch(_schueler.Name, name!, IstAntiWunsch, prio, hinzufuegen: true);

            SuchfeldLeeren();
            NotifyZustand();
            _parent.MeldeGeaendert();
        }

        public void Entfernen(WunschChipVM chip)
        {
            if (_schueler == null) return;

            ZielListe(_schueler).RemoveAll(w => w.ZielName == chip.Name);
            Chips.Remove(chip);

            if (_parent.Gegenseitig)
                _parent.SetzeGegenwunsch(_schueler.Name, chip.Name, IstAntiWunsch, chip.Prio, hinzufuegen: false);

            NotifyZustand();
            _parent.MeldeGeaendert();
        }

        public void PrioGeaendert(WunschChipVM chip)
        {
            if (_schueler == null) return;
            if (_parent.Gegenseitig)
                _parent.SetzeGegenwunschPrio(_schueler.Name, chip.Name, IstAntiWunsch, chip.Prio);
            _parent.MeldeGeaendert();
        }

        private void NotifyZustand()
        {
            OnPropertyChanged(nameof(IstVoll));
            OnPropertyChanged(nameof(KannTippen));
            OnPropertyChanged(nameof(PlatzhalterText));
        }
    }

    // ── Klammer über beide Kategorien: Datenquelle + Reziprozität ────────────
    public partial class WuenscheEditorVM : ObservableObject
    {
        public WunschListeVM Zusammen   { get; }
        public WunschListeVM NichtNeben { get; }

        [ObservableProperty] private bool       _gegenseitig  = true;
        [ObservableProperty] private Prioritaet _standardPrio = Prioritaet.Prio1;

        public IReadOnlyList<string> AlleNamen => _alleNamen;

        // Sortiermodus der Namensliste (0/1 = nach Nachname, 2 = nach Vorname).
        // Bestimmt, wonach die Tipp-Vorschläge „springen". Vom MainViewModel gesetzt.
        public int SortModus { get; set; }

        // Namen liegen als „Nachname, Vorname" vor; ohne Komma gilt der ganze Name.
        public static string NachnameVon(string name)
        {
            int k = name.IndexOf(',');
            return (k >= 0 ? name[..k] : name).Trim();
        }

        public static string VornameVon(string name)
        {
            int k = name.IndexOf(',');
            return (k >= 0 ? name[(k + 1)..] : name).Trim();
        }

        private List<string>            _alleNamen     = new();
        private Func<string, Schueler?> _findeSchueler = _ => null;
        private Schueler?               _aktuell;

        // Callback nach oben (z. B. Lösung als veraltet markieren)
        public Action? OnGeaendert { get; set; }

        public WuenscheEditorVM()
        {
            Zusammen   = new WunschListeVM(this, istAnti: false, "Zusammen mit");
            NichtNeben = new WunschListeVM(this, istAnti: true,  "Nicht neben");
        }

        public void SetzeDatenquelle(IEnumerable<Schueler> alle)
        {
            var liste = alle.ToList();
            _alleNamen     = liste.Select(s => s.Name).ToList();
            _findeSchueler = name => liste.FirstOrDefault(s => s.Name == name);
        }

        public void LadeSchueler(Schueler? s)
        {
            _aktuell = s;
            Zusammen.LadeFuer(s);
            NichtNeben.LadeFuer(s);
        }

        public void MeldeGeaendert() => OnGeaendert?.Invoke();

        private static List<Wunsch> Liste(Schueler s, bool anti) => anti ? s.NichtNeben : s.ZusammenMit;

        /// <summary>Rückwunsch beim Partner setzen/entfernen. Ist dessen Liste voll,
        /// wird stillschweigend übersprungen (max. 2 gilt auch dort).</summary>
        public void SetzeGegenwunsch(string vonName, string zuName, bool anti, Prioritaet prio, bool hinzufuegen)
        {
            var ziel = _findeSchueler(zuName);
            if (ziel == null) return;

            var liste     = Liste(ziel, anti);
            var vorhanden = liste.FirstOrDefault(w => w.ZielName == vonName);

            if (hinzufuegen)
            {
                if (vorhanden != null)
                    vorhanden.Prio = prio;
                else if (liste.Count < WunschListeVM.MaxProKategorie)
                    liste.Add(new Wunsch { ZielName = vonName, Prio = prio, IstAntiWunsch = anti });
            }
            else
            {
                liste.RemoveAll(w => w.ZielName == vonName);
            }

            // Falls der Partner gerade angezeigt wird: seine Chips neu laden
            if (_aktuell != null && _aktuell.Name == zuName)
                LadeSchueler(_aktuell);
        }

        public void SetzeGegenwunschPrio(string vonName, string zuName, bool anti, Prioritaet prio)
        {
            var ziel = _findeSchueler(zuName);
            var w    = ziel != null ? Liste(ziel, anti).FirstOrDefault(x => x.ZielName == vonName) : null;
            if (w != null) w.Prio = prio;

            if (_aktuell != null && _aktuell.Name == zuName)
                LadeSchueler(_aktuell);
        }
    }
}

// ── Converter: markiert den per Tastatur gewählten Vorschlag ─────────────────
namespace SitzplanApp
{
    /// <summary>MultiBinding: (AlternationIndex, HighlightIndex) → Hintergrund-Brush.</summary>
    public class IndexHighlightConverter : IMultiValueConverter
    {
        private static readonly SolidColorBrush Aktiv    = new(Color.FromRgb(0xE6, 0xF1, 0xFB));
        private static readonly SolidColorBrush Inaktiv  = System.Windows.Media.Brushes.Transparent;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int idx && values[1] is int hi && idx == hi)
                return Aktiv;
            return Inaktiv;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
