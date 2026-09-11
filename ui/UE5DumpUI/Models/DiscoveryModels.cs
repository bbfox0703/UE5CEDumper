using System;
using System.Collections.Generic;
using System.Globalization;

namespace UE5DumpUI.Models;

/// <summary>
/// A change-driven discovery request: find the (class, property) targets whose
/// value MOVED across an ordered snapshot sequence, ranked by "looks like game
/// state". This is the automatic front-door to Class Pivot — it dissolves the
/// "user must already know which class to pivot" pain (the reason Pivot is
/// under-used when the target is unknown) by using the change signal between
/// snapshots (capture before/after an in-game action) to collapse thousands of
/// classes into a short, ranked shortlist. Reuses the SPC in-memory intersection
/// load (<see cref="SpcQuery"/>'s machinery); ranks instead of requiring a
/// user-authored predicate chain. See docs/experimental-snapshot-spc-pivot.md
/// §"Phase C — C3".
/// </summary>
public sealed class DiscoveryQuery
{
    /// <summary>Snapshot ids, ordered oldest → newest. Must be ≥ 2.</summary>
    public List<long> SnapshotIds { get; set; } = new();

    /// <summary>Cross-snapshot join mode (same identity keys as SPC). Strict is the
    /// safe default; Loose / InSession trade false-positives for recall.</summary>
    public SpcJoinMode JoinMode { get; set; } = SpcJoinMode.Strict;

    /// <summary>Case-insensitive substring on the class name (empty = all).</summary>
    public string ClassContains { get; set; } = "";
    /// <summary>Case-insensitive substring on the property name (empty = all).</summary>
    public string PropContains  { get; set; } = "";

    /// <summary>Per-game class denylist (reuses the Pivot-scope list). Class FQNs
    /// in this set are skipped at load. Null / empty = no filtering.</summary>
    public HashSet<string>? ExcludedClasses { get; set; }

    /// <summary>Max ranked candidates returned (default 200). One extra is probed
    /// to set <see cref="DiscoveryResult.Truncated"/>.</summary>
    public int MaxResults { get; set; } = 200;
}

/// <summary>One (instance, field) candidate fed to
/// <see cref="Services.PivotDiscoveryEngine"/>: its value sequence across the
/// selected snapshots (oldest → newest) plus the identity needed for the
/// representative-instance handoff. The engine rolls these up per (class, prop).</summary>
public sealed class DiscoveryInput
{
    public string ClassName    { get; set; } = "";
    public string PropName     { get; set; } = "";
    public string DeclaredType { get; set; } = "";
    public string NormPath     { get; set; } = "";
    /// <summary>[W1-DISCOVER-ARRAY] The struct-array field this row is an element of ("" for a scalar),
    /// and the element's inner prop ("" for a leaf-container element). <see cref="PropName"/> is the
    /// rendered "Array[N].Inner", which the scalar field list can never contain.</summary>
    public string ArrayField   { get; set; } = "";
    public string InnerProp    { get; set; } = "";
    /// <summary>Newest-snapshot live address — the Pivot / CE-export handoff target.</summary>
    public string ObjAddr      { get; set; } = "";
    /// <summary>Raw hex per snapshot, oldest → newest (exact change detection).</summary>
    public IReadOnlyList<string>  Hex { get; set; } = Array.Empty<string>();
    /// <summary>Numeric value per snapshot, oldest → newest (direction). Null entries
    /// are non-numeric / undecodable.</summary>
    public IReadOnlyList<double?> Num { get; set; } = Array.Empty<double?>();
}

/// <summary>One ranked discovery candidate: a (class, property) whose value moved
/// across the snapshots, with the change stats + a representative instance for the
/// one-click Pivot handoff.</summary>
public sealed class DiscoveryCandidate
{
    public string ClassName    { get; set; } = "";
    public string PropName     { get; set; } = "";
    public string DeclaredType { get; set; } = "";
    /// <summary>[W1-DISCOVER-ARRAY] Array identity of a struct-array element candidate (both "" for a
    /// scalar), so "Use →" can pivot it through the Snapshot Array source.</summary>
    public string ArrayField   { get; set; } = "";
    public string InnerProp    { get; set; } = "";

    /// <summary>Instances of this (class, prop) present in ALL selected snapshots.</summary>
    public int InstancesTotal   { get; set; }
    /// <summary>How many of those instances had a changing value sequence.</summary>
    public int InstancesChanged { get; set; }

    /// <summary>"↑" monotonic up, "↓" monotonic down, "↕" mixed, "" non-numeric/flat.</summary>
    public string Direction { get; set; } = "";
    /// <summary>A representative changed instance's value sequence ("100  →  80  →  60").</summary>
    public string SampleSequence { get; set; } = "";
    /// <summary>Representative changed instance's newest-snapshot address (Pivot / CE).</summary>
    public string ObjAddr  { get; set; } = "";
    /// <summary>Representative changed instance's normalised path (display).</summary>
    public string NormPath { get; set; } = "";

    /// <summary>PropertyScoringTable interest (HP / Gold / Damage rank high).</summary>
    public int InterestScore { get; set; }
    /// <summary>Interest category display name (e.g. "Stats", "Resources", "Other").</summary>
    public string CategoryName  { get; set; } = "";
    /// <summary>Category chip foreground hex (for the results grid).</summary>
    public string CategoryColor { get; set; } = "";

    /// <summary>Composite rank score (higher = more likely the intended target).
    /// The sub-scores below are exposed for a future cross-game calibration pass
    /// (mirrors how PropertyScoringTable was tuned).</summary>
    public double Score { get; set; }
    public double ChangeScore       { get; set; }
    public double Selectivity       { get; set; }
    public double PopulationPenalty { get; set; }

    /// <summary>Shape sub-score (only meaningful with ≥3 snapshots): 1.0 = a single
    /// discrete change (the in-game action you captured around) — or a 2-snapshot
    /// compare where shape can't be inferred; 0.5 = a steady monotonic trend (e.g. a
    /// draining resource); 0.0 = jitter on every interval (per-frame render/anim/
    /// physics noise). Exposed for a future cross-game calibration pass.</summary>
    public double ShapeScore { get; set; }
    /// <summary>How many of the N-1 snapshot intervals the representative moved in.</summary>
    public int ChangeIntervals { get; set; }
    /// <summary>Human-readable shape ("one-time" / "trend 2/3" / "jitter 3/3"); blank
    /// when only two snapshots were compared (one interval — shape can't be inferred).</summary>
    public string ShapeLabel { get; set; } = "";

    /// <summary>"3/50" — changed instances over total. Compact column display.</summary>
    public string ChangedDisplay => $"{InstancesChanged:N0}/{InstancesTotal:N0}";

    /// <summary>One-decimal score for the grid (sorts on the numeric <see cref="Score"/>).</summary>
    public string ScoreDisplay => Score.ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>Result of a change-driven discovery: the ranked candidates + stats.</summary>
public sealed class DiscoveryResult
{
    public List<DiscoveryCandidate> Rows { get; } = new();
    /// <summary>Number of snapshots in the evaluated sequence.</summary>
    public int SnapshotCount { get; set; }
    /// <summary>Total (class, prop) groups that changed (before the result cap).</summary>
    public int ChangedGroups { get; set; }
    /// <summary>True when the candidate cap was hit.</summary>
    public bool Truncated { get; set; }
}
