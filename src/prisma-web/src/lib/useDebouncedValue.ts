import { useEffect, useState } from 'react'

// O valor só muda depois de um tempo sem mudar: para não consultar a API a cada tecla ou giro de data.
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}
