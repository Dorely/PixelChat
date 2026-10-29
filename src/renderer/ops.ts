/** A failed operation, keeping the registry's error code so views can offer a specific recovery. */
export class OperationError extends Error {
  constructor(readonly code: string, message: string, readonly details?: unknown) { super(message) }
}

/** Calls a registry operation as the user and unwraps its envelope. */
export async function op<T = unknown>(name: string, args: unknown = {}): Promise<T> {
  const response = await window.pixel.operation(name, args)
  if (!response.ok) throw new OperationError(response.error?.code ?? 'FAILED', response.error?.message ?? 'Operation failed.', response.error?.details)
  return response.result as T
}
