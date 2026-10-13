import { useState } from 'react'
import { toast } from 'sonner'
import { BottomSheet } from '@/components/BottomSheet'
import { AssetTile } from '@/components/brand/Tiles'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { SheetTitle } from '@/components/ui/sheet'
import { logoSrc } from './assets'
import {
  confirmedToast,
  eventReviewDetail,
  eventReviewTitle,
  parseReportedQuantity,
  showsConfirmAll,
  type EventReview,
} from './eventReviews'
import { useConfirmEvents, useDisputeEvent, useEventReviews, useUndoEventReviews } from './eventReviewQueries'

// "Conferir eventos" (docs/investimentos.md, I.7c2), no alto de Investimentos: o que cada evento fez com a quantidade da
// pessoa, com "Confere" e "Não confere". Conferido, sai da lista, com "Desfazer". "Conferir todos" confere exatamente o
// que a lista mostra, e só aparece com mais de um. Os botões ficam numa linha própria, como no aviso de ativo que chegou.
export function EventReviewList() {
  const reviews = useEventReviews().data ?? []
  const confirm = useConfirmEvents()
  const undo = useUndoEventReviews()
  const [disputing, setDisputing] = useState<EventReview | null>(null)
  if (reviews.length === 0) return null

  const confirmItems = (items: EventReview[]) => {
    const ids = items.map((i) => i.eventId)
    confirm.mutate(ids, {
      onSuccess: () =>
        toast(confirmedToast(items), {
          action: { label: 'Desfazer', onClick: () => undo.mutate(ids, { onError: (error) => toast.error(error.message) }) },
        }),
      onError: (error) => toast.error(error.message),
    })
  }

  return (
    <section className="flex flex-col gap-3" aria-label="Eventos para conferir">
      <div className="flex min-h-9 items-end justify-between gap-2 px-1">
        <div className="flex min-w-0 flex-col">
          <h2 className="text-sm font-medium text-foreground/75">Conferir eventos</h2>
          <p className="text-xs text-muted-foreground">O Prisma aplicou estes eventos à sua carteira. Confira com a corretora.</p>
        </div>
        {showsConfirmAll(reviews.length) && (
          <Button
            size="sm"
            variant="secondary"
            className="shrink-0 rounded-full"
            disabled={confirm.isPending}
            onClick={() => confirmItems(reviews)}
          >
            Conferir todos
          </Button>
        )}
      </div>
      <ul className="surface overflow-hidden rounded-2xl">
        {reviews.map((review) => (
          <li
            key={review.eventId}
            className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 [&+&]:border-t [&+&]:border-border/60"
          >
            <AssetTile
              symbol={review.symbol}
              logo={logoSrc({ id: review.assetId, kind: review.assetKind, hasLogo: review.hasLogo })}
            />
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="font-medium">{eventReviewTitle(review)}</span>
              <span className="text-sm text-muted-foreground tabular-nums">{eventReviewDetail(review)}</span>
            </span>
            <span className="flex w-full justify-end gap-1">
              <Button
                size="sm"
                variant="ghost"
                className="rounded-full text-muted-foreground"
                onClick={() => setDisputing(review)}
              >
                Não confere
              </Button>
              <Button size="sm" className="rounded-full" disabled={confirm.isPending} onClick={() => confirmItems([review])}>
                Confere
              </Button>
            </span>
          </li>
        ))}
      </ul>
      <DisputeSheet review={disputing} onClose={() => setDisputing(null)} />
    </section>
  )
}

// "Não confere": registra para a curadoria, com a quantidade da corretora se a pessoa souber. A carteira não muda.
function DisputeSheet({ review, onClose }: { review: EventReview | null; onClose: () => void }) {
  const [typed, setTyped] = useState('')
  const dispute = useDisputeEvent()
  const undo = useUndoEventReviews()

  const close = () => {
    setTyped('')
    onClose()
  }
  const send = () => {
    if (!review) return
    const eventId = review.eventId
    dispute.mutate(
      { eventId, reportedQuantity: parseReportedQuantity(typed) },
      {
        onSuccess: () => {
          close()
          toast('Avisado. A gente vai conferir esse evento.', {
            action: {
              label: 'Desfazer',
              onClick: () => undo.mutate([eventId], { onError: (error) => toast.error(error.message) }),
            },
          })
        },
        onError: (error) => toast.error(error.message),
      },
    )
  }

  return (
    <BottomSheet open={review !== null} onClose={close}>
      {review && (
        <>
          <header className="px-4 pt-2 pb-3">
            <SheetTitle className="font-display text-2xl font-normal">Não confere</SheetTitle>
            <p className="text-sm text-muted-foreground">
              {eventReviewTitle(review)} · {eventReviewDetail(review)}
            </p>
          </header>
          <div className="flex flex-col gap-4 px-4 pb-6">
            <label className="flex flex-col gap-1.5">
              <span className="text-xs font-medium text-muted-foreground">
                Quanto a corretora mostra de {review.kind === 'Split' ? review.symbol : review.targetSymbol} depois do evento?
                Opcional.
              </span>
              <Input
                inputMode="numeric"
                autoFocus
                value={typed}
                onChange={(e) => setTyped(e.target.value.replace(/\D/g, ''))}
                className="h-11 rounded-xl text-base tabular-nums"
              />
            </label>
            <p className="text-sm text-muted-foreground">
              Sua carteira continua como está. A gente confere o evento e, se estiver errado, corrige para todo mundo.
            </p>
            <Button className="h-11 rounded-2xl" disabled={dispute.isPending} onClick={send}>
              Avisar
            </Button>
          </div>
        </>
      )}
    </BottomSheet>
  )
}
