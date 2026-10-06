import { ChevronRight } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { AccountTile, AssetTile } from '@/components/brand/Tiles'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { FieldError } from '@/components/FieldError'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { useAccounts } from '@/features/accounts/queries'
import { Amount } from '@/features/transactions/Amount'
import { AmountField, Chip, ChipRow, DateChooser, Section } from '@/features/transactions/fields'
import { useDeleteTransaction, useRestoreTransaction } from '@/features/transactions/queries'
import { formatDayHeading, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { AssetPicker } from './AssetPicker'
import { logoSrc, type AssetItem } from './assets'
import {
  defaultPayoutAccount,
  payoutAccounts,
  payoutKindLabels,
  payoutKinds,
  suggestedKind,
  type Payout,
  type PayoutKind,
} from './payouts'
import { usePayouts, useSavePayout } from './queries'

// O painel dos proventos (docs/investimentos.md, etapa 5b): lançar um, ver os de um dia e editar. Um painel só,
// para o dia abrir a edição sem fechar e abrir outro.
export type PayoutView = { kind: 'new'; asset?: AssetItem } | { kind: 'day'; ids: string[] } | { kind: 'edit'; id: string }

export function PayoutSheet({ view, onClose }: { view: PayoutView | null; onClose: () => void }) {
  // Do dia para a edição de um item, dentro do mesmo painel. A visão é comparada pelo conteúdo: a lista de
  // lançamentos a recria a cada desenho.
  const viewKey = view ? JSON.stringify(view) : ''
  const [editing, setEditing] = useState<{ from: string; id: string } | null>(null)
  const current = editing && editing.from === viewKey ? ({ kind: 'edit', id: editing.id } as const) : view
  const payouts = usePayouts().data ?? []

  return (
    <BottomSheet open={view !== null} onClose={onClose}>
      {current?.kind === 'new' && <PayoutForm key="new" asset={current.asset} payouts={payouts} onDone={onClose} />}
      {current?.kind === 'edit' && (
        <EditPayout key={current.id} payout={payouts.find((p) => p.id === current.id)} payouts={payouts} onDone={onClose} />
      )}
      {current?.kind === 'day' && (
        <PayoutDay items={payouts.filter((p) => current.ids.includes(p.id))} onOpen={(id) => setEditing({ from: viewKey, id })} />
      )}
    </BottomSheet>
  )
}

function EditPayout({ payout, payouts, onDone }: { payout: Payout | undefined; payouts: Payout[]; onDone: () => void }) {
  // Excluído em outra aba, ou a lista ainda carregando: não há o que editar.
  if (!payout)
    return (
      <header className="px-4 pt-2 pb-6">
        <SheetTitle className="font-display text-2xl font-normal">Provento</SheetTitle>
        <p className="text-sm text-muted-foreground">Este provento não está mais aqui.</p>
      </header>
    )
  return <PayoutForm payout={payout} payouts={payouts} onDone={onDone} />
}

function PayoutDay({ items, onOpen }: { items: Payout[]; onOpen: (id: string) => void }) {
  const today = todayInSaoPaulo()
  const total = items.reduce((sum, p) => sum + p.amountCents, 0)
  return (
    <>
      <header className="flex items-end justify-between gap-3 px-4 pt-2 pb-3">
        <div>
          <SheetTitle className="font-display text-2xl font-normal">Proventos</SheetTitle>
          {items[0] && <p className="text-sm text-muted-foreground">{formatDayHeading(items[0].date, today)}</p>}
        </div>
        <Amount type="Income" cents={total} className="font-display text-2xl font-normal" />
      </header>
      <ul className="flex min-h-0 flex-col overflow-y-auto overscroll-contain px-2 pb-6">
        {items.map((p) => (
          <li key={p.id}>
            <button
              type="button"
              onClick={() => onOpen(p.id)}
              className="flex w-full items-center gap-3 rounded-xl px-2 py-2.5 text-left transition-colors hover:bg-muted/50 active:bg-muted/70"
            >
              <AssetTile symbol={p.symbol} logo={logoSrc({ id: p.assetId, kind: p.assetKind, hasLogo: p.assetHasLogo })} />
              <span className="min-w-0 flex-1">
                <span className="block font-medium">
                  {payoutKindLabels[p.kind as PayoutKind]} · {p.symbol}
                </span>
                <span className="block truncate text-sm text-muted-foreground">{p.assetName}</span>
              </span>
              <Amount type="Income" cents={p.amountCents} />
              <ChevronRight className="size-4 shrink-0 text-muted-foreground" />
            </button>
          </li>
        ))}
      </ul>
    </>
  )
}

type Errors = Partial<Record<'asset' | 'amount' | 'account' | 'root', string>>

function PayoutForm({
  payout,
  asset: presetAsset,
  payouts,
  onDone,
}: {
  payout?: Payout
  asset?: AssetItem
  payouts: Payout[]
  onDone: () => void
}) {
  const accounts = useAccounts().data ?? []
  const save = useSavePayout()
  const remove = useDeleteTransaction()
  const restore = useRestoreTransaction()

  const [asset, setAsset] = useState<AssetItem | null>(
    payout
      ? {
          id: payout.assetId,
          symbol: payout.symbol,
          name: payout.assetName,
          kind: payout.assetKind,
          isActive: true,
          hasLogo: payout.assetHasLogo,
        }
      : (presetAsset ?? null),
  )
  const [kind, setKind] = useState<PayoutKind | null>((payout?.kind as PayoutKind | undefined) ?? null)
  const [amountCents, setAmountCents] = useState(payout?.amountCents ?? 0)
  const [date, setDate] = useState(payout?.date ?? todayInSaoPaulo())
  const [chosenAccount, setChosenAccount] = useState<string | null>(payout?.accountId ?? null)
  const [errors, setErrors] = useState<Errors>({})

  const choices = payoutAccounts(accounts)
  // A conta chega com a lista: a do último provento, senão a de investimento, senão a primeira.
  const accountId = chosenAccount ?? defaultPayoutAccount(accounts, payouts)?.id ?? null
  // O tipo segue o ativo até a pessoa escolher outro.
  const effectiveKind: PayoutKind = kind ?? (asset ? suggestedKind(asset.kind) : 'Dividend')

  // Espera a gravação e só então avisa e fecha: a lista recarrega antes, e o painel pode trocar de visão no meio
  // (o dia perde o provento excluído). Com mutate, os avisos se perderiam junto com o form.
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const found: Errors = {
      asset: asset ? undefined : 'Escolha o ativo.',
      amount: amountCents > 0 ? undefined : 'Digite o valor que caiu.',
      account: accountId ? undefined : 'Escolha a conta onde o dinheiro caiu.',
    }
    setErrors(found)
    if (!asset || !accountId || amountCents <= 0) return

    try {
      await save.mutateAsync({ id: payout?.id, body: { accountId, assetId: asset.id, kind: effectiveKind, amountCents, date } })
      toast(payout ? 'Provento salvo' : `${payoutKindLabels[effectiveKind]} de ${asset.symbol} lançado`)
      onDone()
    } catch (error) {
      setErrors({ root: (error as Error).message })
    }
  }

  const deletePayout = async () => {
    if (!payout) return
    try {
      await remove.mutateAsync(payout.id)
      toast('Provento excluído', {
        description: `${payoutKindLabels[payout.kind as PayoutKind]} de ${payout.symbol}, ${formatCents(payout.amountCents)}`,
        duration: 8000,
        action: { label: 'Desfazer', onClick: () => restore.mutate(payout.id) },
      })
      onDone()
    } catch (error) {
      setErrors({ root: (error as Error).message })
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">{payout ? 'Editar provento' : 'Novo provento'}</SheetTitle>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-1 pb-6">
        <Section title="Ativo">
          <AssetPicker value={asset} onChange={setAsset} autoFocus={!asset} />
          <FieldError message={errors.asset} />
        </Section>
        <Section title="Tipo">
          <ChipRow>
            {payoutKinds.map((k) => (
              <Chip key={k} selected={k === effectiveKind} onClick={() => setKind(k)}>
                {payoutKindLabels[k]}
              </Chip>
            ))}
          </ChipRow>
        </Section>
        <AmountField value={amountCents} onChange={setAmountCents} income error={errors.amount} label="Valor recebido" compact />
        <Section title="Data do pagamento">
          <DateChooser value={date} onChange={setDate} />
        </Section>
        <Section title="Caiu na conta">
          {choices.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              O provento cai numa conta.{' '}
              <Link to="/contas" className="font-medium text-foreground underline underline-offset-4">
                Crie uma em Contas
              </Link>
              .
            </p>
          ) : (
            <ChipRow>
              {choices.map((a) => (
                <Chip key={a.id} selected={a.id === accountId} onClick={() => setChosenAccount(a.id)}>
                  <AccountTile name={a.name} type={a.type} size="sm" />
                  {a.name}
                </Chip>
              ))}
            </ChipRow>
          )}
          <FieldError message={errors.account} />
        </Section>
        <FieldError message={errors.root} />
      </div>
      <SheetFooterBar className="flex gap-2">
        {payout && (
          <Button
            type="button"
            variant="ghost"
            size="lg"
            className="h-12 rounded-2xl"
            disabled={remove.isPending}
            onClick={deletePayout}
          >
            Excluir
          </Button>
        )}
        <Button type="submit" size="lg" disabled={save.isPending} className="h-12 flex-1 rounded-2xl text-base">
          {save.isPending ? 'Salvando…' : payout ? 'Salvar' : 'Lançar provento'}
        </Button>
      </SheetFooterBar>
    </form>
  )
}
