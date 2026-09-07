using CommunityToolkit.Mvvm.ComponentModel;

// ─────────────────────────────────────────────────────────────────────────────
//  Wunsch-Matrix: ganze Klasse als Raster. Linksklick zykliert eine Zelle
//  Leer → Zusammen → Nicht-neben → Leer, Rechtsklick löscht. Immer symmetrisch
//  (A×B setzt auch B×A) und mit derselben max-2-Grenze wie das Chip-Panel.
// ─────────────────────────────────────────────────────────────────────────────

namespace SitzplanApp.ViewModels
{
    public enum MatrixZustand { Leer, Zusammen, NichtNeben }

    public partial class MatrixZelleVM : ObservableObject
    {
        public int  Zeile  { get; }
        public int  Spalte { get; }
        public bool IstDiagonale => Zeile == Spalte;

        public string RowName { get; }
        public string ColName { get; }

        [ObservableProperty] private MatrixZustand _zustand;

        public MatrixZelleVM(int zeile, int spalte, string rowName, string colName)
        {
            Zeile = zeile; Spalte = spalte;
            RowName = rowName; ColName = colName;
        }

        public string Farbe => IstDiagonale ? "#E5E7EB"
            : Zustand switch
            {
                MatrixZustand.Zusammen   => "#27AE60",
                MatrixZustand.NichtNeben => "#C0392B",
                _                        => "#FFFFFF",
            };

        public string Glyph => Zustand switch
        {
            MatrixZustand.Zusammen   => "+",
            MatrixZustand.NichtNeben => "–",
            _                        => "",
        };

        public string Tooltip => IstDiagonale ? ""
            : Zustand switch
            {
                MatrixZustand.Zusammen   => $"{RowName} ↔ {ColName}: zusammen",
                MatrixZustand.NichtNeben => $"{RowName} ↔ {ColName}: nicht neben",
                _                        => $"{RowName} ↔ {ColName}",
            };

