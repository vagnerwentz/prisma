import { SectionTitle } from './parts'
import { ChapterList, PhoneVideo } from './PhoneVideo'
import { useChapterVideo, type Chapter } from './useChapterVideo'
import demoDark from './video/demo-dark.mp4'
import demoLight from './video/demo-light.mp4'
import posterDark from './video/poster-dark.webp'
import posterLight from './video/poster-light.webp'

// O app sendo usado (docs/landing.md, D12): um lançamento, a fatura e a carteira, gravados do app de verdade. Os tempos
// dos capítulos saem da gravação: mudou o roteiro, mudam aqui.
const chapters: Chapter[] = [
  { start: 0, title: 'Lance em segundos', text: 'Valor, descrição, categoria. O iFood é reconhecido pelo nome.' },
  { start: 10.8, title: 'A fatura certa', text: 'A compra no cartão vai para a fatura em que vai ser paga.' },
  { start: 15, title: 'A carteira em cores', text: 'Seus investimentos pelo custo, divididos por tipo.' },
]

const sources = {
  light: demoLight,
  dark: demoDark,
  posterLight,
  posterDark,
  label: 'O Prisma Money em uso: um lançamento no iFood, a fatura do cartão e a carteira de investimentos',
}

export function DemoVideo() {
  const video = useChapterVideo(chapters)

  return (
    <section className="mx-auto max-w-6xl px-5 py-16 sm:px-8 sm:py-24">
      <SectionTitle>Veja em 20 segundos</SectionTitle>
      <p className="lead mt-4 max-w-xl">Um lançamento, a fatura do cartão e a carteira. Do jeito que o app é, sem cortes.</p>
      <div className="mt-10 grid items-center gap-10 lg:grid-cols-[auto_1fr] lg:gap-16">
        <PhoneVideo sources={sources} video={video} className="justify-self-center" />
        <ChapterList chapters={chapters} video={video} />
      </div>
    </section>
  )
}
