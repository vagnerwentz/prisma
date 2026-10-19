import { useState, type FormEvent } from 'react'
import { toast } from 'sonner'
import { BottomSheet, SheetFooterBar } from '@/components/BottomSheet'
import { FieldError } from '@/components/FieldError'
import { MoneyInput } from '@/components/MoneyInput'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { SheetTitle } from '@/components/ui/sheet'
import { Section } from '@/features/transactions/fields'
import { ApiError } from '@/lib/api'
import { defaultIndexPercentE4, formatPercent, parsePercent, percentFromE4, percentToE4, type Goal } from './fixedIncome'
import { useSaveGoal } from './fixedIncomeQueries'

// Novo objetivo ou edição (docs/renda-fixa.md, 7.2): nome, descrição, meta e o percentual do CDI que os aportes dele
// já trazem preenchido.
export type GoalView = { kind: 'new' } | { kind: 'edit'; goal: Goal }

export function GoalSheet({ view, onClose, onSaved }: { view: GoalView | null; onClose: () => void; onSaved?: (id: string) => void }) {
  return (
    <BottomSheet open={view !== null} onClose={onClose}>
      {view && <GoalForm key={view.kind === 'edit' ? view.goal.id : 'new'} goal={view.kind === 'edit' ? view.goal : undefined} onDone={onClose} onSaved={onSaved} />}
    </BottomSheet>
  )
}

type Errors = Partial<Record<'name' | 'target' | 'percent' | 'root', string>>

function GoalForm({ goal, onDone, onSaved }: { goal?: Goal; onDone: () => void; onSaved?: (id: string) => void }) {
  const save = useSaveGoal()
  const [name, setName] = useState(goal?.name ?? '')
  const [description, setDescription] = useState(goal?.description ?? '')
  const [targetCents, setTargetCents] = useState(goal?.targetCents ?? 0)
  const [percent, setPercent] = useState(formatPercent(percentFromE4(goal?.defaultIndexPercentE4 ?? defaultIndexPercentE4)).replace('%', ''))
  const [errors, setErrors] = useState<Errors>({})

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const parsed = parsePercent(percent)
    const found: Errors = {
      name: name.trim() ? undefined : 'Dê um nome ao objetivo.',
      percent: parsed === null ? 'Use um percentual entre 0,01% e 500%.' : undefined,
    }
    setErrors(found)
    if (found.name || parsed === null) return

    try {
      const saved = await save.mutateAsync({
        id: goal?.id,
        body: {
          name: name.trim(),
          description: description.trim() || null,
          targetCents: targetCents > 0 ? targetCents : null,
          defaultIndexPercentE4: percentToE4(parsed),
        },
      })
      toast(goal ? 'Objetivo salvo' : `Objetivo "${saved.name}" criado`)
      onSaved?.(saved.id)
      onDone()
    } catch (error) {
      if (error instanceof ApiError && error.code === 'GoalNameTaken') setErrors({ name: 'Você já tem um objetivo com esse nome.' })
      else setErrors({ root: (error as Error).message })
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex min-h-0 flex-col">
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">{goal ? 'Editar objetivo' : 'Novo objetivo'}</SheetTitle>
      </header>
      <div className="flex min-h-0 flex-col gap-6 overflow-y-auto overscroll-contain px-4 pt-1 pb-6">
        <Section title="Nome">
          <Input value={name} onChange={(e) => setName(e.target.value)} maxLength={80} placeholder="Reserva de emergência" autoFocus={!goal} />
          <FieldError message={errors.name} />
        </Section>
        <Section title="Meta" aside="opcional">
          <MoneyInput value={targetCents} onChange={setTargetCents} aria-label="Meta" />
        </Section>
        <Section title="Rende por padrão">
          <div className="flex items-center gap-2">
            <Input
              value={percent}
              onChange={(e) => setPercent(e.target.value)}
              inputMode="decimal"
              aria-label="Percentual do CDI"
              className="w-28 tabular-nums"
            />
            <span className="text-sm text-muted-foreground">% do CDI (pós-fixado)</span>
          </div>
          <FieldError message={errors.percent} />
        </Section>
        <Section title="Descrição" aside="opcional">
          <Input value={description} onChange={(e) => setDescription(e.target.value)} maxLength={300} />
        </Section>
        <FieldError message={errors.root} />
      </div>
      <SheetFooterBar>
        <Button type="submit" size="lg" disabled={save.isPending} className="h-12 w-full rounded-2xl text-base">
          {save.isPending ? 'Salvando…' : goal ? 'Salvar' : 'Criar objetivo'}
        </Button>
      </SheetFooterBar>
    </form>
  )
}
