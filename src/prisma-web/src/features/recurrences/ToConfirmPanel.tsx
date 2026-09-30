import { useMemo, useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { BottomSheet } from '@/components/BottomSheet'
import { EntryTile } from '@/components/brand/Tiles'
import { MoneyInput } from '@/components/MoneyInput'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { useAccounts } from '@/features/accounts/queries'
import { categoryLabels, useCategories } from '@/features/categories/queries'
import type { Transaction } from '@/features/transactions/queries'
import { ApiError } from '@/lib/api'
import { todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { confirmedMessage, debitDetails } from './autoDebit'
import { useConfirmAmount, useToConfirm } from './queries'

// O painel "Para conferir" (docs/fase-2.md, 2.15, D4), carregado só quando alguém o abre: os logos de marca e
// o campo de valor ficam fora do pacote principal, que o sino (em toda tela) carrega.
export default function ToConfirmSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <BottomSheet open={open} onClose={onClose}>
      <ToConfirmPanel />
    </BottomSheet>
  )
}

function ToConfirmPanel() {
  const toConfirm = useToConfirm()
  const accounts = useAccounts()
  const categories = useCategories()
  const today = todayInSaoPaulo()
  const names = useMemo(() => new Map((accounts.data ?? []).map((a) => [a.id, a.name])), [accounts.data])
  const labels = useMemo(() => categoryLabels(categories.data ?? []), [categories.data])
  const items = toConfirm.data ?? []

  return (
    <>
      <header className="shrink-0 px-5 pt-2 pb-3">
        <SheetTitle className="text-base font-medium">Para conferir</SheetTitle>
        {items.length > 0 && <p className="text-xs text-muted-foreground">Débitos automáticos que saíram com o valor médio.</p>}
      </header>
      <div className="flex min-h-0 flex-col gap-3 overflow-y-auto overscroll-contain px-4 pb-[max(1.5rem,env(safe-area-inset-bottom))]">
        {toConfirm.isSuccess && items.length === 0 && (
          <p className="px-1 pt-2 pb-6 text-sm text-muted-foreground">Nada para conferir.</p>
        )}
        {items.map((t) => (
          <ConfirmCard
            key={t.id}
            transaction={t}
            header={
              <div className="flex items-center gap-3">
                <EntryTile description={t.description} category={t.categoryId ? labels.get(t.categoryId) : undefined} />
                <div className="min-w-0 flex-1">
                  {/* O valor na linha do nome: o detalhe fica com a largura toda, e a conta cabe em 320px. */}
                  <p className="flex items-baseline justify-between gap-2">
                    <span className="truncate font-medium">{t.description || 'Débito automático'}</span>
                    <span className="shrink-0 font-medium tabular-nums">
                      <span className="text-muted-foreground">≈ </span>
                      {formatCents(t.amountCents)}
                    </span>
                  </p>
                  <p className="truncate text-sm text-muted-foreground">{debitDetails(t, names.get(t.accountId), today)}</p>
                </div>
              </div>
            }
          />
        ))}
      </div>
    </>
  )
}

// Conferir um débito (regra 6). O caso comum é um toque: "Confirmar" mantém o valor médio. "Mudou" abre o campo
// para o valor da conta, e o botão vira "Salvar". Também no painel do lançamento, sem o cabeçalho.
export function ConfirmCard({
  transaction,
  header,
  className,
}: {
  transaction: Pick<Transaction, 'id' | 'amountCents' | 'description'>
  header?: ReactNode
  className?: string
}) {
  const [changing, setChanging] = useState(false)
  const [typed, setTyped] = useState(0)
  const confirm = useConfirmAmount()

  const submit = async (amountCents: number | null) => {
    try {
      const confirmed = await confirm.mutateAsync({ id: transaction.id, amountCents })
      toast.success(confirmedMessage(confirmed.description, confirmed.amountCents))
    } catch (error) {
      toast.error(error instanceof ApiError ? error.message : 'Não foi possível conectar. Tente novamente.')
    }
  }

  return (
    <div className={cn('surface flex flex-col gap-3 rounded-2xl p-4', className)}>
      {header ?? <p className="text-sm text-muted-foreground">Saiu com o valor médio. Confira com a conta que chegou.</p>}
      {changing && (
        <label className="flex flex-col gap-1.5">
          <span className="text-xs text-muted-foreground">Valor da conta</span>
          <MoneyInput value={typed} onChange={setTyped} autoFocus className="h-11 rounded-xl text-base tabular-nums" />
        </label>
      )}
      <div className="grid grid-cols-2 gap-2">
        <Button
          variant="outline"
          className="h-11 rounded-2xl"
          disabled={confirm.isPending}
          onClick={() => {
            setChanging(!changing)
            setTyped(0)
          }}
        >
          {changing ? 'Cancelar' : 'Mudou'}
        </Button>
        {changing ? (
          <Button className="h-11 rounded-2xl" disabled={confirm.isPending || typed === 0} onClick={() => submit(typed)}>
            Salvar
          </Button>
        ) : (
          <Button className="h-11 rounded-2xl" disabled={confirm.isPending} onClick={() => submit(null)}>
            Confirmar
          </Button>
        )}
      </div>
    </div>
  )
}
