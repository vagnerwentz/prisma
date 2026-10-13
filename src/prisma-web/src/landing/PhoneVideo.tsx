import { Play } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { Chapter, ChapterVideo, VideoSources } from './useChapterVideo'

// O vídeo do app num celular e os capítulos dele (a lógica fica em useChapterVideo.ts).
export function PhoneVideo({
  sources,
  video,
  className,
}: {
  sources: VideoSources
  video: ChapterVideo
  className?: string
}) {
  return (
    <figure className={cn('phone relative', className)}>
      {/* A primeira cena atrás do vídeo, como imagem "lazy": o atributo poster baixaria logo ao abrir a página, mesmo
          longe da seção. Quando o vídeo toca, ele cobre a imagem. */}
      <img className="shot-light video-poster" src={sources.posterLight} alt="" loading="lazy" decoding="async" />
      <img className="shot-dark video-poster" src={sources.posterDark} alt="" loading="lazy" decoding="async" />
      {[
        { className: 'shot-light', src: sources.light },
        { className: 'shot-dark', src: sources.dark },
      ].map((v, i) => (
        <video
          key={v.className}
          {...video.bind(i)}
          className={cn(v.className, 'relative')}
          src={v.src}
          width={390}
          height={844}
          muted
          loop
          playsInline
          preload="none"
          aria-label={sources.label}
        />
      ))}
      {video.paused && (
        <button type="button" onClick={() => video.jump(video.chapter)} className="video-play" aria-label="Tocar o vídeo">
          <Play aria-hidden className="size-6 translate-x-0.5" fill="currentColor" />
        </button>
      )}
    </figure>
  )
}

// Capítulos como lista grande, com texto (o vídeo de 20 segundos).
export function ChapterList({ chapters, video }: { chapters: Chapter[]; video: ChapterVideo }) {
  return (
    <ol className="flex flex-col gap-2">
      {chapters.map((c, i) => (
        <li key={c.title}>
          <button
            type="button"
            onClick={() => video.jump(i)}
            className={cn('chapter w-full rounded-2xl p-4 text-left sm:p-5', video.chapter === i && 'chapter-active')}
            aria-current={video.chapter === i}
          >
            <span className="flex items-center gap-3">
              <span className="chapter-number">{i + 1}</span>
              <span className="text-lg font-medium">{c.title}</span>
            </span>
            {c.text && <span className="mt-1 block pl-10 text-muted-foreground">{c.text}</span>}
          </button>
        </li>
      ))}
    </ol>
  )
}

// Capítulos como fichas curtas, embaixo do vídeo (a nota de corretagem).
export function ChapterChips({ chapters, video }: { chapters: Chapter[]; video: ChapterVideo }) {
  return (
    <ol className="flex flex-wrap justify-center gap-2">
      {chapters.map((c, i) => (
        <li key={c.title}>
          <button
            type="button"
            onClick={() => video.jump(i)}
            className={cn('chapter flex items-center gap-2 rounded-full py-1.5 pr-3.5 pl-1.5 text-sm', video.chapter === i && 'chapter-active')}
            aria-current={video.chapter === i}
          >
            <span className="chapter-number">{i + 1}</span>
            {c.title}
          </button>
        </li>
      ))}
    </ol>
  )
}
