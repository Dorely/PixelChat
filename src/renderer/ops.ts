/** Calls a registry operation as the user and unwraps its envelope. */
export async function op<T = unknown>(name: string, args: unknown = {}): Promise<T> {
  const response = await window.pixel.operation(name, args)
  if (!response.ok) throw new Error(response.error?.message ?? 'Operation failed.')
  return response.result as T
}
