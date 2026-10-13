import { CreditCard } from 'lucide-react'
import { useState, type CSSProperties } from 'react'
import { cn } from '@/lib/utils'
import { dayAndMonth, daysBetween, monthOfDate, statementForPurchase } from './demoMath'
import { Eyebrow } from './parts'

// "Em qual fatura cai?" (docs/landing.md, D7): a pessoa arrasta o dia da compra e vê a fatura e o dia em que o dinheiro
// sai, pela mesma regra do app (StatementCalculator). Um cartão de exemplo, que fecha dia 25 e vence dia 5.
const ClosingDay = 25
const DueDay = 5
const Year = 2026
const Month = 10
const DaysInMonth = 31

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

export function StatementDemo() {
  const [day, setDay] = useState(26)
  const purchase = `${Year}-${Month}-${String(day).padStart(2, '0')}`
  const { dueDate } = statementForPurchase(purchase, ClosingDay, DueDay)
  const wait = daysBetween(purchase, dueDate)
  const afterClosing = day > ClosingDay
  const fraction = (value: number) => (value - 1) / (DaysInMonth - 1)
  // O polegar (1,75rem) anda de borda a borda: o centro dele vai de 0,875rem até a largura menos 0,875rem.
  const thumbCenter = (value: number) => `calc(0.875rem + ${fraction(value)} * (100% - 1.75rem))`

  return (
    <section id="fatura" className="mx-auto grid max-w-6xl scroll-mt-8 items-center gap-10 px-5 py-16 sm:px-8 sm:py-24 lg:grid-cols-2 lg:gap-16">
      <div className="flex max-w-xl flex-col gap-4">
        <Eyebrow band="#8b5cf6">Cartão de crédito</Eyebrow>
        <h2 className="feature-title">O mês que você vê é o mês que você paga.</h2>
        <p className="lead">
          A data da compra decide a fatura. A fatura decide quando o dinheiro sai. O Prisma Money faz essa conta em cada compra,
          e o seu mês mostra o que sai da conta de verdade.
        </p>
        <p className="lead">Arraste o dia da compra e veja.</p>
      </div>

      <div className="surface flex flex-col gap-6 rounded-3xl p-6 sm:p-8">
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <CreditCard aria-hidden className="size-4" />
          Cartão que fecha dia {ClosingDay} e vence dia {DueDay}
        </p>

        <div className="flex flex-col gap-1">
          <span className="text-sm text-muted-foreground">Compra em</span>
          <span className="font-display text-4xl leading-none sm:text-5xl">{dayAndMonth(purchase)}</span>
        </div>

        <div className="day-slider" style={{ '--fill': thumbCenter(day) } as CSSProperties}>
          <input
            type="range"
            min={1}
            max={DaysInMonth}
            value={day}
            onChange={(e) => setDay(Number(e.target.value))}
            aria-label="Dia da compra em outubro"
            aria-valuetext={dayAndMonth(purchase)}
          />
          <span className="closing-mark" style={{ left: thumbCenter(ClosingDay) }} aria-hidden>
            fecha {ClosingDay}
          </span>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1 rounded-2xl bg-muted/60 p-4">
            <span className="text-sm text-muted-foreground">Entra na fatura de</span>
            <span key={dueDate} className={cn('swap-in text-xl font-semibold sm:text-2xl', afterClosing && 'text-spectrum')}>
              {capitalize(monthOfDate(dueDate))}
            </span>
          </div>
          <div className="flex flex-col gap-1 rounded-2xl bg-muted/60 p-4">
            <span className="text-sm text-muted-foreground">O dinheiro sai em</span>
            <span key={dueDate} className="swap-in text-xl font-semibold sm:text-2xl">
              {dayAndMonth(dueDate)}
            </span>
          </div>
        </div>

        <p className="text-sm text-muted-foreground" aria-live="polite">
          {afterClosing
            ? `Passou do fechamento: vai para a fatura seguinte, e você só paga ${wait} dias depois da compra.`
            : `Até o dia do fechamento: entra na fatura que fecha dia ${ClosingDay} e sai ${wait} dias depois da compra.`}
        </p>
      </div>
    </section>
  )
}
