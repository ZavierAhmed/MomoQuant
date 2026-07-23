import { apiRequest } from '@/api/apiClient';
import type { RecalculateIndicatorsRequest, RecalculateIndicatorsResponse } from '@/api/domainTypes';

export const indicatorsApi = {
  recalculate: (body: RecalculateIndicatorsRequest) =>
    apiRequest<RecalculateIndicatorsResponse>('/indicators/recalculate', { method: 'POST', body }),
};
