import { apiRequest } from './apiClient';

export interface CreateExportRequest {
  scope: string;
  sourceId: string;
  format: 'json' | 'pdf' | 'csv';
  detailLevel?: 'summary' | 'standard' | 'full';
  includeCharts?: boolean;
  includeCandles?: boolean;
  includeDiagnostics?: boolean;
  includeTrades?: boolean;
  includeRejectedCandidates?: boolean;
  includeSettings?: boolean;
  includeRawJson?: boolean;
}

export interface ExportJobDto {
  exportId: number;
  scope: string;
  sourceId: string;
  format: string;
  detailLevel: string;
  status: string;
  fileName?: string;
  contentType?: string;
  sizeBytes?: number;
  errorMessage?: string;
  requestedAtUtc: string;
  completedAtUtc?: string;
  downloadUrl?: string;
}

export async function createExport(request: CreateExportRequest) {
  return apiRequest<ExportJobDto>('/exports', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export async function getExport(exportId: number) {
  return apiRequest<ExportJobDto>(`/exports/${exportId}`);
}

export function getExportDownloadUrl(exportId: number) {
  return `/api/v1/exports/${exportId}/download`;
}
