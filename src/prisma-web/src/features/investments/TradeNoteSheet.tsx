import { Plus, X } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { toast } from 'sonner'
import { AccountTile, AssetTile } from '@/components/brand/Tiles'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { FieldError } from '@/components/FieldError'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { SheetTitle } from '@/components/ui/sheet'
import { CheckChip, Chip, ChipRow, DateChooser, Section } from '@/features/transactions/fields'
import { useAccountBalances, useAccounts } from '@/features/accounts/queries'
import { todayInSaoPaulo } from '@/lib/dates'
import { formatCents, parseCentsInput } from '@/lib/money'
import { AssetPicker } from './AssetPicker'
import { logoSrc, type AssetItem } from './assets'
import { useDeleteTradeNote, useRestoreTradeNote, useSaveTradeNote, useTradeNote } from './queries'
import {
  brokerageKindLabel,
  brokerageKindOptions,
  brokerageKindsError,
  brokerChoices,
  defaultBrokerageKinds,
  defaultSettlementAccount,
  draftErrors,
  goesNegative,
  noteTitle,
  noteTotals,
  parseQuantity,
  settlementAccountChoices,
  settlementLine,
  settlementNetCents,
  showsBrokerageKinds,
  sideLabels,
  type DraftErrors,
  type DraftLine,
  type TradeNote,
  type TradeSide,
} from './trades'

// O painel da nota de corretagem (docs/investimentos.md, I.7b2): lançar e editar. A pessoa copia a nota olhando
// para ela; cada operação entra por um mini-form dentro do próprio painel (decisão do dono).
export type TradeNoteView = { kind: 'new'; asset?: AssetItem } | { kind: 'edit'; id: string }

// A última corretora usada é preferência de tela, não dado financeiro: fica no navegador (como o tema).
const lastBrokerKey = 'prisma.lastBroker'

function readLastBroker(): string | null {
  try {
    return localStorage.getItem(lastBrokerKey)
  } catch {
    return null
  }
}

function rememberBroker(broker: string) {
  try {
    localStorage.setItem(lastBrokerKey, broker)
  } catch {
    // Sem armazenamento (aba anônima): a próxima nota só não vem com a corretora escolhida.
  }
}

// A última conta da corretora escolhida, ou 'none' se a pessoa escolheu não vincular: também preferência de tela.
const lastSettlementKey = 'prisma.lastSettlementAccount'

function readLastSettlement(): string | null {
  try {
    return localStorage.getItem(lastSettlementKey)
  } catch {
    return null
  }
}

function rememberSettlement(accountId: string | null) {
  try {
    localStorage.setItem(lastSettlementKey, accountId ?? 'none')
  } catch {
    // Sem armazenamento: a próxima nota só não vem com a conta escolhida.
  }
}

export function TradeNoteSheet({
  view,
  fallbackBroker,
  onClose,
}: {
  view: TradeNoteView | null
  // A corretora da nota mais recente, se o navegador não lembrar de nenhuma.
  fallbackBroker: string | null
  onClose: () => void
}) {
  return (
    <BottomSheet open={view !== null} onClose={onClose}>
      {view?.kind === 'new' && (
        <NoteForm key="new" asset={view.asset} lastBroker={readLastBroker() ?? fallbackBroker} onDone={onClose} />
      )}
      {view?.kind === 'edit' && <EditNote key={view.id} id={view.id} onDone={onClose} />}
    </BottomSheet>
  )
}

function EditNote({ id, onDone }: { id: string; onDone: () => void }) {
  const note = useTradeNote(id)
  if (note.isSuccess) return <NoteForm note={note.data} lastBroker={note.data.broker} onDone={onDone} />
  return (
    <header className="px-4 pt-2 pb-6">
      <SheetTitle className="font-display text-2xl font-normal">Nota</SheetTitle>
      {note.isError && <p className="text-sm text-muted-foreground">Esta nota não está mais aqui.</p>}
    </header>
  )
}

type Line = DraftLine & { asset: AssetItem }

type Errors = Partial<Record<'broker' | 'lines' | 'brokerage' | 'root', string>>

