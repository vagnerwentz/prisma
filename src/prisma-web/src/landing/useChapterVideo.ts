import { useEffect, useRef, useState, type SyntheticEvent } from 'react'
import { prefersReducedMotion } from './motion'

// Vídeo do app num celular (docs/landing.md, D12 e D13): gravação real, muda e repetindo, nas versões clara e escura
// (só a do tema aparece e baixa). Toca quando a seção chega na tela e para quando ela sai; para quem pede menos
// movimento, fica parado com um botão de tocar. Os capítulos acendem pelo tempo do vídeo e, tocados, pulam para o trecho.
export type Chapter = { start: number; title: string; text?: string }
export type VideoSources = { light: string; dark: string; posterLight: string; posterDark: string; label: string }

export function useChapterVideo(chapters: Chapter[]) {
  const videos = useRef<(HTMLVideoElement | null)[]>([])
  const [chapter, setChapter] = useState(0)
  const [paused, setPaused] = useState(true)

  // Só um dos dois vídeos aparece (o do tema); é ele que toca.
  const visibleVideo = () => videos.current.find((v) => v && v.offsetParent !== null) ?? null

  // Observa o próprio vídeo: a seção pode ser mais alta que a tela (no celular, o vídeo e o texto um embaixo do outro).
  useEffect(() => {
    if (prefersReducedMotion()) return
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          const video = entry.target as HTMLVideoElement
          if (video.offsetParent === null) continue
          if (entry.isIntersecting) void video.play().catch(() => {})
          else video.pause()
        }
      },
      { threshold: 0.5 },
    )
    videos.current.forEach((v) => v && observer.observe(v))
    return () => observer.disconnect()
  }, [])

  const jump = (index: number) => {
    const video = visibleVideo()
    if (!video) return
    video.currentTime = chapters[index].start
    setChapter(index)
    void video.play().catch(() => {})
  }

  const bind = (index: number) => ({
    ref: (el: HTMLVideoElement | null) => {
      videos.current[index] = el
    },
    onTimeUpdate: (e: SyntheticEvent<HTMLVideoElement>) =>
      setChapter(Math.max(0, chapters.findLastIndex((c) => e.currentTarget.currentTime >= c.start))),
    onPlay: () => setPaused(false),
    onPause: () => setPaused(true),
  })

  return { chapter, paused, jump, bind }
}

export type ChapterVideo = ReturnType<typeof useChapterVideo>
