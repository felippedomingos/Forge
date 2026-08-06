namespace Forge.Domain.Entities;

// Founder-requested: every project gets a color, always pastel - never a free-form hex
// input (which could land on something saturated/illegible). A fixed palette is the
// simplest way to guarantee "pastel" by construction rather than validating HSL ranges
// on every write. Referenced by both POST /projects (auto-assign) and PATCH /projects/{id}
// (restrict edits to these values) so there's exactly one source of truth - see
// Program.cs. The backfill migration (AddProjectColor) hardcodes this same list in raw
// SQL since migrations can't reference application code; keep both in sync if this list
// ever changes.
public static class ProjectColorPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#FFD1DC", // pastel pink
        "#FFE4B5", // pastel peach
        "#FFFACD", // pastel yellow
        "#D4F1D4", // pastel green
        "#C1E7FF", // pastel blue
        "#D9C6F0", // pastel purple
        "#FFCCCB", // pastel salmon
        "#C6F0EB", // pastel teal
        "#F0D9E8", // pastel orchid
        "#E8DCC8", // pastel tan
    ];

    public static bool IsValid(string color) => Colors.Contains(color);
}
