import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef } from 'react';
import { getWbDataState } from '../api/wbDataState';

export function WbDataFreshness({ startDate, endDate, campaignId }: {
  startDate: string; endDate: string; campaignId?: string;
}) {
  const client = useQueryClient();
  const seen = useRef<Record<string, string>>({});
  const state = useQuery({ queryKey: ['wb-data-state', startDate, endDate, campaignId],
    queryFn: () => getWbDataState(startDate, endDate, campaignId),
    refetchInterval: (query) => query.state.data?.active ? 15_000 : 60_000,
    refetchIntervalInBackground: false, refetchOnWindowFocus: true });
  useEffect(() => {
    for (const source of state.data?.sources ?? []) {
      const before = seen.current[source.kind];
      seen.current[source.kind] = source.version;
      if (!before || before === source.version) continue;
      const keys = source.kind === 'fullstats' ? ['projects', 'dashboard-daily', 'campaign-daily',
        'campaign-summary', 'campaign-nomenclatures', 'wb-spend-trend', 'wb-coverage', 'loaded-periods',
        'wb-cluster-recommendations', 'wb-recommendation-summary'] :
        source.kind === 'orders' || source.kind === 'expenses' ? ['dashboard-daily', 'wb-coverage'] :
        source.kind === 'clusters' ? ['wb-clusters', 'wb-cluster-recommendations', 'wb-recommendation-summary'] : ['wb-jam'];
      for (const key of keys) void client.invalidateQueries({ queryKey: [key] });
    }
  }, [client, state.data]);
  useEffect(() => { seen.current = {}; }, [startDate, endDate, campaignId]);
  return null;
}
