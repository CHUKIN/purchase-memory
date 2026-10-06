import { useEffect, useState } from "react";

const debounceMs = 250;

/**
 * The value once it has stopped changing for a moment. It starts settled, so
 * a shared link or a «Назад» step loads its results at once.
 */
export function useDebounced(value: string): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    if (value === debounced) return;
    const timer = window.setTimeout(() => setDebounced(value), debounceMs);
    return () => window.clearTimeout(timer);
  }, [value, debounced]);
  return debounced;
}
