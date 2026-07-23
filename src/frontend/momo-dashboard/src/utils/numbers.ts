export function requireNumber(value: number | '' | null | undefined, fieldName: string): number {
  if (value === '' || value === null || value === undefined || Number.isNaN(Number(value))) {
    throw new Error(`${fieldName} is required.`);
  }

  return Number(value);
}

export function requireNumberArray(values: number[], fieldName: string): number[] {
  if (!values.length) {
    throw new Error(`${fieldName} requires at least one selection.`);
  }

  return values.map((value) => Number(value));
}

export function requireStringArray(values: string[], fieldName: string): string[] {
  if (!values.length) {
    throw new Error(`${fieldName} requires at least one selection.`);
  }

  return values;
}
