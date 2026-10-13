// Contas das demonstrações da página de apresentação (docs/landing.md). São as mesmas regras do app, refeitas aqui
// porque a página roda sem a API: a fatura de StatementCalculator, o rateio de Money.Allocate e o preço médio de
// AssetPosition. Datas como "aaaa-mm-dd", sem passar por Date com fuso (CLAUDE.md, regra 3).

const cycleOf = (year: number, month: number) => year * 12 + (month - 1)

const pad = (n: number) => String(n).padStart(2, '0')

// Dia além do fim do mês vira o último dia do mês.
function dayInMonth(cycle: number, day: number): string {
  const year = Math.floor(cycle / 12)
  const month = (cycle % 12) + 1
  const lastDay = new Date(Date.UTC(year, month, 0)).getUTCDate()
  return `${year}-${pad(month)}-${pad(Math.min(day, lastDay))}`
}

function statementOfCycle(cycle: number, closingDay: number, dueDay: number) {
  // Vencimento igual ou anterior ao fechamento cai no mês seguinte.
  const dueCycle = dueDay > closingDay ? cycle : cycle + 1
  return { closingDate: dayInMonth(cycle, closingDay), dueDate: dayInMonth(dueCycle, dueDay) }
}

// A compra entra na primeira fatura que fecha na data dela ou depois.
export function statementForPurchase(purchaseDate: string, closingDay: number, dueDay: number) {
  const [year, month] = purchaseDate.split('-').map(Number)
  let cycle = cycleOf(year, month) - 1
  while (statementOfCycle(cycle, closingDay, dueDay).closingDate < purchaseDate) cycle++
  return statementOfCycle(cycle, closingDay, dueDay)
}

const toUtcDay = (date: string) => {
  const [year, month, day] = date.split('-').map(Number)
  return Date.UTC(year, month - 1, day) / 86_400_000
}

export const daysBetween = (from: string, to: string) => toUtcDay(to) - toUtcDay(from)

// Rateio por peso: cada parte leva o piso da sua fatia, e os centavos que sobram vão, um por vez, para os maiores
// restos (empate: a ordem das partes). A soma é sempre o total.
export function allocateCents(totalCents: number, weights: number[]): number[] {
  const totalWeight = weights.reduce((a, b) => a + b, 0)
  if (totalWeight <= 0) throw new RangeError('Ao menos um peso precisa ser maior que zero.')

  const exact = weights.map((w) => BigInt(totalCents) * BigInt(w))
  const parts = exact.map((e) => Number(e / BigInt(totalWeight)))
  const remainders = exact.map((e) => e % BigInt(totalWeight))
  const leftover = totalCents - parts.reduce((a, b) => a + b, 0)

  const order = weights.map((_, i) => i).sort((a, b) => (remainders[b] > remainders[a] ? 1 : remainders[b] < remainders[a] ? -1 : a - b))
  for (const i of order.slice(0, leftover)) parts[i]++
  return parts
}

// Preço médio com custos: custo de aquisição ÷ quantidade, arredondado para cima a partir da metade.
export const averageCostCents = (costCents: number, quantity: number) => Math.floor((2 * costCents + quantity) / (2 * quantity))

const monthNames = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']

// "2026-12-05" → "5 de dezembro". Sem Intl, para o HTML do build e o do navegador saírem iguais.
export function dayAndMonth(date: string): string {
  const [, month, day] = date.split('-').map(Number)
  return `${day} de ${monthNames[month - 1]}`
}

// "2026-12-05" → "dezembro": o nome da fatura é o mês do vencimento.
export const monthOfDate = (date: string) => monthNames[Number(date.split('-')[1]) - 1]
