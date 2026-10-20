import { useState, type FormEvent } from 'react'
import { toast } from 'sonner'
import { AccountTile } from '@/components/brand/Tiles'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { FieldError } from '@/components/FieldError'
import { Button } from '@/components/ui/button'
import { SheetTitle } from '@/components/ui/sheet'
import { useAccounts } from '@/features/accounts/queries'
import { AmountField, Chip, ChipRow, DateChooser, Section } from '@/features/transactions/fields'
import { formatShortDate, todayInSaoPaulo } from '@/lib/dates'
import { formatCents } from '@/lib/money'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { depositTitle, previewRows, redemptionDestination, usableForRedemption, type Deposit } from './fixedIncome'
import { useCreateRedemption, useDeleteRedemption, useRedemptionPreview } from './fixedIncomeQueries'

// Resgatar um aporte, no todo ou em parte (docs/renda-fixa-resgate.md, 6). A pessoa digita o valor bruto, como no banco; a prévia
// (imposto, líquido) vem da API, que roda a mesma conta da gravação sem gravar. O líquido do extrato é opcional e, se vier, vale.
export function RedemptionSheet({ deposit, onClose }: { deposit: Deposit | null; onClose: () => void }) {
  return (
    <BottomSheet open={deposit !== null} onClose={onClose}>
      {deposit && <RedemptionForm key={deposit.id} deposit={deposit} onDone={onClose} />}
    </BottomSheet>
  )
}

function RedemptionForm({ deposit, onDone }: { deposit: Deposit; onDone: () => void }) {
  const accounts = useAccounts().data ?? []
  const create = useCreateRedemption()
  const remove = useDeleteRedemption()

  const [requestId] = useState(() => crypto.randomUUID())
  // Parte do último dia em que o resgate é possível (no CDI, o dia seguinte à última taxa), não de um "hoje" que ainda não vale. A API
  // diz qual é na primeira prévia; até lá (e se a pessoa escolher outra data) vale o que ela escolheu.
  const today = todayInSaoPaulo()
  const [picked, setPicked] = useState<string | null>(null)
  const [latest, setLatest] = useState<string | null>(null)
  const date = picked ?? (latest !== null && latest < today ? latest : today)
  const [grossCents, setGrossCents] = useState(0)
  const [informNet, setInformNet] = useState(false)
  const [netCents, setNetCents] = useState(0)
  const [pickedAccount, setPickedAccount] = useState<string | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)

  // Só aporte que saiu de uma conta pelo app oferece a conta: é ele que diz de qual conta de investimento o principal volta.
  const canFund = deposit.funding != null
  const destinations = accounts.filter(usableForRedemption)
  const suggested = redemptionDestination(deposit.funding, accounts)
  const destination = pickedAccount === undefined ? suggested : pickedAccount

  const wanted = useDebouncedValue(
    { date, grossCents: grossCents > 0 ? grossCents : null, netCents: informNet && netCents > 0 ? netCents : null },
    250,
  )
  const preview = useRedemptionPreview(deposit.id, wanted)
  const info = preview.data
  if (info?.latestDate && info.latestDate !== latest) setLatest(info.latestDate)
  const balance = info?.balanceCents ?? null
  const redemption = info?.redemption ?? null
  // O que a pessoa está digitando ainda não chegou à prévia: não mostra o resultado de um valor antigo.
  const typing = wanted.date !== date || wanted.grossCents !== (grossCents > 0 ? grossCents : null)
  // Data que não serve (sem saldo calculável) avisa sempre; valor que não serve, só depois de a pessoa terminar de digitar.
  const dateProblem = balance === null ? (info?.problem ?? null) : null
  const valueProblem = grossCents > 0 && !typing ? (info?.problem ?? null) : null
  const problem = dateProblem ?? valueProblem
  const canSubmit = redemption !== null && !typing && !create.isPending

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    if (grossCents <= 0) return setError('Digite o valor do resgate.')
    if (!redemption) return setError(info?.problem ?? 'Confira o valor e a data do resgate.')

    try {
      const saved = await create.mutateAsync({
        depositId: deposit.id,
        body: {
          clientRequestId: requestId,
          redemptionDate: date,
          grossCents,
          netCents: informNet && netCents > 0 ? netCents : null,
          funding: canFund && destination ? { toAccountId: destination } : null,
        },
      })
      toast(`Resgate de ${formatCents(saved.grossCents)} registrado`, {
        description: `Líquido ${formatCents(saved.netCents)} · ${formatShortDate(saved.redemptionDate)}`,
        duration: 8000,
        // Desfazer é excluir o resgate que acabou de ser feito, com os lançamentos que ele criou.
        action: {
          label: 'Desfazer',
          onClick: () => remove.mutate({ depositId: deposit.id, id: saved.id, transfer: saved.funding ? 'delete' : undefined }),
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
        <SheetTitle className="font-display text-2xl font-normal">Resgatar</SheetTitle>
        <p className="text-sm text-muted-foreground">
          {depositTitle(deposit.productKind, deposit.indexKind, deposit.rateE4)} · aportado em{' '}
          {formatShortDate(deposit.depositDate)}
        </p>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-1 pb-6">
        <Section title="Data do resgate">
          <DateChooser value={date} onChange={setPicked} />
        </Section>

        <div className="flex flex-col gap-2">
          <AmountField
            value={grossCents}
            onChange={setGrossCents}
            income={false}
            label="Valor bruto resgatado"
            compact
            autoFocus
          />
          <div className="flex items-center justify-between gap-3 text-sm text-muted-foreground">
            <span>{balance !== null ? `Saldo bruto em ${formatShortDate(wanted.date)}: ${formatCents(balance)}` : ' '}</span>
            {balance !== null && balance > 0 && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="-mr-2 rounded-full"
                onClick={() => setGrossCents(balance)}
              >
                Resgatar tudo
              </Button>
            )}
          </div>
          <FieldError message={problem ?? undefined} />
          {info?.problemCode === 'RatesUnavailable' && (
            <p className="text-xs text-muted-foreground">As taxas saem à noite. Escolha o último dia útil para resgatar.</p>
          )}
        </div>

        {redemption && !typing && (
          <section className="surface flex flex-col gap-2 rounded-2xl p-4" aria-label="Prévia do resgate">
            {previewRows(redemption).map((row, index, rows) => (
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
              {redemption.taxSource === 'Informed'
                ? 'Valores pelo líquido do extrato.'
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
                    ? 'O principal volta como transferência da conta de investimento, e o rendimento líquido entra como receita.'
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
