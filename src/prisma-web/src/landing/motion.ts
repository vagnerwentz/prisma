import { useEffect, useRef, useState } from 'react'

// Movimento da página de apresentação (docs/landing.md, D8): cada cena toca uma vez, quando a pessoa chega nela, e
// fica pronta para quem pede menos movimento.

export function prefersReducedMotion(): boolean {
  return typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

// Avisa uma vez quando o elemento entra na tela: as cenas tocam só quando a pessoa chega nelas.
export function useSeenOnce<T extends Element>(threshold = 0.35) {
  const ref = useRef<T>(null)
  const [seen, setSeen] = useState(false)

  useEffect(() => {
    const element = ref.current
    if (!element || seen) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setSeen(true)
          observer.disconnect()
        }
      },
      { threshold },
    )
    observer.observe(element)
    return () => observer.disconnect()
  }, [seen, threshold])

  return [ref, seen] as const
}
