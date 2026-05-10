using Google.OrTools.Sat;

namespace SitzplanApp.Services;

/// <summary>
/// ILP-basierter Optimierungsservice mit Google OR-Tools (CP-SAT Solver).
///
/// Architektur:
///   Phase 0: Fixierungen → direkt gesetzt, nicht im ILP
///   Phase 1: ILP löst die Zuweisung der restlichen Schüler
///            - Hard Constraints: Prio-3, Sehschwäche-Reihe, Fixierungen
///            - Soft Constraints: Prio-2/1 als gewichtete Objective-Terms
///   Phase 2: Bewertung & Diagnose
///
/// 3 Lösungen:
///   Lösung 1: Solver mit vollem Timeout (beste Lösung)
///   Lösung 2: Solver mit Zufalls-Seed 2 (andere Exploration)
///   Lösung 3: Solver mit Zufalls-Seed 3 (weitere Alternative)
///
/// Parameter werden aus ParameterSet gelesen (Excel-Sheet "Parameter").
/// </summary>
public class OptimierungsService
{
    private const double NEBEN_SCHWELLE = 1.5;

    // ── Öffentliche Methode ──────────────────────────────────────────────────
    public OptimierungsErgebnis Optimiere(
        List<Schueler> alleSchueler,
        List<Tischgruppe> alleGruppen,
        ParameterSet param)
    {
        var ergebnis    = new OptimierungsErgebnis();
        var plaetzeListe = BauePlatzPool(alleGruppen);

        // ── Lösung 1: Volloptimierung ────────────────────────────────────────
        var l1 = LoeseMitILP(
            index: 1, bezeichnung: "Volloptimierung",
            alleSchueler, alleGruppen, plaetzeListe,
            param, seed: 1, timeoutSek: 30,
            geschlechterFaktor: 1.0,
            verboteneZuweisung: null);
        ergebnis.Loesungen.Add(l1);

        // ── Lösung 2: Geschlechterverteilung stärker gewichten (×5) ─────────
        var param2 = new ParameterSet
        {
            W_PRIO2_ERF    = param.W_PRIO2_ERF,
            W_PRIO2_STRAF  = param.W_PRIO2_STRAF,
            W_PRIO1_ERF    = param.W_PRIO1_ERF,
            W_PRIO1_STRAF  = param.W_PRIO1_STRAF,
            W_SEH_VORNE    = param.W_SEH_VORNE,
            W_GESCHLECHT   = param.W_GESCHLECHT * 5,
            W_LINKSHAENDER = param.W_LINKSHAENDER,
        };
        var l2 = LoeseMitILP(
            index: 2, bezeichnung: "Geschlechterverteilung priorisiert",
            alleSchueler, alleGruppen, plaetzeListe,
            param2, seed: 2, timeoutSek: 30,
            geschlechterFaktor: 5.0,
            verboteneZuweisung: null);
        ergebnis.Loesungen.Add(l2);

        // ── Lösung 3: No-good cut auf Lösung 1 → strukturell andere Lösung ──
        var verboten = l1.Sitzplaetze
            .Where(p => !p.IstFrei)
            .Select(p => new VerboteneZuweisung(p.Schueler!.Name, p.Gruppe.Name, p.PlatzNr))
            .ToList();

        var l3 = LoeseMitILP(
            index: 3, bezeichnung: "Alternative Lösung (No-good cut)",
            alleSchueler, alleGruppen, plaetzeListe,
            param, seed: 3, timeoutSek: 30,
            geschlechterFaktor: 1.0,
            verboteneZuweisung: verboten);
        ergebnis.Loesungen.Add(l3);

        return ergebnis;
    }

