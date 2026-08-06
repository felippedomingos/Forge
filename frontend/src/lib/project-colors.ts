// Mirrors backend/src/Forge.Domain/Entities/ProjectColorPalette.cs exactly - keep both
// lists in sync if the palette ever changes. Fixed pastel swatches only: the create
// dialog never lets the founder type an arbitrary hex, so "pastel" holds by
// construction rather than by validating saturation/lightness on every save.
export const PROJECT_COLOR_PALETTE = [
  '#FFD1DC', // pastel pink
  '#FFE4B5', // pastel peach
  '#FFFACD', // pastel yellow
  '#D4F1D4', // pastel green
  '#C1E7FF', // pastel blue
  '#D9C6F0', // pastel purple
  '#FFCCCB', // pastel salmon
  '#C6F0EB', // pastel teal
  '#F0D9E8', // pastel orchid
  '#E8DCC8', // pastel tan
] as const

// TaskCard tints the card background with the owning project's color rather than
// replacing it outright - full-opacity pastel reads fine on the light theme's white
// cards but washes out contrast against the dark theme's dark card, so alpha is themed
// (lower in dark mode) and text keeps using the existing --foreground/--muted-foreground
// vars, which already adapt per theme on their own.
export function hexToRgba(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}
