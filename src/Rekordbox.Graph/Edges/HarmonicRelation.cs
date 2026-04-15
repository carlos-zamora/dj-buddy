namespace DJBuddy.Rekordbox.Graph;

/// <summary>
/// The harmonic relationship between two tracks' Camelot keys.
/// </summary>
/// <remarks>
/// Directionality matters for energy-shifting relations: <see cref="EnergyBoost"/> and
/// <see cref="EnergyDrop"/> are not symmetric — the reverse of a boost is a drop, not another
/// boost. <see cref="Same"/> and <see cref="Adjacent"/> are symmetric.
/// </remarks>
public enum HarmonicRelation
{
    /// <summary>Identical Camelot key (e.g. 6A → 6A).</summary>
    Same,

    /// <summary>Neighbor on the Camelot wheel (±1 same letter) or relative major/minor (same number, different letter).</summary>
    Adjacent,

    /// <summary>Energy-boost transition: +2 steps on the Camelot wheel (e.g. 6A → 8A). Directional.</summary>
    EnergyBoost,

    /// <summary>Energy-drop transition: −2 steps on the Camelot wheel (e.g. 6A → 4A). Directional.</summary>
    EnergyDrop,
}
