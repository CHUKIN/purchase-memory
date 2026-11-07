import { Receipt } from "lucide-react";
import { useState } from "react";
import { findStoreLogo } from "./storeLogos";

/** Tinted background and a darker letter; every pair is at least 4.5:1 (checked in StoreAvatar.test.tsx). */
export const storeAvatarPalette: ReadonlyArray<{ background: string; color: string }> = [
  { background: "#fbe7ef", color: "#a3315f" },
  { background: "#e6edf8", color: "#2f5a9a" },
  { background: "#e3f1e7", color: "#2e6b45" },
  { background: "#efe8f8", color: "#5b3f8f" },
  { background: "#fdeedd", color: "#8a4f10" },
  { background: "#e0f1f3", color: "#1e5f6b" },
  { background: "#f3ecdc", color: "#7a5a12" },
  { background: "#fbe9e6", color: "#a23a2a" }
];

/** FNV-1a over the lower-cased name: the same store always gets the same colours. */
function hashName(name: string): number {
  let hash = 2166136261;
  for (const char of name.toLocaleLowerCase()) {
    hash ^= char.codePointAt(0)!;
    hash = Math.imul(hash, 16777619) >>> 0;
  }
  return hash;
}

export function storeAvatarColors(name: string) {
  return storeAvatarPalette[hashName(name.trim()) % storeAvatarPalette.length];
}

export function storeInitial(name: string | null | undefined): string | null {
  const trimmed = name?.trim();
  if (!trimmed) return null;
  // First code point, so Ż, Ł and letters outside the BMP survive.
  return Array.from(trimmed)[0].toLocaleUpperCase();
}

/**
 * The round mark at the start of a purchase card: the bundled logo of a known
 * chain, otherwise the store's first letter. A logo that fails to load falls
 * back to the letter. Decorative: the store name is read from the card itself.
 */
export function StoreAvatar({ name }: { name: string | null | undefined }) {
  const initial = storeInitial(name);
  const logo = findStoreLogo(name);
  const [failedLogo, setFailedLogo] = useState<string | null>(null);
  if (initial === null) {
    return <span className="pm-store-avatar pm-store-avatar-unknown" aria-hidden="true"><Receipt size={20} /></span>;
  }
  if (logo && failedLogo !== logo.id) {
    return (
      <span className="pm-store-avatar pm-store-avatar-logo" aria-hidden="true">
        <img src={logo.src} alt="" onError={() => setFailedLogo(logo.id)} />
      </span>
    );
  }
  const { background, color } = storeAvatarColors(name!);
  return <span className="pm-store-avatar" style={{ background, color }} aria-hidden="true">{initial}</span>;
}
