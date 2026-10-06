/**
 * True when the app runs as an installed Home Screen / desktop app rather than
 * in a browser tab. On iPhone this matters for sign-in: the installed app keeps
 * its own storage, and links from Mail always open in the browser instead.
 */
export function isStandaloneDisplay(): boolean {
  if (typeof window === "undefined") return false;
  const iosStandalone = (window.navigator as Navigator & { standalone?: boolean }).standalone === true;
  const mediaStandalone = typeof window.matchMedia === "function" && window.matchMedia("(display-mode: standalone)").matches;
  return iosStandalone || mediaStandalone;
}
