import { useState, type FormEvent } from 'react'
import { toast } from 'sonner'
import { AccountTile } from '@/components/brand/Tiles'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { FieldError } from '@/components/FieldError'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { SheetTitle } from '@/components/ui/sheet'
import { useAccounts } from '@/features/accounts/queries'
import { AmountField, Chip, ChipRow, DateChooser, Section } from '@/features/transactions/fields'
import { formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { cn } from '@/lib/utils'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { depositTitle, usableForRedemption, type Deposit } from './fixedIncome'
import { useCreateRedemptionBatch, useRedemptionBatchPreview, useUndoRedemptionBatch } from './fixedIncomeQueries'
import {
  batchCountLabel,
  batchDestination,
  batchHasEntries,
  batchHasFunding,
  batchNearlyAll,
  batchTotalRows,
  lineIsToggleable,
  lineNote,
  toggleId,
  undoItems,
} from './redemptionBatch'

// Resgatar vários aportes de uma vez (docs/renda-fixa-resgate.md, 15): o valor bruto do extrato, dividido do aporte mais antigo ao mais
// novo. A divisão e o imposto de cada um vêm da API (a prévia); cada aporte mantém o seu resgate. Desmarcar um aporte na prévia
// redistribui o valor entre os outros. `deposits` são os aportes escolhidos na lista, na ordem em que a lista os mostra.
export function BatchRedemptionSheet({
  deposits,
  onClose,
  onDone,
}: {
  deposits: Deposit[] | null
  onClose: () => void
  onDone: () => void
}) {
  return (
    <BottomSheet open={deposits !== null} onClose={onClose}>
      {deposits && <BatchForm key={deposits.map((d) => d.id).join()} deposits={deposits} onDone={onDone} />}
    </BottomSheet>
  )
}

function BatchForm({ deposits: chosenByList, onDone }: { deposits: Deposit[]; onDone: () => void }) {
  // Na ordem em que o valor sai: do aporte mais antigo ao mais novo (a lista da tela mostra o mais novo primeiro).
  const deposits = [...chosenByList].sort((a, b) => a.depositDate.localeCompare(b.depositDate))
  const accounts = useAccounts().data ?? []
  const create = useCreateRedemptionBatch()
  const undo = useUndoRedemptionBatch()

  const [requestId] = useState(() => crypto.randomUUID())
  // Parte do último dia em que o resgate é possível (no CDI, o dia seguinte à última taxa); a API diz qual é na primeira prévia.
  const today = todayInSaoPaulo()
  const [picked, setPicked] = useState<string | null>(null)
  const [latest, setLatest] = useState<string | null>(null)
  const date = picked ?? (latest !== null && latest < today ? latest : today)

  const [included, setIncluded] = useState(() => deposits.map((d) => d.id))
  // "Resgatar tudo" é um modo, não uma cópia do valor (como no resgate de um aporte só): o valor acompanha o saldo dos aportes marcados.
  const [all, setAll] = useState(false)
  const [typedCents, setTypedCents] = useState(0)
  const [informNet, setInformNet] = useState(false)
  const [netCents, setNetCents] = useState(0)
  const [pickedAccount, setPickedAccount] = useState<string | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)

  const chosen = deposits.filter((d) => included.includes(d.id))
  const canFund = batchHasFunding(chosen)
  const destinations = accounts.filter(usableForRedemption)
  const suggested = batchDestination(chosen, accounts)
  const destination = pickedAccount === undefined ? suggested : pickedAccount

  const current = {
    depositIds: chosen.map((d) => d.id),
    date,
    all,
    grossCents: !all && typedCents > 0 ? typedCents : null,
    netCents: informNet && netCents > 0 ? netCents : null,
  }
  const wanted = useDebouncedValue(current, 250)
  const preview = useRedemptionBatchPreview(wanted)
  const info = chosen.length > 0 ? preview.data : undefined
  if (info?.latestDate && info.latestDate !== latest) setLatest(info.latestDate)

  const totalBalance = info?.totalBalanceCents ?? null
  const totals = info?.totals ?? null
  const lineOf = (id: string) => info?.lines.find((l) => l.depositId === id)
  const grossCents = all ? (totalBalance ?? 0) : typedCents
  // O que a pessoa está digitando ainda não chegou à prévia: não mostra o resultado de um valor antigo.
  const typing = JSON.stringify(current) !== JSON.stringify(wanted)
  const problem = !typing && (all || typedCents > 0 || info?.problemCode) ? (info?.problem ?? null) : null
  const canSubmit = totals !== null && !typing && !create.isPending && chosen.length > 0

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    if (chosen.length === 0) return setError('Escolha ao menos um aporte.')
    if (!all && typedCents <= 0) return setError('Digite o valor do resgate.')
    if (!totals) return setError(info?.problem ?? 'Confira o valor e a data do resgate.')

    try {
      const saved = await create.mutateAsync({
        clientRequestId: requestId,
        depositIds: current.depositIds,
        redemptionDate: date,
        all,
        grossCents: current.grossCents,
        netCents: current.netCents,
        funding: canFund && destination ? { toAccountId: destination } : null,
      })
      toast(`Resgate de ${formatCents(saved.totals.grossCents)} em ${batchCountLabel(saved.redemptions.length)} registrado`, {
        description: `Líquido ${formatCents(saved.totals.netCents)} · ${formatShortDate(date)}`,
        duration: 8000,
        // Desfazer é excluir os resgates que acabaram de ser feitos, com os lançamentos que eles criaram.
        action: {
          label: 'Desfazer',
          onClick: () =>
            undo.mutate(
              { items: undoItems(saved), transfer: batchHasEntries(saved) ? 'delete' : null },
              { onError: (e) => toast.error((e as Error).message) },
            ),
        },
      })
      onDone()
    } catch (e) {
      setError((e as Error).message)
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">Resgatar {batchCountLabel(deposits.length)}</SheetTitle>
        <p className="text-sm text-muted-foreground">O valor sai do aporte mais antigo para o mais novo.</p>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-1 pb-6">
        <Section title="Data do resgate">
          <DateChooser value={date} onChange={setPicked} />
        </Section>

        <div className="flex flex-col gap-2">
          <AmountField
            value={grossCents}
            onChange={(cents) => {
              setAll(false)
              setTypedCents(cents)
            }}
            income={false}
            label="Valor bruto resgatado"
            compact
            autoFocus
          />
          <div className="flex items-center justify-between gap-3 text-sm text-muted-foreground">
            <span>{totalBalance !== null ? `Saldo bruto dos aportes: ${formatCents(totalBalance)}` : ' '}</span>
            {totalBalance !== null && totalBalance > 0 && !all && (
              <Button type="button" variant="ghost" size="sm" className="-mr-2 rounded-full" onClick={() => setAll(true)}>
                {batchNearlyAll(typedCents, totalBalance) ? `Resgatar tudo (${formatCents(totalBalance)})` : 'Resgatar tudo'}
              </Button>
            )}
          </div>
          <FieldError message={problem ?? undefined} />
          {info?.problemCode === 'RatesUnavailable' && (
            <p className="text-xs text-muted-foreground">As taxas saem à noite. Escolha o último dia útil para resgatar.</p>
          )}
        </div>

        <Section title="Aportes" aside={`${chosen.length} de ${deposits.length}`}>
          <ul className="surface divide-y divide-border/60 rounded-2xl" aria-label="Aportes do resgate">
            {deposits.map((d) => {
              const checked = included.includes(d.id)
              const line = checked ? lineOf(d.id) : undefined
              const toggleable = !line || lineIsToggleable(line)
              return (
                <li key={d.id}>
                  <label className={cn('flex items-start gap-3 px-4 py-3', toggleable ? 'cursor-pointer' : 'opacity-60')}>
                    <Checkbox
                      className="mt-0.5"
                      checked={checked}
                      disabled={!toggleable}
                      onCheckedChange={() => setIncluded((ids) => toggleId(ids, d.id))}
                      aria-label={`Aporte de ${formatShortDate(d.depositDate)}`}
                    />
                    <span className="min-w-0 flex-1">
                      <span className={cn('block font-medium tabular-nums', !checked && 'text-muted-foreground')}>
                        {formatCents(d.amountCents)}
                      </span>
                      <span className="block text-sm text-muted-foreground">
                        {formatShortDate(d.depositDate)} · {depositTitle(d.productKind, d.indexKind, d.rateE4)}
                      </span>
                      <span className="block text-xs text-muted-foreground">
                        {!checked
                          ? 'Fora do resgate.'
                          : line && !typing
                            ? totals
                              ? lineNote(line)
                              : line.skipped
                                ? lineNote(line)
                                : `Saldo ${formatCents(line.balanceCents ?? 0)}`
                            : ' '}
                      </span>
                    </span>
                    <span className="shrink-0 text-sm font-medium tabular-nums">
                      {checked && !typing && line?.redemption ? formatCents(line.redemption.grossCents) : ''}
                    </span>
                  </label>
                </li>
              )
            })}
          </ul>
        </Section>

        {totals && !typing && (
          <section className="surface flex flex-col gap-2 rounded-2xl p-4" aria-label="Prévia do resgate">
            {batchTotalRows(totals, informNet && netCents > 0).map((row, index, rows) => (
              <p
                key={row.label}
                className={
                  index === rows.length - 1
                    ? 'flex items-baseline justify-between gap-3 border-t border-border/60 pt-2 font-medium'
                    : 'flex items-baseline justify-between gap-3 text-sm text-muted-foreground'
                }
              >
                <span>{row.label}</span>
                <span className="tabular-nums">{formatCents(row.cents)}</span>
              </p>
            ))}
            <p className="text-xs text-muted-foreground">
              {informNet && netCents > 0
                ? 'Imposto repartido entre os aportes na proporção do estimado.'
                : 'Estimativa; o extrato do banco manda. Se o banco pagou outro líquido, informe abaixo.'}
            </p>
          </section>
        )}

        <Section title="Líquido do extrato" aside="opcional">
          <ChipRow>
            <Chip selected={!informNet} onClick={() => setInformNet(false)}>
              Usar a estimativa
            </Chip>
            <Chip selected={informNet} onClick={() => setInformNet(true)}>
              Informar o líquido
            </Chip>
          </ChipRow>
          {informNet && (
            <AmountField value={netCents} onChange={setNetCents} income={false} label="Líquido pago pelo banco" compact />
          )}
        </Section>

        {canFund && (
          <Section title="Cai em qual conta?">
            {destinations.length === 0 ? (
              <p className="text-sm text-muted-foreground">Não há conta corrente ou carteira ativa para receber o dinheiro.</p>
            ) : (
              <>
                <ChipRow>
                  <Chip selected={destination === null} onClick={() => setPickedAccount(null)}>
                    Só registrar
                  </Chip>
                  {destinations.map((a) => (
                    <Chip key={a.id} selected={a.id === destination} onClick={() => setPickedAccount(a.id)}>
                      <AccountTile name={a.name} type={a.type} size="sm" />
                      {a.name}
                    </Chip>
                  ))}
                </ChipRow>
                <p className="text-xs text-muted-foreground">
                  {destination
                    ? 'O principal volta como transferência da conta de investimento, e o rendimento líquido entra como receita. Aporte que não saiu de uma conta pelo app fica só registrado.'
                    : 'Nada é lançado nas contas: o resgate fica só registrado aqui.'}
                </p>
              </>
            )}
          </Section>
        )}
        <FieldError message={error ?? undefined} />
      </div>
      <SheetFooterBar>
        <Button type="submit" size="lg" disabled={!canSubmit} className="h-12 w-full rounded-2xl text-base">
          {create.isPending ? 'Registrando…' : 'Registrar resgate'}
        </Button>
      </SheetFooterBar>
    </form>
  )
}
