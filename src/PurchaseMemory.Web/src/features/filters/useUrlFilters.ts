import { useCallback, useLayoutEffect, useMemo, useRef } from "react";
import { useSearchParams } from "react-router";

/**
 * Keeps a flat set of string filters in the address bar, so a filtered view
 * can be shared as a link and comes back after opening a purchase and pressing
 * «Назад». Values equal to their default are left out of the URL; unrelated
 * parameters are preserved. Updates replace the history entry, so typing a
 * search does not leave one «Назад» step per keystroke.
 */
export function useUrlFilters<T extends { [K in keyof T]: string }>(
  defaults: T,
  sanitize: (values: T) => T = (values) => values
): [T, (patch: Partial<T>) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  // React Router hands a functional update the params of the last render, so
  // two updates in one event would overwrite each other. The newest params
  // written here are kept until the next render catches up.
  const latest = useRef(searchParams);
  useLayoutEffect(() => {
    latest.current = searchParams;
  }, [searchParams]);

  const values = useMemo(() => {
    const read: Record<string, string> = { ...defaults };
    for (const key of Object.keys(defaults)) {
      const value = searchParams.get(key);
      if (value !== null) read[key] = value;
    }
    return sanitize(read as T);
    // `defaults` and `sanitize` are module constants at every call site.
  }, [searchParams]);

  // A patch, not a whole object: several calls in one event (a reset clearing
  // each field) each touch only their own key and cannot undo one another.
  const update = useCallback((patch: Partial<T>) => {
    const parameters = new URLSearchParams(latest.current);
    const patchValues = patch as Record<string, string | undefined>;
    const defaultValues = defaults as Record<string, string>;
    for (const key of Object.keys(patch)) {
      if (!(key in defaultValues)) continue;
      const value = patchValues[key];
      if (value === undefined || value === defaultValues[key]) parameters.delete(key);
      else parameters.set(key, value);
    }
    latest.current = parameters;
    setSearchParams(parameters, { replace: true });
  }, [setSearchParams]);

  return [values, update];
}