const emptyDraft = (asset?: AssetItem): DraftLine => ({ asset: asset ?? null, side: 'Buy', quantity: 0, unitPriceCents: 0 })

function NoteForm({
  note,
  asset: presetAsset,
  lastBroker,
  onDone,
}: {
  note?: TradeNote
  asset?: AssetItem
  lastBroker: string | null
  onDone: () => void
}) {
  const save = useSaveTradeNote()
  const remove = useDeleteTradeNote()
  const restore = useRestoreTradeNote()
  const accounts = useAccounts()
  const balances = useAccountBalances()

  const choices = brokerChoices(lastBroker)
  const [broker, setBroker] = useState<string | null>(note?.broker ?? lastBroker)
  const [typingBroker, setTypingBroker] = useState(false)
  const [date, setDate] = useState(note?.date ?? todayInSaoPaulo())
  const [lines, setLines] = useState<Line[]>(
    note?.trades.map((t) => ({
      asset: { id: t.assetId, symbol: t.symbol, name: t.assetName, kind: t.assetKind, isActive: true, hasLogo: t.assetHasLogo },
      side: t.side,
      quantity: t.quantity,
      unitPriceCents: t.unitPriceCents,
    })) ?? [],
  )
  // O mini-form: uma operação nova ('new') ou a de um índice da lista, sendo corrigida. A nota nova já abre nele.
  const [editing, setEditing] = useState<number | 'new' | null>(note ? null : 'new')
  const [feesCents, setFeesCents] = useState(note?.feesCents ?? 0)
  const [brokerageCents, setBrokerageCents] = useState(note?.brokerageFeeCents ?? 0)
  // Em quais tipos a corretagem foi cobrada (I.7b3). A nota guardada traz os dela; a nova começa só com ação.
  const [brokerageKinds, setBrokerageKinds] = useState(() =>
    note ? note.brokerageFeeKinds.flatMap((k) => (k ? [k] : [])) : defaultBrokerageKinds,
  )
  // A conta cujo caixa a nota mexe (I.8, docs/investimentos.md): opcional. A nota guardada traz a dela (nenhuma, se for de antes da
  // I.8); a nova começa na última escolhida ou na conta com o nome da corretora. `undefined` até as contas chegarem.
  const settlementChoices = settlementAccountChoices(accounts.data ?? [], note?.settlementAccountId ?? null)
  const [pickedAccount, setPickedAccount] = useState<string | null | undefined>(note ? (note.settlementAccountId ?? null) : undefined)
  const settlementAccountId =
    pickedAccount !== undefined ? pickedAccount : defaultSettlementAccount(settlementChoices, broker, readLastSettlement())
  const [errors, setErrors] = useState<Errors>({})

  const totals = noteTotals(lines)
  const kindOptions = brokerageKindOptions(lines.map((l) => l.asset.kind))
  const showKinds = showsBrokerageKinds(brokerageCents, kindOptions, brokerageKinds)
  const toggleKind = (kind: (typeof kindOptions)[number], checked: boolean) => {
    setBrokerageKinds((current) => (checked ? [...current, kind] : current.filter((k) => k !== kind)))
    setErrors((e) => ({ ...e, brokerage: undefined }))
  }
  const netCents = settlementNetCents(totals, feesCents + brokerageCents)
  const settlementAccount = settlementChoices.find((a) => a.id === settlementAccountId)
  // O que a nota já liquidava nesta mesma conta está no saldo atual: a conta, sem ela, é o que sobra antes desta gravação.
  const previousNet = note && note.settlementAccountId === settlementAccountId ? note.netCashCents : 0
  const negative = lines.length > 0 && goesNegative(balances.data?.get(settlementAccountId ?? '')?.balanceCents ?? null, previousNet, netCents)
  const costsByLine = note && editing === null && sameLines(note, lines) ? note.trades.map((t) => t.costsCents) : null

  const putLine = (line: Line) => {
    setLines((current) =>
      editing === 'new' || editing === null ? [...current, line] : current.map((l, i) => (i === editing ? line : l)),
    )
    setEditing(null)
    setErrors((e) => ({ ...e, lines: undefined }))
  }

  const removeLine = (index: number) => {
    setLines((current) => current.filter((_, i) => i !== index))
    setEditing(null)
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const found: Errors = {
      broker: broker?.trim() ? undefined : 'Escolha a corretora.',
      lines: lines.length > 0 ? undefined : 'Inclua ao menos uma operação.',
      brokerage: lines.length > 0 ? brokerageKindsError(brokerageCents, kindOptions, brokerageKinds) : undefined,
      root: editing !== null && lines.length > 0 ? 'Termine a operação em aberto: toque em Incluir ou Cancelar.' : undefined,
    }
    setErrors(found)
    if (found.broker || found.lines || found.brokerage || found.root || !broker) return

    try {
      const saved = await save.mutateAsync({
        id: note?.id,
        body: {
          broker: broker.trim(),
          date,
          feesCents,
          brokerageFeeCents: brokerageCents,
          // A marcação de antes fica de fora: a lista decide (I.7b3).
          brokerageFeeOnAllKinds: null,
          brokerageFeeKinds: brokerageKinds,
          settlementAccountId,
          trades: lines.map((l) => ({
            assetId: l.asset.id,
            side: l.side,
            quantity: l.quantity,
            unitPriceCents: l.unitPriceCents,
          })),
        },
      })
      rememberBroker(saved.broker)
      rememberSettlement(settlementAccountId)
      toast(note ? 'Nota salva' : 'Nota lançada', { description: noteTitle(saved.trades) })
      onDone()
    } catch (error) {
      setErrors({ root: (error as Error).message })
    }
  }

  const deleteNote = async () => {
    if (!note) return
    try {
      await remove.mutateAsync(note.id)
      toast('Nota excluída', {
        description: noteTitle(note.trades),
        duration: 8000,
        action: {
          label: 'Desfazer',
          onClick: () => restore.mutate(note.id, { onError: (error) => toast.error(error.message) }),
        },
      })
      onDone()
    } catch (error) {
      setErrors({ root: (error as Error).message })
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">{note ? 'Editar nota' : 'Nova nota'}</SheetTitle>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-1 pb-6">
        <Section title="Corretora">
          <ChipRow>
            {choices.map((b) => (
              <Chip
                key={b}
                selected={!typingBroker && b === broker}
                onClick={() => {
                  setTypingBroker(false)
                  setBroker(b)
                }}
              >
                <AccountTile name={b} type="Investment" size="sm" />
                {b}
              </Chip>
            ))}
            <Chip
              selected={typingBroker}
              onClick={() => {
                setTypingBroker(true)
                setBroker('')
              }}
            >
              Outra
            </Chip>
          </ChipRow>
          {typingBroker && (
            <Input
              autoFocus
              value={broker ?? ''}
              maxLength={60}
              onChange={(e) => setBroker(e.target.value)}
              placeholder="Nome da corretora"
              aria-label="Nome da corretora"
            />
          )}
          <FieldError message={errors.broker} />
        </Section>

        <Section title="Data do pregão">
          <DateChooser value={date} onChange={setDate} />
        </Section>

        {settlementChoices.length > 0 && (
          <Section title="Debitar da conta">
            <ChipRow>
              <Chip selected={settlementAccountId === null} onClick={() => setPickedAccount(null)}>
                Nenhuma
              </Chip>
              {settlementChoices.map((a) => (
                <Chip key={a.id} selected={a.id === settlementAccountId} onClick={() => setPickedAccount(a.id)}>
                  <AccountTile name={a.name} type={a.type} size="sm" />
                  {a.name}
                </Chip>
              ))}
            </ChipRow>
            <p className="text-xs text-pretty text-muted-foreground">
              {settlementAccount && lines.length > 0
                ? settlementLine(netCents, settlementAccount.name)
                : 'A compra sai do caixa da conta e a venda entra nele. Sem conta, o saldo não muda.'}
            </p>
            {negative && <p className="text-xs text-pretty text-foreground">A conta ficará negativa. Você pode salvar mesmo assim.</p>}
          </Section>
        )}

        <Section title="Operações">
          {lines.length > 0 && (
            <ul className="flex flex-col">
              {lines.map((line, index) =>
                editing === index ? (
                  <li key={index}>
                    <LineEditor
                      initial={line}
                      onSubmit={putLine}
                      onCancel={() => setEditing(null)}
                      onRemove={() => removeLine(index)}
                    />
                  </li>
                ) : (
                  <li key={index}>
                    <button
                      type="button"
                      onClick={() => setEditing(index)}
                      className="flex w-full items-center gap-3 rounded-xl px-2 py-2 text-left transition-colors hover:bg-muted/50 active:bg-muted/70"
                    >
                      <AssetTile symbol={line.asset.symbol} logo={logoSrc(line.asset)} size="sm" />
                      <span className="min-w-0 flex-1">
                        <span className="block text-sm font-medium">
                          {sideLabels[line.side]} · {line.asset.symbol}
                        </span>
                        <span className="block text-xs text-muted-foreground tabular-nums">
                          {line.quantity.toLocaleString('pt-BR')} × {formatCents(line.unitPriceCents)}
                          {costsByLine && costsByLine[index] > 0 && ` · custos ${formatCents(costsByLine[index])}`}
                        </span>
                      </span>
                      <span className="shrink-0 text-sm tabular-nums">{formatCents(line.quantity * line.unitPriceCents)}</span>
                    </button>
                  </li>
                ),
              )}
            </ul>
          )}
          {editing === 'new' ? (
            <LineEditor
              initial={emptyDraft(presetAsset)}
              onSubmit={putLine}
              onCancel={lines.length > 0 ? () => setEditing(null) : undefined}
            />
          ) : (
            editing === null && (
              <Button type="button" variant="secondary" className="self-start rounded-full" onClick={() => setEditing('new')}>
                <Plus />
                Adicionar operação
              </Button>
            )
          )}
          <FieldError message={errors.lines} />
        </Section>

        <Section title="Custos da nota">
          <div className="grid grid-cols-2 gap-3">
            <MoneyInput label="Taxas" value={feesCents} onChange={setFeesCents} />
            <MoneyInput label="Corretagem" value={brokerageCents} onChange={setBrokerageCents} />
          </div>
          <p className="text-xs text-muted-foreground">Taxas: liquidação, emolumentos, ISS e outros. O IRRF não entra.</p>
          {showKinds && (
            <fieldset className="flex flex-col gap-2 pt-1">
              <legend className="mb-2 text-xs text-muted-foreground">Corretagem cobrada em</legend>
              <div className="flex flex-wrap gap-2">
                {kindOptions.map((kind) => (
                  <CheckChip key={kind} checked={brokerageKinds.includes(kind)} onCheckedChange={(checked) => toggleKind(kind, checked)}>
                    {brokerageKindLabel(kind)}
                  </CheckChip>
                ))}
              </div>
            </fieldset>
          )}
          <FieldError message={errors.brokerage} />
        </Section>

        {lines.length > 0 && (
          <dl className="flex flex-col gap-1.5 rounded-2xl bg-muted/50 px-4 py-3 text-sm">
            {totals.buyCents > 0 && (
              <div className="flex justify-between gap-3">
                <dt className="text-muted-foreground">Compras</dt>
                <dd className="tabular-nums">{formatCents(totals.buyCents)}</dd>
              </div>
            )}
            {totals.sellCents > 0 && (
              <div className="flex justify-between gap-3">
                <dt className="text-muted-foreground">Vendas</dt>
                <dd className="tabular-nums">{formatCents(totals.sellCents)}</dd>
              </div>
            )}
            <div className="flex justify-between gap-3">
              <dt className="text-muted-foreground">Custos</dt>
              <dd className="tabular-nums">{formatCents(feesCents + brokerageCents)}</dd>
            </div>
          </dl>
        )}
        <FieldError message={errors.root} />
      </div>
      <SheetFooterBar className="flex gap-2">
        {note && (
          <Button
            type="button"
            variant="ghost"
            size="lg"
            className="h-12 rounded-2xl"
            disabled={remove.isPending}
            onClick={deleteNote}
          >
            Excluir
          </Button>
        )}
        <Button type="submit" size="lg" disabled={save.isPending} className="h-12 flex-1 rounded-2xl text-base">
          {save.isPending ? 'Salvando…' : note ? 'Salvar' : 'Lançar nota'}
        </Button>
      </SheetFooterBar>
    </form>
  )
}

// O custo de cada linha vem da API, depois de gravar: só vale enquanto as linhas são as gravadas.
function sameLines(note: TradeNote, lines: Line[]): boolean {
  return (
    note.trades.length === lines.length &&
    note.trades.every(
      (t, i) =>
        t.assetId === lines[i].asset.id &&
        t.side === lines[i].side &&
        t.quantity === lines[i].quantity &&
        t.unitPriceCents === lines[i].unitPriceCents,
    )
  )
}

// O mini-form de uma operação: ativo, compra ou venda, quantidade e preço.
function LineEditor({
  initial,
  onSubmit,
  onCancel,
  onRemove,
}: {
  initial: DraftLine
  onSubmit: (line: Line) => void
  onCancel?: () => void
  onRemove?: () => void
}) {
  const [draft, setDraft] = useState(initial)
  const [errors, setErrors] = useState<DraftErrors>({})

  const include = () => {
    const found = draftErrors(draft)
    setErrors(found)
    if (Object.keys(found).length > 0 || !draft.asset) return
    onSubmit({ ...draft, asset: draft.asset })
  }

  return (
    <div className="flex flex-col gap-3 rounded-2xl bg-muted/50 p-3">
      <div>
        <AssetPicker value={draft.asset} onChange={(asset) => setDraft((d) => ({ ...d, asset }))} autoFocus={!draft.asset} />
        <FieldError message={errors.asset} />
      </div>
      <div className="flex gap-2">
        {(['Buy', 'Sell'] as TradeSide[]).map((side) => (
          <Chip key={side} selected={draft.side === side} onClick={() => setDraft((d) => ({ ...d, side }))}>
            {sideLabels[side]}
          </Chip>
        ))}
      </div>
      <div className="grid grid-cols-2 gap-3">
        <label className="flex flex-col gap-1.5">
          <span className="text-xs font-medium text-muted-foreground">Quantidade</span>
          <Input
            inputMode="numeric"
            autoComplete="off"
            value={draft.quantity === 0 ? '' : draft.quantity.toLocaleString('pt-BR')}
            onChange={(e) => setDraft((d) => ({ ...d, quantity: parseQuantity(e.target.value) }))}
            aria-invalid={errors.quantity ? true : undefined}
            className="tabular-nums"
          />
          <FieldError message={errors.quantity} />
        </label>
        <MoneyInput
          label="Preço"
          value={draft.unitPriceCents}
          onChange={(unitPriceCents) => setDraft((d) => ({ ...d, unitPriceCents }))}
          error={errors.price}
        />
      </div>
      <div className="flex items-center gap-2">
        {onRemove && (
          <Button type="button" variant="ghost" size="sm" className="rounded-full text-muted-foreground" onClick={onRemove}>
            <X />
            Tirar
          </Button>
        )}
        <span className="flex-1" />
        {onCancel && (
          <Button type="button" variant="ghost" size="sm" className="rounded-full" onClick={onCancel}>
            Cancelar
          </Button>
        )}
        <Button type="button" size="sm" className="rounded-full" onClick={include}>
          Incluir
        </Button>
      </div>
    </div>
  )
}

// Valor em reais digitado como numa calculadora (os dígitos entram pelos centavos), num campo pequeno: dentro da
// nota, o destaque é o total, não cada taxa.
function MoneyInput({
  label,
  value,
  onChange,
  error,
}: {
  label: string
  value: number
  onChange: (cents: number) => void
  error?: string
}) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-medium text-muted-foreground">{label}</span>
      <Input
        inputMode="numeric"
        autoComplete="off"
        value={formatCents(value)}
        onChange={(e) => onChange(parseCentsInput(e.target.value))}
        aria-invalid={error ? true : undefined}
        className="tabular-nums"
      />
      <FieldError message={error} />
    </label>
  )
}
