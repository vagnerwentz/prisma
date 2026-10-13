import { Plus } from 'lucide-react'
import { AccountTile } from '@/components/brand/Tiles'
import { StaleFade } from '@/components/StaleFade'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { formatShortDate } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { WaitingRequests } from './AssetRequestList'
import type { AssetRequest } from './assetRequests'
import { useTradeNotes } from './queries'
import { noteTitle, notesTotals, type TradeNote } from './trades'

// A aba Notas (docs/investimentos.md, I.7e): as notas do ano, quanto comprou e vendeu nele, e os avisos de ativo que
// esperam chegar.
export function NotesTab({
  year,
  requests,
  onOpenNote,
  onNewNote,
}: {
  year: number
  requests: AssetRequest[]
  onOpenNote: (id: string) => void
  onNewNote: () => void
}) {
  const notes = useTradeNotes({ year })
  const list = notes.data ?? []
  const { buyCents, sellCents } = notesTotals(list)

  return (
    <div className="flex flex-col gap-6">
      {notes.isPending ? (
        <Skeleton className="h-60 w-full rounded-2xl" />
      ) : notes.isError ? (
        // Sem as notas, nada de "Nenhuma nota": leria como verdade.
        <Alert variant="destructive">
          <AlertDescription>Não foi possível carregar suas notas.</AlertDescription>
        </Alert>
      ) : (
        <StaleFade stale={notes.isPlaceholderData} className="flex flex-col gap-3">
          <div className="flex flex-wrap items-end justify-between gap-x-3 gap-y-2">
            <div className="flex min-w-0 flex-col px-1">
              <h2 className="text-sm font-medium text-foreground/75">
                {list.length === 1 ? '1 nota' : `${list.length} notas`} em {year}
              </h2>
              {list.length > 0 && (
                <p className="text-sm text-muted-foreground tabular-nums">
                  Comprou {formatCents(buyCents)}
                  {sellCents > 0 && ` · vendeu ${formatCents(sellCents)}`}
                </p>
              )}
            </div>
            <Button size="sm" className="rounded-full" onClick={onNewNote}>
              <Plus />
              Nova nota
            </Button>
          </div>
          {list.length > 0 ? (
            <ul className="surface overflow-hidden rounded-2xl">
              {list.map((note) => (
                <li key={note.id} className="[&+&]:border-t [&+&]:border-border/60">
                  <NoteRow note={note} onOpen={() => onOpenNote(note.id)} />
                </li>
              ))}
            </ul>
          ) : (
            <p className="surface rounded-2xl px-4 py-6 text-center text-sm text-muted-foreground">
              Nenhuma nota em {year}. Lance suas notas de corretagem para ver quantidade e preço médio.
            </p>
          )}
        </StaleFade>
      )}

      <WaitingRequests requests={requests} />
    </div>
  )
}

// Uma nota: a corretora, o que fez e com quais ativos, o dia e quanto movimentou.
function NoteRow({ note, onOpen }: { note: TradeNote; onOpen: () => void }) {
  const { buyCents, sellCents } = notesTotals([note])
  return (
    <button
      type="button"
      onClick={onOpen}
      className="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:bg-muted/60 active:bg-muted/70"
    >
      <AccountTile name={note.broker} type="Investment" />
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="truncate font-medium">{noteTitle(note.trades)}</span>
        <span className="truncate text-sm text-muted-foreground">
          {formatShortDate(note.date)} · {note.broker}
        </span>
      </span>
      <span className="shrink-0 text-sm tabular-nums">{formatCents(buyCents + sellCents)}</span>
    </button>
  )
}
