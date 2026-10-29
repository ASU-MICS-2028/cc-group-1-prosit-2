/** aria props tying a control to its Field's hint/error. */
export function describe(id: string, error?: string) {
  return {
    id,
    "aria-invalid": error ? true : undefined,
    "aria-describedby": error ? `${id}-error` : `${id}-hint`,
  }
}
