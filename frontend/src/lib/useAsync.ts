import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../api/client';

interface AsyncState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  notFound: boolean;
  reload: () => void;
}

/**
 * Runs a loader when the page mounts or when `deps` change, and tracks
 * loading/error/not-found so every page can render the right state.
 * Call `reload()` after a mutation to re-fetch.
 *
 * The loader is read from a ref, so callers may pass an inline closure without
 * re-running the effect on every render.
 */
export function useAsync<T>(loader: () => Promise<T>, deps: readonly unknown[] = []): AsyncState<T> {
  const loaderRef = useRef(loader);
  loaderRef.current = loader;

  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  const reload = useCallback(() => setReloadKey((key) => key + 1), []);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    setNotFound(false);

    loaderRef
      .current()
      .then((result) => {
        if (active) setData(result);
      })
      .catch((caught: unknown) => {
        if (!active) return;
        if (caught instanceof ApiError) {
          // 401 is handled globally (logout); 404 gets its own "not found" state.
          if (caught.status === 404) setNotFound(true);
          else if (caught.status !== 401) setError(caught.message);
        } else {
          setError('Something went wrong. Please try again.');
        }
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
    // `deps` is the caller's dependency list; `reloadKey` forces a re-fetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, reloadKey]);

  return { data, loading, error, notFound, reload };
}
