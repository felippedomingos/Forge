import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

// Tag badges (TaskCard/TaskDetailSheet) use a user-picked hex background - this picks
// black or white text against it via relative luminance so the label stays readable
// no matter which color was chosen.
export function getContrastTextColor(hex: string): '#000000' | '#ffffff' {
  const match = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex)
  if (!match) return '#ffffff'
  const [r, g, b] = match.slice(1).map((c) => parseInt(c, 16) / 255)
  const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b
  return luminance > 0.6 ? '#000000' : '#ffffff'
}
