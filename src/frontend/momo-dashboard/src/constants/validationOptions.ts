export const VALIDATION_MODE_OPTIONS = [
  { label: 'Standard backtest', value: 'None' },
  { label: '70/30 validation', value: 'InSampleOutOfSample70_30' },
] as const;

export const PARAMETER_MODE_OPTIONS = [
  { label: 'Fixed/manual parameters', value: 'ManualOnly' },
  { label: 'Grid search optimization', value: 'GridSearch' },
  { label: 'Random search optimization', value: 'RandomSearch' },
] as const;

export type ValidationModeValue = (typeof VALIDATION_MODE_OPTIONS)[number]['value'];
export type ParameterModeValue = (typeof PARAMETER_MODE_OPTIONS)[number]['value'];

export function formatValidationApiError(message: string, fieldErrors: Record<string, string>): string {
  if (fieldErrors.validationMode) {
    return 'Validation request failed: validationMode value is invalid.';
  }
  if (fieldErrors.request) {
    return 'Validation request failed: request body is invalid. Check strategy, symbol, dates, and enum values.';
  }
  const fieldMessages = Object.entries(fieldErrors).map(([field, value]) => `${field}: ${value}`);
  if (fieldMessages.length > 0) {
    return `Validation request failed: ${fieldMessages.join(' ')}`;
  }
  if (message === 'Bad Request') {
    return 'Validation request failed: check strategy, symbol, date range, and validation mode.';
  }
  return message.startsWith('Validation request failed') ? message : `Validation request failed: ${message}`;
}
