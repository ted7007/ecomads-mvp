import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { httpClient } from '../api/httpClient';

const recorded = new Set<string>();
const pending = new Set<string>();
export function RecommendationExposure({ runId, evaluationId, ruleId }: { runId: string | null; evaluationId: string; ruleId: string }) {
  const ref = useRef<HTMLSpanElement>(null); const cache = useQueryClient();
  useEffect(() => {
    if (!runId || !ref.current) return;
    const key = `${runId}:${evaluationId}:${ruleId}`;
    if (recorded.has(key)) return;
    let stopped = false;
    const observer = new IntersectionObserver(entries => {
      if (!entries.some(e => e.isIntersecting) || pending.has(key) || recorded.has(key)) return;
      pending.add(key);
      void httpClient<void>('/api/wb/recommendations/exposures', { method: 'POST', body: [{ runId, evaluationId, ruleId }] })
        .then(() => { recorded.add(key); observer.disconnect(); if (!stopped) void cache.invalidateQueries({ queryKey: ['wb-results'] }); })
        .catch(() => { /* A later visible mount retries; never fabricate a successful exposure. */ })
        .finally(() => pending.delete(key));
    });
    observer.observe(ref.current);
    return () => { stopped = true; observer.disconnect(); };
  }, [runId, evaluationId, ruleId, cache]);
  return <span ref={ref} aria-hidden="true" style={{ display: 'block', width: 1, height: 1, marginBottom: -1 }} />;
}
