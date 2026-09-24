// Dinheiro chega da API em centavos inteiros (CLAUDE.md, regra 1). A formatação trabalha só com
// inteiros: nada de dividir por 100 em ponto flutuante.
export function formatCents(cents: number): string {
  if (!Number.isSafeInteger(cents)) throw new RangeError('Valor em centavos deve ser um inteiro.')

  const sign = cents < 0 ? '-' : ''
  const absolute = Math.abs(cents)
  const centsPart = absolute % 100
  const reais = (absolute - centsPart) / 100

  const groupedReais = String(reais).replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  return `${sign}R$ ${groupedReais},${String(centsPart).padStart(2, '0')}`
}

// Campo de valor no estilo dos apps de banco: só os dígitos contam e entram pela direita
// ("4590" → R$ 45,90). Até 13 dígitos, para continuar um inteiro seguro.
const maxDigits = 13

export function parseCentsInput(text: string): number {
  const digits = text.replace(/\D/g, '').slice(0, maxDigits)
  return digits === '' ? 0 : Number(digits)
}

// Mesma regra de Money.SplitInto no backend (CLAUDE.md, regra 2): o resto vai, um centavo por
// vez, para as primeiras partes, e a soma é sempre o total.
export function splitCents(totalCents: number, parts: number): number[] {
  if (!Number.isSafeInteger(totalCents)) throw new RangeError('Valor em centavos deve ser um inteiro.')
  if (!Number.isInteger(parts) || parts < 1) throw new RangeError('O número de partes deve ser pelo menos 1.')

  const remainder = totalCents % parts
  const base = (totalCents - remainder) / parts
  return Array.from({ length: parts }, (_, i) => (i < remainder ? base + 1 : base))
}

// Prévia exata das parcelas: "5x de R$ 100,01 e 5x de R$ 100,00".
export function describeInstallments(totalCents: number, parts: number): string {
  if (parts === 1) return 'à vista'

  const split = splitCents(totalCents, parts)
  const larger = split.filter((cents) => cents === split[0]).length
  const first = `${larger}x de ${formatCents(split[0])}`
  return larger === parts ? first : `${first} e ${parts - larger}x de ${formatCents(split[parts - 1])}`
}
