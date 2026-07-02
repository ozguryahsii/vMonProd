import { useEffect, useState, useCallback } from "react";

export interface AsyncState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  reload: () => void;
}

/** Basit veri çekme kancası: yükleme/hata/veri + iptal + yeniden yükleme + opsiyonel periyodik yenileme. */
export function useAsync<T>(fn: (signal: AbortSignal) => Promise<T>, refreshMs?: number): AsyncState<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const reload = useCallback(() => setTick((t) => t + 1), []);

  useEffect(() => {
    const ctrl = new AbortController();
    let alive = true;
    (async () => {
      try {
        setError(null);
        const d = await fn(ctrl.signal);
        if (alive) setData(d);
      } catch (e) {
        if (alive && (e as Error).name !== "AbortError") setError((e as Error).message);
      } finally {
        if (alive) setLoading(false);
      }
    })();

    let iv: ReturnType<typeof setInterval> | undefined;
    if (refreshMs) iv = setInterval(reload, refreshMs);
    return () => {
      alive = false;
      ctrl.abort();
      if (iv) clearInterval(iv);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tick]);

  return { data, loading, error, reload };
}