        partial void OnZustandChanged(MatrixZustand value)
        {
            OnPropertyChanged(nameof(Farbe));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(Tooltip));
        }
    }

    public class MatrixZeileVM
    {
        public string Kopf { get; }
        public ObservableCollection<MatrixZelleVM> Zellen { get; } = new();
        public MatrixZeileVM(string kopf) => Kopf = kopf;
    }

    public partial class WunschMatrixVM : ObservableObject
    {
        public ObservableCollection<MatrixZeileVM> Zeilen        { get; } = new();
        public ObservableCollection<string>        SpaltenKoepfe { get; } = new();

        [ObservableProperty] private string _meldung = "";
        [ObservableProperty] private bool   _hatDaten;

        public Action? OnGeaendert { get; set; }

        private List<Schueler>    _schueler = new();
        private MatrixZelleVM[,]? _zellen;

        // ── Aufbau ───────────────────────────────────────────────────────────
        public void Initialisiere(List<Schueler> schueler)
        {
            _schueler = schueler;
            Zeilen.Clear();
            SpaltenKoepfe.Clear();
            Meldung  = "";
            HatDaten = schueler.Count > 0;

            int n = schueler.Count;
            _zellen = new MatrixZelleVM[n, n];

            foreach (var s in schueler)
                SpaltenKoepfe.Add(s.Nr.ToString());

            for (int i = 0; i < n; i++)
            {
                var zeile = new MatrixZeileVM($"{schueler[i].Nr}  {schueler[i].Name}");
                for (int j = 0; j < n; j++)
                {
                    var zelle = new MatrixZelleVM(i, j, schueler[i].Name, schueler[j].Name);
                    _zellen[i, j] = zelle;
                    zeile.Zellen.Add(zelle);
                }
                Zeilen.Add(zeile);
            }

            AusDatenAktualisieren();
        }

        /// <summary>Zellenfarben aus den (ggf. anderswo geänderten) Schüler-Listen ableiten.</summary>
        public void AusDatenAktualisieren()
        {
            if (_zellen == null) return;
            int n = _schueler.Count;
            for (int i = 0; i < n; i++)
            {
                var s = _schueler[i];
                for (int j = 0; j < n; j++)
                {
                    if (i == j) { _zellen[i, j].Zustand = MatrixZustand.Leer; continue; }
                    var name = _schueler[j].Name;
                    _zellen[i, j].Zustand =
                        s.ZusammenMit.Any(w => w.ZielName == name) ? MatrixZustand.Zusammen :
                        s.NichtNeben.Any(w => w.ZielName == name)  ? MatrixZustand.NichtNeben :
                                                                     MatrixZustand.Leer;
                }
            }
        }

        // ── Interaktion ──────────────────────────────────────────────────────
        public void ZelleGeklickt(MatrixZelleVM zelle, bool links)
        {
            if (_zellen == null || zelle.IstDiagonale) return;

            int i = zelle.Zeile, j = zelle.Spalte;
            var a = _schueler[i];
            var b = _schueler[j];
            Meldung = "";

            if (!links)                       // Rechtsklick → immer löschen
            {
                SetzePaar(a, b, i, j, MatrixZustand.Leer);
                OnGeaendert?.Invoke();
                return;
            }

            var ziel = zelle.Zustand switch    // Linksklick → vorwärts zyklen
            {
                MatrixZustand.Leer     => MatrixZustand.Zusammen,
                MatrixZustand.Zusammen => MatrixZustand.NichtNeben,
                _                      => MatrixZustand.Leer,
            };

            if (ziel == MatrixZustand.Zusammen && !KannHinzufuegen(a, b, anti: false))
            { Meldung = KapazitaetMeldung(a, b, anti: false); return; }

            if (ziel == MatrixZustand.NichtNeben && !KannHinzufuegen(a, b, anti: true))
            { Meldung = KapazitaetMeldung(a, b, anti: true); return; }

            SetzePaar(a, b, i, j, ziel);
            OnGeaendert?.Invoke();
        }

        public void LoescheAlles()
        {
            foreach (var s in _schueler)
            {
                s.ZusammenMit.Clear();
                s.NichtNeben.Clear();
            }
            AusDatenAktualisieren();
            Meldung = "Alle Wünsche gelöscht.";
            OnGeaendert?.Invoke();
        }

        // ── Helfer ───────────────────────────────────────────────────────────
        private static int Anzahl(Schueler s, bool anti, string ausser)
        {
            var liste = anti ? s.NichtNeben : s.ZusammenMit;
            return liste.Count(w => w.ZielName != ausser);
        }

        private static bool KannHinzufuegen(Schueler a, Schueler b, bool anti)
            => Anzahl(a, anti, b.Name) < WunschListeVM.MaxProKategorie
            && Anzahl(b, anti, a.Name) < WunschListeVM.MaxProKategorie;

        private static string KapazitaetMeldung(Schueler a, Schueler b, bool anti)
        {
            string kat  = anti ? "Nicht-neben" : "Zusammen";
            var    voll = new List<string>();
            if (Anzahl(a, anti, b.Name) >= WunschListeVM.MaxProKategorie) voll.Add(a.Name);
            if (Anzahl(b, anti, a.Name) >= WunschListeVM.MaxProKategorie) voll.Add(b.Name);
            return $"{string.Join(" und ", voll)} " +
                   (voll.Count > 1 ? "haben" : "hat") + $" schon 2 {kat}-Wünsche.";
        }

        private static void EntfernePaar(Schueler a, Schueler b)
        {
            a.ZusammenMit.RemoveAll(w => w.ZielName == b.Name);
            a.NichtNeben .RemoveAll(w => w.ZielName == b.Name);
            b.ZusammenMit.RemoveAll(w => w.ZielName == a.Name);
            b.NichtNeben .RemoveAll(w => w.ZielName == a.Name);
        }

        private void SetzePaar(Schueler a, Schueler b, int i, int j, MatrixZustand ziel)
        {
            EntfernePaar(a, b);

            if (ziel == MatrixZustand.Zusammen)
            {
                a.ZusammenMit.Add(new Wunsch { ZielName = b.Name, Prio = Prioritaet.Prio1, IstAntiWunsch = false });
                b.ZusammenMit.Add(new Wunsch { ZielName = a.Name, Prio = Prioritaet.Prio1, IstAntiWunsch = false });
            }
            else if (ziel == MatrixZustand.NichtNeben)
            {
                a.NichtNeben.Add(new Wunsch { ZielName = b.Name, Prio = Prioritaet.Prio1, IstAntiWunsch = true });
                b.NichtNeben.Add(new Wunsch { ZielName = a.Name, Prio = Prioritaet.Prio1, IstAntiWunsch = true });
            }

            _zellen![i, j].Zustand = ziel;
            _zellen![j, i].Zustand = ziel;   // Spiegelzelle
        }
    }
}
