namespace EQBuddy.Core;

/// <summary>The tracked-rule (Watch) scan — the memo record, the incremental
/// accumulators (perf audit #10), the from-scratch oracle test seam, and the
/// per-event fold. First file split out of SessionStats (2026-08-14, the
/// architecture ratchet's first catch): same class, same lock discipline —
/// every member here is called under <c>_lock</c> by the snapshot path in
/// SessionStats.cs.</summary>
public sealed partial class SessionStats
{
    /// <summary>One rule's journal-scan result, minus the time-derived rates —
    /// the memoizable half of a TrackedRuleResult (perf audit #4).</summary>
    private sealed record TrackedScan(string Name, string Id, int Total,
        List<NameCount> Items, DateTime? First, DateTime? Last, string? LastItem);
    private (long Version, string Fingerprint, List<TrackedScan> Scans)? _trackedMemo;

    /// <summary>Perf audit #10: one rule's running journal-scan totals, folded forward
    /// as entries append instead of rescanned O(rules × journal) every tick. Valid only
    /// together with <see cref="_trackedScanIndex"/> and the invalidation guards below.</summary>
    private sealed class TrackedAcc
    {
        public readonly Dictionary<string, int> Items = new(StringComparer.OrdinalIgnoreCase);
        public int Total;
        public DateTime? First, Last;
        public string? LastItem;
    }
    /// <summary>Accumulators parallel to the enabled-rule subsequence the fingerprint
    /// describes; null = must rebuild from the whole journal.</summary>
    private List<TrackedAcc>? _trackedAccs;
    private string? _trackedAccFingerprint;
    /// <summary>First _journal index the accumulators have NOT folded in. The journal
    /// prune adjusts it by however many entries it removed below it, so the index keeps
    /// pointing at the same unscanned entry.</summary>
    private int _trackedScanIndex;
    // The scan's answers depend on more than the journal + rules: CharmTracker.FadeLabel
    // reads its own hold ledger, SpellFadeMatches/BuffFadeMatches classify via the (learning)
    // spell catalog, and Kill rules ask IsPet against the CURRENT pet name. A
    // from-scratch scan re-evaluates all of that per tick; the incremental scan must
    // therefore rebuild whenever any of them changes, or an already-folded event
    // could be stuck with a stale answer. Each is a cheap revision/identity check.
    private int _trackedSpellsRevision = -1;
    private int _trackedHoldRevision = -1;
    private string _trackedPetName = "";
    /// <summary>The enabled rules the tracked scan evaluates, in rule order — the
    /// subsequence the fingerprint (and the accumulator list) describes.</summary>
    private static List<TrackedRule> ActiveTrackedRules(IReadOnlyList<TrackedRule> rules)
    {
        var active = new List<TrackedRule>(rules.Count);
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            if (rule.EffectivePattern.Length == 0 && !rule.IsMatchAllKind) continue;
            active.Add(rule);
        }
        return active;
    }

    /// <summary>Incremental tracked-rule scan (perf audit #10). Behavior contract: the
    /// result must always equal a from-scratch scan of the current journal (the test
    /// seam <see cref="TrackedScanFromScratch"/> is that oracle) — EXCEPT Text rules
    /// once the raw-line prune has run (perf audit #5): their accumulators keep
    /// counts whose raw events have aged out of the journal, deliberately, so a
    /// text total never shrinks mid-session. Accumulators fold
    /// forward over appended entries only; they rebuild from index 0 whenever anything
    /// an already-folded answer could have depended on changed — the rules fingerprint,
    /// the learning spell catalog (SpellFade classification), the charm-hold lookaside
    /// (FadeLabel), or the current pet name (Kill rules' IsPet). Session resets null
    /// the accumulators in <see cref="ResetLocked"/>. The journal prune never removes
    /// an entry a rule can match, so pruning only shifts the index.</summary>
    private List<TrackedScan> ScanTrackedLocked(IReadOnlyList<TrackedRule> rules, string rulesFp)
    {
        var active = ActiveTrackedRules(rules);
        var petName = _charm.PetName ?? "";
        var fpChanged = _trackedAccs is null || _trackedAccFingerprint != rulesFp;
        var stateChanged = _trackedSpellsRevision != _spells.Revision
            || _trackedHoldRevision != _charm.HoldRevision
            || !string.Equals(_trackedPetName, petName, StringComparison.OrdinalIgnoreCase);
        var start = _trackedScanIndex;
        var textStart = start;
        if (fpChanged || stateChanged)
        {
            // Raw lines are pruned past retention (perf audit #5), so the Text
            // accumulators are the ONLY keeper of pre-retention text counts. A
            // state-triggered rescan (spell catalog / charm holds / pet name — none
            // of which a Text rule's match depends on) therefore KEEPS them and
            // re-folds only the entries they haven't seen; every other kind
            // rebuilds from the journal as before. Only a rules edit (fingerprint
            // change) rebuilds Text accumulators too — their counts then honestly
            // reflect the retained window, the same trade the combat prune makes.
            var fresh = new List<TrackedAcc>(active.Count);
            for (var i = 0; i < active.Count; i++)
                fresh.Add(!fpChanged && active[i].Kind == WatchKind.Text
                    ? _trackedAccs![i] : new TrackedAcc());
            textStart = fpChanged ? 0 : _trackedScanIndex;
            start = 0;
            _trackedAccs = fresh;
            _trackedAccFingerprint = rulesFp;
            _trackedSpellsRevision = _spells.Revision;
            _trackedHoldRevision = _charm.HoldRevision;
            _trackedPetName = petName;
        }
        for (var i = start; i < _journal.Count; i++)
        {
            var evt = _journal[i];
            for (var r = 0; r < active.Count; r++)
            {
                if (i < textStart && active[r].Kind == WatchKind.Text) continue;
                FoldTrackedEvent(active[r], _trackedAccs![r], evt);
            }
        }
        _trackedScanIndex = _journal.Count;

        var scans = new List<TrackedScan>(active.Count);
        for (var r = 0; r < active.Count; r++)
        {
            var rule = active[r];
            var acc = _trackedAccs[r];
            scans.Add(new TrackedScan(
                rule.Name.Length > 0 ? rule.Name : rule.Pattern,
                rule.Id, acc.Total,
                acc.Items.OrderByDescending(kv => kv.Value)
                    .Select(kv => new NameCount(kv.Key, kv.Value)).ToList(),
                acc.First, acc.Last, acc.LastItem));
        }
        return scans;
    }

    /// <summary>TEST SEAM (perf audit #10): the tracked results recomputed from scratch
    /// over the current journal, bypassing the incremental accumulators — the oracle
    /// the incremental path is asserted equal to. Never used by production callers.</summary>
    internal List<TrackedRuleResult> TrackedScanFromScratch(IReadOnlyList<TrackedRule> rules)
    {
        lock (_lock)
        {
            // Same rate denominators BuildSnapshotLocked derives (both event-derived).
            var elapsed = _sessionStart is { } ss && _lastEventTime is { } le
                ? (le - ss) : TimeSpan.Zero;
            var hours = Math.Max(elapsed.TotalHours, 1.0 / 60);
            var activeSeconds = Math.Min(_activeBuckets.Count * ActiveBucket.TotalSeconds,
                Math.Max(elapsed.TotalSeconds, ActiveBucket.TotalSeconds));
            var activeHours = Math.Max(activeSeconds / 3600.0, 1.0 / 60);

            var active = ActiveTrackedRules(rules);
            var results = new List<TrackedRuleResult>(active.Count);
            foreach (var rule in active)
            {
                var acc = new TrackedAcc();
                foreach (var evt in _journal) FoldTrackedEvent(rule, acc, evt);
                results.Add(new TrackedRuleResult(
                    rule.Name.Length > 0 ? rule.Name : rule.Pattern,
                    acc.Total,
                    acc.Items.OrderByDescending(kv => kv.Value)
                        .Select(kv => new NameCount(kv.Key, kv.Value)).ToList(),
                    acc.Total / hours, acc.Total / activeHours,
                    acc.First, acc.Last, acc.LastItem, rule.Id));
            }
            return results;
        }
    }

    /// <summary>Fold one journal entry into one rule's accumulator — the match logic
    /// is the old per-tick rescan's, unchanged, just applied once per entry.</summary>
    private void FoldTrackedEvent(TrackedRule rule, TrackedAcc acc, GameEvent evt)
    {
        var (item, qty) = (rule.Kind, evt) switch
        {
            (WatchKind.Loot, LootEvent l) when rule.Matches(l.Item) => (l.Item, 1),
            (WatchKind.Loot, AutoSellEvent a) when rule.Matches(a.Item) => (a.Item, a.Count),
            (WatchKind.Kill, KillEvent k) when (k.Killer == "You" || IsPet(k.Killer))
                && rule.Matches(k.Target) => (k.Target, 1),
            (WatchKind.SkillUp, SkillUpEvent su) when rule.Matches(su.Skill) => (su.Skill, 1),
            (WatchKind.Death, DeathEvent de) when rule.Matches(de.Killer)
                => ($"Slain by {de.Killer}", 1),
            (WatchKind.Milestone, LevelEvent lev) => ($"Level {lev.Level}", 1),
            (WatchKind.Milestone, AaEvent) => ("AA point", 1),
            (WatchKind.SpellFade, SpellWornOffEvent { Pet: false } wo)
                when SpellFadeMatches(rule, wo.Spell)
                => (_charm.FadeLabel(wo), 1),
            // Buff/HoT fades carry candidate spells (the log named
            // none); the rule fires if ANY candidate satisfies it, and
            // the row shows the catalog label ("Haste") since we can't
            // know which haste it was. CC filters are excluded: these
            // flavor lines are first-person — something wore off YOU —
            // while the CC filters mean "my control of a MOB ended".
            // rahvynn (#69): once the fade catalog learned "You are no
            // longer stunned.", the default CC-broke rule fired every
            // time an NPC's stun on HIM wore off. ByName/AnySpell/HoT
            // still hear self-fades — watching your own buffs is their job.
            (WatchKind.SpellFade, BuffFadeEvent bf)
                when !IsCcFilter(rule.SpellFilter) && BuffFadeMatches(rule, bf)
                => (bf.Label, 1),
            // Re-matched here rather than trusted from ingest: the journal
            // holds lines kept for ANY text rule, so each rule still has to
            // claim its own. The line itself is the item, so a raid script
            // repeating the same call groups into one row with a count.
            (WatchKind.Text, RawLineEvent raw) when rule.Matches(raw.Line)
                => (Ellipsize(raw.Line), 1),
            _ => (null, 0),
        };
        if (item is null) return;
        acc.Items[item] = acc.Items.TryGetValue(item, out var c) ? c + qty : qty;
        acc.Total += qty;
        acc.First ??= evt.Time;
        acc.Last = evt.Time;
        acc.LastItem = item;
    }
}
