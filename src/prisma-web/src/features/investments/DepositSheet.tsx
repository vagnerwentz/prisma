import { useState, type FormEvent } from 'react'
import { toast } from 'sonner'
import { AccountTile } from '@/components/brand/Tiles'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { FieldError } from '@/components/FieldError'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { SheetTitle } from '@/components/ui/sheet'
import { useAccounts } from '@/features/accounts/queries'
import { AmountField, Chip, ChipRow, DateChooser, Section } from '@/features/transactions/fields'
import { formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import {
  defaultIndexPercentE4,
  formatPercent,
  indexKindLabels,
  indexKinds,
  maturityWarning,
  productKinds,
  productLabels,
  initialRateText,
  parseRate,
  percentFromE4,
  percentToE4,
  rateFields,
  type Deposit,
  type Goal,
  type IndexKind,
  type ProductKind,
} from './fixedIncome'
import {
  useCreateDeposit,
  useDeleteDeposit,
  useMoveDeposit,
  useRestoreDeposit,
  useUpdateDeposit,
} from './fixedIncomeQueries'

// Novo aporte ou edição (docs/renda-fixa.md, 7.2). Aporte que saiu de uma conta (financiado) tem valor e data
// travados: mexer neles desencontraria a transferência.
export type DepositView = { kind: 'new'; goalId: string | null } | { kind: 'edit'; deposit: Deposit }

export function DepositSheet({ view, goals, onClose }: { view: DepositView | null; goals: Goal[]; onClose: () => void }) {
  return (
    <BottomSheet open={view !== null} onClose={onClose}>
      {view && (
        <DepositForm
          key={view.kind === 'edit' ? view.deposit.id : `new-${view.goalId}`}
          view={view}
          goals={goals}
          onDone={onClose}
        />
      )}
    </BottomSheet>
  )
}

type Errors = Partial<Record<'amount' | 'percent' | 'maturity' | 'funding' | 'root', string>>

function DepositForm({ view, goals, onDone }: { view: DepositView; goals: Goal[]; onDone: () => void }) {
  const deposit = view.kind === 'edit' ? view.deposit : undefined
  const accounts = (useAccounts().data ?? []).filter((a) => a.isActive && a.type !== 'CreditCard')
  const create = useCreateDeposit()
  const update = useUpdateDeposit()
  const move = useMoveDeposit()
  const remove = useDeleteDeposit()
  const restore = useRestoreDeposit()

  const [requestId] = useState(() => crypto.randomUUID())
  const [goalId, setGoalId] = useState<string | null>(deposit ? deposit.goalId : view.kind === 'new' ? view.goalId : null)
  const defaultE4 = goals.find((g) => g.id === goalId)?.defaultIndexPercentE4 ?? defaultIndexPercentE4
  const [product, setProduct] = useState<ProductKind>(deposit?.productKind ?? 'Cdb')
  const [kind, setKind] = useState<IndexKind>(deposit?.indexKind ?? 'Cdi')
  const [percent, setPercent] = useState<string | null>(deposit ? formatPercent(percentFromE4(deposit.rateE4)).replace('%', '') : null)
  const percentText = percent ?? initialRateText(kind, defaultE4)
  const field = rateFields[kind]
  const [amountCents, setAmountCents] = useState(deposit?.amountCents ?? 0)
  const [date, setDate] = useState(deposit?.depositDate ?? todayInSaoPaulo())
  const [maturity, setMaturity] = useState(deposit?.maturityDate ?? '')
  const [funded, setFunded] = useState(false)
  const [fromId, setFromId] = useState<string | null>(null)
  const [toId, setToId] = useState<string | null>(null)
  const [askingDelete, setAskingDelete] = useState(false)
  const [errors, setErrors] = useState<Errors>({})

  const locked = deposit?.funding != null
  const investmentAccounts = accounts.filter((a) => a.type === 'Investment')
  const sourceAccounts = accounts.filter((a) => a.type !== 'Investment')
  const to = toId ?? investmentAccounts[0]?.id ?? null
  const from = fromId ?? sourceAccounts[0]?.id ?? null
  const pending = create.isPending || update.isPending || move.isPending

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const parsed = parseRate(percentText, kind)
    const found: Errors = {
      amount: amountCents > 0 ? undefined : 'Digite o valor aportado.',
      percent: parsed === null ? field.error : undefined,
      maturity: maturity && maturity < date ? 'O vencimento não pode ser antes do aporte.' : undefined,
      funding: funded && (!from || !to) ? 'Escolha de qual conta saiu e em qual conta de investimento entrou.' : undefined,
    }
    setErrors(found)
    if (found.amount || parsed === null || found.maturity || found.funding) return

    const common = {
      depositDate: date,
      amountCents,
      productKind: product,
      indexKind: kind,
      rateE4: percentToE4(parsed),
      maturityDate: maturity || null,
      liquidity: deposit?.liquidity ?? ('Unknown' as const),
      note: deposit?.note ?? null,
    }
    try {
      if (deposit) {
        await update.mutateAsync({ id: deposit.id, body: common })
        if (goalId !== deposit.goalId) await move.mutateAsync({ id: deposit.id, goalId })
        toast('Aporte salvo', { description: 'Mudou o histórico: o saldo do objetivo foi recalculado.' })
      } else {
        await create.mutateAsync({
          ...common,
          goalId,
          clientRequestId: requestId,
          funding: funded && from && to ? { fromAccountId: from, toAccountId: to } : null,
        })
        toast(`Aporte de ${formatCents(amountCents)} lançado`)
      }
      onDone()
    } catch (error) {
      setErrors({ root: (error as Error).message })
    }
  }

  const deleteDeposit = async (transfer?: 'keep' | 'delete') => {
    if (!deposit) return
    try {
      await remove.mutateAsync({ id: deposit.id, transfer })
      toast('Aporte excluído', {
        description: `${formatShortDate(deposit.depositDate)}, ${formatCents(deposit.amountCents)}`,
        duration: 8000,
        action: { label: 'Desfazer', onClick: () => restore.mutate(deposit.id) },
      })
      onDone()
    } catch (error) {
      setErrors({ root: (error as Error).message })
      setAskingDelete(false)
    }
  }

  if (askingDelete)
    return (
      <>
        <header className="px-4 pt-2 pb-3">
          <SheetTitle className="font-display text-2xl font-normal">Excluir aporte</SheetTitle>
          <p className="text-sm text-muted-foreground">
            Este aporte saiu de uma conta. O que fazer com a transferência de {formatCents(deposit?.amountCents ?? 0)}?
          </p>
        </header>
        <div className="flex flex-col gap-2 px-4 pb-6">
          <Button size="lg" className="h-12 rounded-2xl" disabled={remove.isPending} onClick={() => deleteDeposit('keep')}>
            Manter a transferência
          </Button>
          <Button variant="outline" size="lg" className="h-12 rounded-2xl" disabled={remove.isPending} onClick={() => deleteDeposit('delete')}>
            Excluir junto
          </Button>
          <Button variant="ghost" size="lg" className="h-12 rounded-2xl" onClick={() => setAskingDelete(false)}>
            Voltar
          </Button>
        </div>
      </>
    )

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">{deposit ? 'Editar aporte' : 'Novo aporte'}</SheetTitle>
        {locked && <p className="text-sm text-muted-foreground">Valor e data ficam travados: o dinheiro saiu de uma conta.</p>}
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-1 pb-6">
        {locked ? (
          <Section title="Aportado">
            <p className="font-display text-3xl tabular-nums">{formatCents(amountCents)}</p>
            <p className="text-sm text-muted-foreground">em {formatShortDate(date)}</p>
          </Section>
        ) : (
          <>
            <AmountField value={amountCents} onChange={setAmountCents} income={false} error={errors.amount} label="Valor aportado" compact autoFocus={!deposit} />
            <Section title="Data do aporte">
              <DateChooser value={date} onChange={setDate} />
            </Section>
          </>
        )}
        <Section title="Objetivo">
          <ChipRow>
            <Chip selected={goalId === null} onClick={() => setGoalId(null)}>
              Sem objetivo
            </Chip>
            {goals.map((g) => (
              <Chip key={g.id} selected={goalId === g.id} onClick={() => setGoalId(g.id)}>
                {g.name}
              </Chip>
            ))}
          </ChipRow>
        </Section>
        <Section title="Produto">
          <ChipRow>
            {productKinds.map((p) => (
              <Chip key={p} selected={p === product} onClick={() => setProduct(p)}>
                {productLabels[p]}
              </Chip>
            ))}
          </ChipRow>
        </Section>
        <Section title="Rende">
          <ChipRow>
            {indexKinds.map((k) => (
              <Chip
                key={k}
                selected={k === kind}
                onClick={() => {
                  setKind(k)
                  setPercent(null)
                  setErrors((e) => ({ ...e, percent: undefined }))
                }}
              >
                {indexKindLabels[k]}
              </Chip>
            ))}
          </ChipRow>
          <div className="flex items-center gap-2">
            <Input
              value={percentText}
              onChange={(e) => setPercent(e.target.value)}
              inputMode="decimal"
              aria-label={field.label}
              className="w-28 tabular-nums"
            />
            <span className="text-sm text-muted-foreground">{field.suffix}</span>
          </div>
          {field.hint && <p className="text-xs text-muted-foreground">{field.hint}</p>}
          <FieldError message={errors.percent} />
        </Section>
        <Section title="Vencimento" aside="opcional">
          <Input type="date" value={maturity} onChange={(e) => setMaturity(e.target.value)} aria-label="Vencimento" className="w-44" />
          <FieldError message={errors.maturity} />
          {maturityWarning(product, date, maturity) && (
            <p className="text-xs text-muted-foreground">{maturityWarning(product, date, maturity)}</p>
          )}
        </Section>
        {!deposit && (
          <Section title="Saiu de uma conta?">
            <ChipRow>
              <Chip selected={!funded} onClick={() => setFunded(false)}>
                Não, só registrar
              </Chip>
              <Chip selected={funded} onClick={() => setFunded(true)}>
                Sim, saiu de uma conta
              </Chip>
            </ChipRow>
            {funded &&
              (investmentAccounts.length === 0 ? (
                <p className="text-sm text-muted-foreground">Crie antes uma conta do tipo Investimento em Contas, para receber o aporte.</p>
              ) : (
                <>
                  <p className="text-xs text-muted-foreground">Saiu de</p>
                  <ChipRow>
                    {sourceAccounts.map((a) => (
                      <Chip key={a.id} selected={a.id === from} onClick={() => setFromId(a.id)}>
                        <AccountTile name={a.name} type={a.type} size="sm" />
                        {a.name}
                      </Chip>
                    ))}
                  </ChipRow>
                  <p className="text-xs text-muted-foreground">Entrou em</p>
                  <ChipRow>
                    {investmentAccounts.map((a) => (
                      <Chip key={a.id} selected={a.id === to} onClick={() => setToId(a.id)}>
                        <AccountTile name={a.name} type={a.type} size="sm" />
                        {a.name}
                      </Chip>
                    ))}
                  </ChipRow>
                </>
              ))}
            <FieldError message={errors.funding} />
          </Section>
        )}
        <FieldError message={errors.root} />
      </div>
      <SheetFooterBar className="flex gap-2">
        {deposit && (
          <Button
            type="button"
            variant="ghost"
            size="lg"
            className="h-12 rounded-2xl"
            disabled={remove.isPending}
            onClick={() => (locked ? setAskingDelete(true) : deleteDeposit())}
          >
            Excluir
          </Button>
        )}
        <Button type="submit" size="lg" disabled={pending} className="h-12 flex-1 rounded-2xl text-base">
          {pending ? 'Salvando…' : deposit ? 'Salvar' : 'Lançar aporte'}
        </Button>
      </SheetFooterBar>
    </form>
  )
}
