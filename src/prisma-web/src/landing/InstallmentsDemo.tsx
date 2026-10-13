import { Check, Minus, Plus } from 'lucide-react'
import { useState } from 'react'
import { formatCents, parseCentsInput, splitCents } from '@/lib/money'
import { Eyebrow } from './parts'

// Parcelas ao vivo (docs/landing.md, D7): a pessoa digita o valor e o número de parcelas, e vê a divisão pela mesma
// regra do app (splitCents, igual a Money.SplitInto): o resto vai para as primeiras, e a soma é sempre o total.
const MaxInstallments = 24
const Shown = 12

export function InstallmentsDemo() {
  const [totalCents, setTotalCents] = useState(10000)
  const [parts, setParts] = useState(3)
  const split = splitCents(totalCents, parts)
  const sum = split.reduce((a, b) => a + b, 0)
  // Acima de 12 parcelas, as últimas viram um resumo. Elas podem ter um centavo de diferença entre si.
  const visible = parts > Shown ? split.slice(0, Shown - 1) : split
  const rest = split.slice(visible.length)
  const restValues = [...new Set(rest)].map(formatCents).join(' ou ')

  return (
    <section id="parcelas" className="mx-auto grid max-w-6xl scroll-mt-8 items-center gap-10 px-5 py-16 sm:px-8 sm:py-24 lg:grid-cols-2 lg:gap-16">
      <div className="flex max-w-xl flex-col gap-4 lg:order-2">
        <Eyebrow band="#3b82f6">Parcelas</Eyebrow>
        <h2 className="feature-title">Parcelou? Nenhum centavo some no caminho.</h2>
        <p className="lead">
          R$&nbsp;100 em 3x não é 33,33 três vezes, que dá 99,99. O Prisma Money põe o centavo que sobra na primeira parcela, e a
          soma sempre fecha.
        </p>
        <p className="lead">
          Cada parcela vai para a fatura do mês em que cai, e você vê, mês a mês, quanto das próximas faturas já está
          comprometido. Experimente com a sua compra.
        </p>
      </div>

      <div className="surface flex flex-col gap-6 rounded-3xl p-6 sm:p-8 lg:order-1">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <label className="flex min-w-0 flex-col gap-1">
            <span className="text-sm text-muted-foreground">Valor da compra</span>
            <input
              inputMode="numeric"
              value={formatCents(totalCents)}
              onChange={(e) => setTotalCents(parseCentsInput(e.target.value))}
              // Os dígitos entram pela direita: o cursor vai para o fim, como no app do banco.
              onFocus={(e) => {
                const input = e.currentTarget
                requestAnimationFrame(() => input.setSelectionRange(input.value.length, input.value.length))
              }}
              className="w-full max-w-[14rem] bg-transparent font-display text-4xl leading-none outline-none sm:text-5xl"
            />
          </label>
          <div className="flex flex-col gap-1">
            <span className="text-sm text-muted-foreground" id="parcelas-label">
              Parcelas
            </span>
            <div className="flex items-center gap-1" role="group" aria-labelledby="parcelas-label">
              <button type="button" className="stepper" aria-label="Menos uma parcela" onClick={() => setParts((p) => Math.max(1, p - 1))}>
                <Minus aria-hidden className="size-4" />
              </button>
              <span className="w-12 text-center text-xl font-semibold tabular-nums" aria-live="polite">
                {parts}x
              </span>
              <button
                type="button"
                className="stepper"
                aria-label="Mais uma parcela"
                onClick={() => setParts((p) => Math.min(MaxInstallments, p + 1))}
              >
                <Plus aria-hidden className="size-4" />
              </button>
            </div>
          </div>
        </div>

        <ol className="grid grid-cols-2 gap-2 sm:grid-cols-3">
          {visible.map((cents, i) => (
            <li key={i} className="installment-chip">
              <span className="text-xs text-muted-foreground">{i + 1}ª parcela</span>
              <span className="font-medium tabular-nums">{formatCents(cents)}</span>
            </li>
          ))}
          {rest.length > 0 && (
            <li className="installment-chip col-span-2 sm:col-span-1">
              <span className="text-xs text-muted-foreground">
                da {visible.length + 1}ª à {parts}ª
              </span>
              <span className="font-medium tabular-nums">{restValues}</span>
            </li>
          )}
        </ol>

        <p className="flex items-center gap-2 border-t border-border/60 pt-4 text-sm">
          <span className="grid size-5 place-items-center rounded-full bg-[image:var(--spectrum-cool)] text-white">
            <Check aria-hidden className="size-3" strokeWidth={3} />
          </span>
          <span>
            Soma das parcelas: <strong className="font-semibold tabular-nums">{formatCents(sum)}</strong>, o valor da compra.
          </span>
        </p>
      </div>
    </section>
  )
}
