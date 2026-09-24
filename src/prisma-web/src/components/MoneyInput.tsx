import type { ComponentProps } from 'react'
import { Input } from '@/components/ui/input'
import { formatCents, parseCentsInput } from '@/lib/money'

type Props = Omit<ComponentProps<typeof Input>, 'value' | 'onChange' | 'type'> & {
  value: number
  onChange: (cents: number) => void
}

// Valor em centavos, digitado como nos apps de banco: "4590" vira R$ 45,90.
export function MoneyInput({ value, onChange, ...props }: Props) {
  return (
    <Input
      {...props}
      type="text"
      inputMode="numeric"
      autoComplete="off"
      value={value === 0 ? '' : formatCents(value)}
      placeholder={formatCents(0)}
      onChange={(event) => onChange(parseCentsInput(event.target.value))}
    />
  )
}
