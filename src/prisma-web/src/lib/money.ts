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