    // ── ILP-Kern ─────────────────────────────────────────────────────────────
    private Loesung LoeseMitILP(
        int index, string bezeichnung,
        List<Schueler> alleSchueler,
        List<Tischgruppe> alleGruppen,
        List<PlatzVorlage> plaetzeVorlage,
        ParameterSet param,
        int seed, int timeoutSek,
        double geschlechterFaktor,
        List<VerboteneZuweisung>? verboteneZuweisung)
    {
        var loesung = new Loesung { Index = index, Bezeichnung = bezeichnung };

        // Frische Sitzplätze für diese Lösung
        var plaetze = plaetzeVorlage
            .Select(t => new Sitzplatz { Gruppe = t.Gruppe, PlatzNr = t.PlatzNr })
            .ToList();

        int S = alleSchueler.Count;  // Anzahl Schüler
        int P = plaetze.Count;       // Anzahl Plätze

        if (P < S)
        {
            loesung.Warnungen.Add($"Nur {P} Plätze für {S} Schüler!");
            // Trotzdem versuchen
        }

        // ── Phase 0: Fixierungen vorbelegen ──────────────────────────────────
        var fixiert = new Dictionary<int, int>(); // schülerIdx → platzIdx
        var fixierteSchueler = new HashSet<int>();
        var fixiertePlaetze  = new HashSet<int>();

        for (int si = 0; si < S; si++)
        {
            var s = alleSchueler[si];
            if (s.FixGruppenNamen.Count == 0) continue;

            var zielGruppen = s.FixGruppenNamen
                .Select(n => alleGruppen.FirstOrDefault(g =>
                    g.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                .Where(g => g != null)
                .Cast<Tischgruppe>()
                .ToList();

            if (zielGruppen.Count == 0)
            {
                loesung.Warnungen.Add($"{s.Name}: Keine Fixierungsgruppe gefunden.");
                continue;
            }

            var zielNamen = zielGruppen.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Kandidatenplätze: alle Plätze in Zielgruppen die noch frei sind
            int pi = -1;

            if (zielGruppen.Count == 1 && s.FixSitzplatzNr.HasValue)
            {
                // Spezifischer Platz
                for (int k = 0; k < P; k++)
                {
                    if (fixiertePlaetze.Contains(k)) continue;
                    if (zielNamen.Contains(plaetze[k].Gruppe.Name) &&
                        plaetze[k].PlatzNr == s.FixSitzplatzNr)
                    { pi = k; break; }
                }
            }

            if (pi < 0)
            {
                // Ersten freien Platz in einer der Zielgruppen (Oder-Logik)
                for (int k = 0; k < P; k++)
                {
                    if (fixiertePlaetze.Contains(k)) continue;
                    if (zielNamen.Contains(plaetze[k].Gruppe.Name))
                    { pi = k; break; }
                }
            }

            if (pi >= 0)
            {
                // Verbots-Check für fixierten Platz
                if (s.Verbote.Any(v => v.TrifftZu(plaetze[pi].Gruppe.Name, plaetze[pi].PlatzNr)))
                    loesung.Warnungen.Add($"{s.Name}: Fixierter Platz verletzt ein Verbot ({plaetze[pi].Gruppe.Name}/Platz {plaetze[pi].PlatzNr}).");

                fixiert[si] = pi;
                fixierteSchueler.Add(si);
                fixiertePlaetze.Add(pi);
                plaetze[pi].Schueler = s;
                loesung.Protokoll.Add(
                    $"{s.Nr}|{s.Name}|{plaetze[pi].Gruppe.Name}|{plaetze[pi].PlatzNr}|Fixiert||Fixiert");
            }
            else
            {
                var gruppenListe = string.Join(" / ", zielGruppen.Select(g => g.Name));
                loesung.Warnungen.Add($"{s.Name}: Kein freier Platz in '{gruppenListe}'.");
            }
        }

        // Nicht-fixierte Schüler und freie Plätze
        var nfSchueler = Enumerable.Range(0, S)
            .Where(i => !fixierteSchueler.Contains(i)).ToList();
        var freiePlaetze = Enumerable.Range(0, P)
            .Where(i => !fixiertePlaetze.Contains(i)).ToList();

        int NS = nfSchueler.Count;
        int NP = freiePlaetze.Count;

        if (NS == 0) goto Bewertung;

        // ── ILP-Modell ────────────────────────────────────────────────────────
        {
            var model  = new CpModel();
            var solver = new CpSolver();

            // Einstellungen
            solver.StringParameters =
                $"max_time_in_seconds:{timeoutSek}," +
                $"random_seed:{seed}," +
                "num_search_workers:4," +
                "log_search_progress:false";

            // Entscheidungsvariablen: x[si, pi] = 1 wenn Schüler nfSchueler[si] auf Platz freiePlaetze[pi]
            var x = new BoolVar[NS, NP];
            for (int si = 0; si < NS; si++)
                for (int pi = 0; pi < NP; pi++)
                    x[si, pi] = model.NewBoolVar($"x_{si}_{pi}");

            // ── Constraint 1: Jeder Schüler genau einen Platz ────────────────
            for (int si = 0; si < NS; si++)
            {
                var row = new BoolVar[NP];
                for (int pi = 0; pi < NP; pi++) row[pi] = x[si, pi];
                model.Add(LinearExpr.Sum(row) == 1);
            }

            // ── Constraint 2: Jeder Platz max. einen Schüler ─────────────────
            for (int pi = 0; pi < NP; pi++)
            {
                var col = new BoolVar[NS];
                for (int si = 0; si < NS; si++) col[si] = x[si, pi];
                model.Add(LinearExpr.Sum(col) <= 1);
            }

            // ── Constraint 3: Prio-3 Hard Constraints ────────────────────────
            for (int si = 0; si < NS; si++)
            {
                var s = alleSchueler[nfSchueler[si]];

                // Prio-3 Nicht-neben: si und zi dürfen nicht auf benachbarten Plätzen sitzen
                foreach (var wunsch in s.NichtNeben.Where(w => w.Prio == Prioritaet.Prio3))
                {
                    int zielGlobalIdx = alleSchueler.FindIndex(z =>
                        OptimierungsService.NamenMatch(z.Name, wunsch.ZielName));
                    if (zielGlobalIdx < 0) continue;

                    if (fixierteSchueler.Contains(zielGlobalIdx))
                    {
                        // Ziel fixiert → verbiete si auf allen Plätzen neben fixiertem Platz
                        int fixPlatzIdx = fixiert[zielGlobalIdx];
                        var fixPlatz = plaetze[fixPlatzIdx];
                        for (int pi = 0; pi < NP; pi++)
                        {
                            var p = plaetze[freiePlaetze[pi]];
                            if (p.AbstandZu(fixPlatz) <= NEBEN_SCHWELLE)
                                model.Add(x[si, pi] == 0);
                        }
                    }
                    else
                    {
                        int zi = nfSchueler.IndexOf(zielGlobalIdx);
                        if (zi < 0 || zi == si) continue;

                        // Für jedes benachbarte Platzpaar: nicht beide gleichzeitig
                        // si > zi nicht überspringen — Constraint ist symmetrisch, Duplikate sind harmlos
                        for (int pi = 0; pi < NP; pi++)
                            for (int pj = 0; pj < NP; pj++)
                            {
                                if (pi == pj) continue;
                                var pa = plaetze[freiePlaetze[pi]];
                                var pb = plaetze[freiePlaetze[pj]];
                                if (pa.AbstandZu(pb) <= NEBEN_SCHWELLE)
                                    model.Add(x[si, pi] + x[zi, pj] <= 1);
                            }
                    }
                }

                // Prio-3 Zusammen-mit: korrekte Implikation
                // Wenn si auf Platz pi → zi muss auf einem zu pi benachbarten Platz sitzen
                foreach (var wunsch in s.ZusammenMit.Where(w => w.Prio == Prioritaet.Prio3))
                {
                    int zielGlobalIdx = alleSchueler.FindIndex(z =>
                        OptimierungsService.NamenMatch(z.Name, wunsch.ZielName));
                    if (zielGlobalIdx < 0) continue;

                    if (fixierteSchueler.Contains(zielGlobalIdx))
                    {
                        // Ziel fixiert → si muss auf einem zu fixiertem Platz benachbarten Platz
                        int fixPlatzIdx = fixiert[zielGlobalIdx];
                        var fixPlatz = plaetze[fixPlatzIdx];
                        var erlaubte = new List<BoolVar>();
                        for (int pi = 0; pi < NP; pi++)
                        {
                            var p = plaetze[freiePlaetze[pi]];
                            if (p.SitzenZusammen(fixPlatz))
                                erlaubte.Add(x[si, pi]);
                        }
                        model.Add(LinearExpr.Sum(erlaubte) >= 1);
                    }
                    else
                    {
                        int zi = nfSchueler.IndexOf(zielGlobalIdx);
                        if (zi < 0 || zi == si) continue;

                        // Constraint in beide Richtungen setzen (nicht nur si < zi)
                        // si auf pi → zi muss auf benachbartem pj sitzen
                        for (int pi = 0; pi < NP; pi++)
                        {
                            var pa = plaetze[freiePlaetze[pi]];
                            var nachbarn = new List<BoolVar>();
                            for (int pj = 0; pj < NP; pj++)
                            {
                                if (pi == pj) continue;
                                var pb = plaetze[freiePlaetze[pj]];
                                if (pa.SitzenZusammen(pb))
                                    nachbarn.Add(x[zi, pj]);
                            }
                            if (nachbarn.Count > 0)
                                model.Add(LinearExpr.Sum(nachbarn) >= x[si, pi]);
                            else
                                model.Add(x[si, pi] == 0);
                        }

                        // Nur wenn si < zi: auch Gegenrichtung (zi auf pj → si muss benachbart)
                        // Immer setzen (nicht nur wenn si < zi) damit B nicht ohne Nachbar platziert wird
                        for (int pj = 0; pj < NP; pj++)
                        {
                            var pb = plaetze[freiePlaetze[pj]];
                            var nachbarn = new List<BoolVar>();
                            for (int pi = 0; pi < NP; pi++)
                            {
                                if (pi == pj) continue;
                                var pa = plaetze[freiePlaetze[pi]];
                                if (pb.SitzenZusammen(pa))
                                    nachbarn.Add(x[si, pi]);
                            }
                            if (nachbarn.Count > 0)
                                model.Add(LinearExpr.Sum(nachbarn) >= x[zi, pj]);
                            else
                                model.Add(x[zi, pj] == 0);
                        }
                    }
                }

                // Constraint 4: Sehschwäche → Reihe 1 oder 2 (Hard)
                if (s.Sehschwaeche)
                {
                    var vornePlaetze = new List<BoolVar>();
                    for (int pi = 0; pi < NP; pi++)
                    {
                        var p = plaetze[freiePlaetze[pi]];
                        if (p.Gruppe.Reihe <= 2) vornePlaetze.Add(x[si, pi]);
                    }
                    int anzahlVorne = freiePlaetze.Count(pi =>
                        plaetze[pi].Gruppe.Reihe <= 2);
                    if (anzahlVorne > 0 && vornePlaetze.Count > 0)
                        model.Add(LinearExpr.Sum(vornePlaetze) >= 1);
                }

                // Constraint 5: Platzverbote (Hard)
                if (s.Verbote.Count > 0)
                {
                    for (int pi = 0; pi < NP; pi++)
                    {
                        var p = plaetze[freiePlaetze[pi]];
                        if (s.Verbote.Any(v => v.TrifftZu(p.Gruppe.Name, p.PlatzNr)))
                            model.Add(x[si, pi] == 0);
                    }
                }
            }

            // ── Constraint 5: No-good cut (Lösung 3) ─────────────────────────
            // Verhindert dass exakt dieselbe Zuweisung wie Lösung 1 entsteht.
            // Mindestens eine Zuweisung muss abweichen.
            if (verboteneZuweisung != null && verboteneZuweisung.Count > 0)
            {
                var gleicheZuweisungen = new List<BoolVar>();
                int anzahlVerboteneTreffer = 0;

                for (int si = 0; si < NS; si++)
                {
                    var sName = alleSchueler[nfSchueler[si]].Name;
                    for (int pi = 0; pi < NP; pi++)
                    {
                        var platz = plaetze[freiePlaetze[pi]];
                        bool istVerboten = verboteneZuweisung.Any(v =>
                            v.SchuelerName == sName &&
                            v.GruppeName   == platz.Gruppe.Name &&
                            v.PlatzNr      == platz.PlatzNr);
                        if (istVerboten)
                        {
                            gleicheZuweisungen.Add(x[si, pi]);
                            anzahlVerboteneTreffer++;
                        }
                    }
                }

                if (anzahlVerboteneTreffer > 0)
                    model.Add(LinearExpr.Sum(gleicheZuweisungen) <= anzahlVerboteneTreffer - 1);
            }
            var objVars   = new List<BoolVar>();
            var objCoeffs = new List<long>();

            for (int si = 0; si < NS; si++)
            {
                var s = alleSchueler[nfSchueler[si]];

                for (int pi = 0; pi < NP; pi++)
                {
                    var platz = plaetze[freiePlaetze[pi]];
                    long score = 0;

                    if (s.Sehschwaeche)
                        score += (long)(param.W_SEH_VORNE * (10.0 / (platz.Gruppe.Reihe + 1)));

                    if (s.Linkshaender && platz.PlatzNr == 1)
                        score += (long)param.W_LINKSHAENDER;

                    foreach (var wunsch in s.AlleWuensche.Where(w => w.Prio != Prioritaet.Prio3))
                    {
                        int zielGlobalIdx = alleSchueler.FindIndex(z =>
                            OptimierungsService.NamenMatch(z.Name, wunsch.ZielName));
                        if (zielGlobalIdx < 0) continue;

                        bool zielFixiert = fixierteSchueler.Contains(zielGlobalIdx);
                        int? zielFixPlatzIdx = zielFixiert ? fixiert.GetValueOrDefault(zielGlobalIdx, -1) : null;

                        if (wunsch.IstAntiWunsch)
                        {
                            if (zielFixiert && zielFixPlatzIdx.HasValue && zielFixPlatzIdx.Value >= 0)
                            {
                                var fixPlatz = plaetze[zielFixPlatzIdx.Value];
                                double dist = platz.AbstandZu(fixPlatz);
                                if (dist <= NEBEN_SCHWELLE)
                                    score -= wunsch.Prio == Prioritaet.Prio2
                                        ? (long)param.W_PRIO2_STRAF : (long)param.W_PRIO1_STRAF;
                                else
                                    score += wunsch.Prio == Prioritaet.Prio2
                                        ? (long)param.W_PRIO2_ERF : (long)param.W_PRIO1_ERF;
                            }
                        }
                        else
                        {
                            if (zielFixiert && zielFixPlatzIdx.HasValue && zielFixPlatzIdx.Value >= 0)
                            {
                                var fixPlatz = plaetze[zielFixPlatzIdx.Value];
                                if (platz.SitzenZusammen(fixPlatz))
                                    score += wunsch.Prio == Prioritaet.Prio2
                                        ? (long)param.W_PRIO2_ERF : (long)param.W_PRIO1_ERF;
                            }
                        }
                    }

                    foreach (int fixPlatzIdx in fixiertePlaetze)
                    {
                        var fixPlatz = plaetze[fixPlatzIdx];
                        if (fixPlatz.Schueler?.Geschlecht == s.Geschlecht &&
                            platz.AbstandZu(fixPlatz) <= NEBEN_SCHWELLE)
                            score -= (long)param.W_GESCHLECHT;
                    }

                    if (score != 0)
                    {
                        objVars.Add(x[si, pi]);
                        objCoeffs.Add(score);
                    }
                }
            }

            // Interaktionsterme für nicht-fixierte Paare
            for (int si = 0; si < NS; si++)
            {
                var s = alleSchueler[nfSchueler[si]];
                foreach (var wunsch in s.AlleWuensche.Where(w => w.Prio != Prioritaet.Prio3))
                {
                    int zielGlobalIdx = alleSchueler.FindIndex(z =>
                        OptimierungsService.NamenMatch(z.Name, wunsch.ZielName));
                    if (zielGlobalIdx < 0) continue;
                    if (fixierteSchueler.Contains(zielGlobalIdx)) continue;

                    int zi = nfSchueler.IndexOf(zielGlobalIdx);
                    if (zi <= si) continue;

                    long bonus  = wunsch.Prio == Prioritaet.Prio2 ? (long)param.W_PRIO2_ERF  : (long)param.W_PRIO1_ERF;
                    long strafe = wunsch.Prio == Prioritaet.Prio2 ? (long)param.W_PRIO2_STRAF : (long)param.W_PRIO1_STRAF;

                    for (int pi = 0; pi < NP; pi++)
                        for (int pj = 0; pj < NP; pj++)
                        {
                            if (pi == pj) continue;
                            var pa = plaetze[freiePlaetze[pi]];
                            var pb = plaetze[freiePlaetze[pj]];

                            var both = model.NewBoolVar($"b_{si}_{zi}_{pi}_{pj}");
                            model.AddBoolAnd(new[] { x[si, pi], x[zi, pj] }).OnlyEnforceIf(both);
                            model.AddBoolOr(new[] { x[si, pi].Not(), x[zi, pj].Not() }).OnlyEnforceIf(both.Not());

                            long coeff = 0;
                            if (wunsch.IstAntiWunsch)
                                coeff = pa.AbstandZu(pb) <= NEBEN_SCHWELLE ? -strafe : bonus;
                            else if (pa.SitzenZusammen(pb))
                                coeff = bonus;

                            if (coeff != 0) { objVars.Add(both); objCoeffs.Add(coeff); }
                        }

                    if (s.Geschlecht == alleSchueler[zielGlobalIdx].Geschlecht)
                        for (int pi = 0; pi < NP; pi++)
                            for (int pj = 0; pj < NP; pj++)
                            {
                                if (pi == pj) continue;
                                var pa = plaetze[freiePlaetze[pi]];
                                var pb = plaetze[freiePlaetze[pj]];
                                if (pa.AbstandZu(pb) <= NEBEN_SCHWELLE)
                                {
                                    var both = model.NewBoolVar($"g_{si}_{zi}_{pi}_{pj}");
                                    model.AddBoolAnd(new[] { x[si, pi], x[zi, pj] }).OnlyEnforceIf(both);
                                    model.AddBoolOr(new[] { x[si, pi].Not(), x[zi, pj].Not() }).OnlyEnforceIf(both.Not());
                                    objVars.Add(both);
                                    objCoeffs.Add(-(long)param.W_GESCHLECHT);
                                }
                            }
                }
            }

            model.Maximize(LinearExpr.WeightedSum(objVars, objCoeffs));

            // ── Lösen ─────────────────────────────────────────────────────────
            var status = solver.Solve(model);

            if (status == CpSolverStatus.Infeasible)
            {
                loesung.Warnungen.Add("ILP: Keine zulässige Lösung gefunden (Prio-3-Konflikte?).");
                // Fallback: einfache Greedy-Zuweisung
                FallbackGreedy(nfSchueler, freiePlaetze, plaetze, alleSchueler, loesung);
            }
            else
            {
                // Lösung auslesen
                for (int si = 0; si < NS; si++)
                    for (int pi = 0; pi < NP; pi++)
                        if (solver.BooleanValue(x[si, pi]))
                        {
                            var platz = plaetze[freiePlaetze[pi]];
                            platz.Schueler = alleSchueler[nfSchueler[si]];
                        }

                if (status == CpSolverStatus.Feasible)
                    loesung.Warnungen.Add($"ILP: Timeout nach {timeoutSek}s – beste gefundene Lösung.");
            }
        }

        Bewertung:
        loesung.Sitzplaetze = plaetze;
        BewerteLoesung(loesung, alleSchueler, param);
        ErstelleProtokoll(loesung, alleSchueler);
        return loesung;
    }

    // ── Fallback Greedy (wenn ILP infeasible) ────────────────────────────────
    private void FallbackGreedy(
        List<int> nfSchueler, List<int> freiePlaetze,
        List<Sitzplatz> plaetze, List<Schueler> alleSchueler,
        Loesung loesung)
    {
        loesung.Warnungen.Add("Fallback: Greedy-Zuweisung.");
        var frei = new Queue<int>(freiePlaetze);
        foreach (int si in nfSchueler)
        {
            if (frei.Count == 0) break;
            plaetze[frei.Dequeue()].Schueler = alleSchueler[si];
        }
    }

    // ── Sitzplatz-Pool aufbauen ───────────────────────────────────────────────
    private static List<PlatzVorlage> BauePlatzPool(List<Tischgruppe> gruppen)
    {
        var pool = new List<PlatzVorlage>();
        foreach (var g in gruppen.OrderBy(g => g.Reihe).ThenBy(g => g.Spalte))
            for (int p = 1; p <= g.Sitzplaetze; p++)
                pool.Add(new PlatzVorlage(g, p));
        return pool;
    }

    // ── Öffentliche Bewertung für manuelle Lösungen ───────────────────────────
    public void BewerteManuell(Loesung loesung, List<Schueler> alleSchueler, ParameterSet param)
    {
        loesung.Bewertungen.Clear();
        loesung.Warnungen.Clear();
        loesung.Prio3Gesamt           = 0;
        loesung.Prio3Erfuellt         = 0;
        loesung.Prio2Gesamt           = 0;
        loesung.Prio2Erfuellt         = 0;
        loesung.Prio1Gesamt           = 0;
        loesung.Prio1Erfuellt         = 0;
        loesung.SehschwaeacheVorne    = 0;
        loesung.SehschwaeacheGesamt   = 0;
        loesung.GeschlechterScore     = 0;
        loesung.GesamtScore           = 0;
        BewerteLoesung(loesung, alleSchueler, param);
    }

    // ── Bewertung ─────────────────────────────────────────────────────────────
    private void BewerteLoesung(Loesung loesung, List<Schueler> alleSchueler,
        ParameterSet param)
    {
        var plaetze = loesung.Sitzplaetze;

        foreach (var s in alleSchueler)
        {
            var platz = plaetze.FirstOrDefault(p => p.Schueler == s);
            if (platz == null) continue;

            foreach (var wunsch in s.AlleWuensche)
            {
                var zielPlatz = FindePlatz(plaetze, wunsch.ZielName);
                double dist = zielPlatz != null ? platz.AbstandZu(zielPlatz) : 999;

                bool erfuellt = zielPlatz == null
                    ? wunsch.IstAntiWunsch  // Ziel in Wartezone → Anti-Wunsch erfüllt, Zusammen-Wunsch nicht
                    : wunsch.IstAntiWunsch
                        ? dist > NEBEN_SCHWELLE
                        : platz.SitzenZusammen(zielPlatz);

                loesung.Bewertungen.Add(new WunschBewertung
                {
                    Schueler = s,
                    Wunsch   = wunsch,
                    Erfuellt = erfuellt,
                    Abstand  = dist >= 999 ? -1 : (int)Math.Round(dist),
                });

                switch (wunsch.Prio)
                {
                    case Prioritaet.Prio3:
                        loesung.Prio3Gesamt++;
                        if (erfuellt) loesung.Prio3Erfuellt++;
                        else loesung.Warnungen.Add(
                            $"Prio-3 VERLETZT: {s.Name} " +
                            $"{(wunsch.IstAntiWunsch ? "sitzt neben" : "sitzt nicht neben")} " +
                            $"{wunsch.ZielName}");
                        break;
                    case Prioritaet.Prio2:
                        loesung.Prio2Gesamt++;
                        if (erfuellt) loesung.Prio2Erfuellt++;
                        else loesung.GesamtScore -= param.W_PRIO2_STRAF;
                        break;
                    case Prioritaet.Prio1:
                        loesung.Prio1Gesamt++;
                        if (erfuellt) loesung.Prio1Erfuellt++;
                        else loesung.GesamtScore -= param.W_PRIO1_STRAF;
                        break;
                }
            }

            if (s.Sehschwaeche)
            {
                loesung.SehschwaeacheGesamt++;
                if (platz.Gruppe.Reihe <= 2) loesung.SehschwaeacheVorne++;
            }

            // Verbots-Verletzung prüfen
            if (s.Verbote.Any(v => v.TrifftZu(platz.Gruppe.Name, platz.PlatzNr)))
                loesung.Warnungen.Add($"Verbot verletzt: {s.Name} sitzt auf {platz.Gruppe.Name}/Platz {platz.PlatzNr}");
        }

        loesung.GeschlechterScore = BerechneGeschlechterScore(plaetze);

        loesung.GesamtScore +=
            loesung.Prio2Erfuellt * param.W_PRIO2_ERF +
            loesung.Prio1Erfuellt * param.W_PRIO1_ERF +
            loesung.SehschwaeacheVorne * param.W_SEH_VORNE +
            loesung.GeschlechterScore * 50;

        int p3verletzt = loesung.Prio3Gesamt - loesung.Prio3Erfuellt;
        loesung.GesamtScore -= p3verletzt * 2000;
    }

    private double BerechneGeschlechterScore(List<Sitzplatz> plaetze)
    {
        var gruppen = plaetze.Where(p => !p.IstFrei).GroupBy(p => p.Gruppe);
        if (!gruppen.Any()) return 0;
        var verhaeltnisse = gruppen.Select(g =>
        {
            int total = g.Count();
            int m = g.Count(p => p.Schueler!.Geschlecht == "m");
            return total > 0 ? (double)m / total : 0.5;
        }).ToList();
        double mean = verhaeltnisse.Average();
        double variance = verhaeltnisse.Average(v => Math.Pow(v - mean, 2));
        return Math.Max(0, 1 - Math.Sqrt(variance) * 4);
    }

    // ── Protokoll ────────────────────────────────────────────────────────────
    private void ErstelleProtokoll(Loesung loesung, List<Schueler> alleSchueler)
    {
        loesung.Protokoll.Clear();
        foreach (var p in loesung.Sitzplaetze
            .Where(p => !p.IstFrei)
            .OrderBy(p => p.Gruppe.Reihe)
            .ThenBy(p => p.Gruppe.Spalte)
            .ThenBy(p => p.PlatzNr))
        {
            var s = p.Schueler!;
            var wErfuellt = loesung.Bewertungen
                .Where(b => b.Schueler == s && b.Erfuellt)
                .Select(b => b.Wunsch.ToString()).ToList();
            var wVerletzt = loesung.Bewertungen
                .Where(b => b.Schueler == s && !b.Erfuellt)
                .Select(b => $"!{b.Wunsch}").ToList();

            string grund = s.FixGruppenname != null ? "Fixiert"
                : s.Sehschwaeche ? "Sehschwäche"
                : wErfuellt.Any() ? string.Join(", ", wErfuellt)
                : "Standard";

            loesung.Protokoll.Add(
                $"{s.Nr}|{s.Name}|{p.Gruppe.Name}|{p.PlatzNr}|{grund}" +
                $"|{string.Join(", ", wVerletzt)}" +
                $"|{(s.FixGruppenname != null ? "Fixiert" : "")}");
        }
    }

    // ── Hilfsmethoden ────────────────────────────────────────────────────────
    public static bool NamenMatch(string vollName, string suchName) =>
        !string.IsNullOrWhiteSpace(suchName) &&
        (vollName.Contains(suchName, StringComparison.OrdinalIgnoreCase) ||
         suchName.Contains(vollName, StringComparison.OrdinalIgnoreCase));

    private static Sitzplatz? FindePlatz(List<Sitzplatz> plaetze, string name) =>
        plaetze.FirstOrDefault(p => !p.IstFrei && NamenMatch(p.Schueler!.Name, name));
}

public class VerboteneZuweisung
{
    public string SchuelerName { get; }
    public string GruppeName   { get; }
    public int    PlatzNr      { get; }

    public VerboteneZuweisung(string schuelerName, string gruppeName, int platzNr)
    {
        SchuelerName = schuelerName;
        GruppeName   = gruppeName;
        PlatzNr      = platzNr;
    }
}

public class PlatzVorlage
{
    public Tischgruppe Gruppe  { get; }
    public int         PlatzNr { get; }

    public PlatzVorlage(Tischgruppe gruppe, int platzNr)
    {
        Gruppe  = gruppe;
        PlatzNr = platzNr;
    }
}
