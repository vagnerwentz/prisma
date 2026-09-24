import { createElement, type CSSProperties } from 'react'
import type { AccountType } from '@/features/accounts/labels'
import { findBrand, readableTextOn, type BrandMark } from '@/lib/brands/merchants'
import { cn } from '@/lib/utils'
import { accountTypeIcons, categoryIcon } from './categoryIcons'

type Size = 'sm' | 'md' | 'lg'

const box: Record<Size, string> = {
  sm: 'size-7 rounded-lg [&_svg]:size-3.5 text-[0.6rem]',
  md: 'size-10 rounded-xl [&_svg]:size-5 text-xs',
  lg: 'size-12 rounded-2xl [&_svg]:size-6 text-sm',
}

// Cor neutra, mas estável, para categorias sem cor (criadas antes da identidade visual).
function fallbackColor(seed: string): string {
  let hash = 0
  for (const char of seed) hash = (hash * 31 + char.charCodeAt(0)) | 0
  const palette = ['#8b5cf6', '#3b82f6', '#22d3ee', '#84cc16', '#f59e0b', '#f43f5e', '#ec4899', '#14b8a6']
  return palette[Math.abs(hash) % palette.length]
}

export function BrandTile({ brand, size = 'md', className }: { brand: BrandMark; size?: Size; className?: string }) {
  const onDark = readableTextOn(brand.color) === 'light'
  return (
    <span
      role="img"
      aria-label={brand.name}
      className={cn('flex shrink-0 items-center justify-center font-semibold', box[size], className)}
      style={{ backgroundColor: brand.color, color: onDark ? '#fff' : '#111' }}
    >
      {brand.kind === 'logo' ? (
        <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden>
          <path d={brand.path} />
        </svg>
      ) : (
        <span className="leading-none tracking-tight">{brand.text}</span>
      )}
    </span>
  )
}

export function CategoryTile({
  name,
  icon,
  color,
  size = 'md',
  className,
}: {
  name?: string | null
  icon?: string | null
  color?: string | null
  size?: Size
  className?: string
}) {
  const tile = color ?? fallbackColor(name ?? '')
  return (
    <span
      className={cn('category-tile flex shrink-0 items-center justify-center', box[size], className)}
      style={{ '--tile': tile } as CSSProperties}
    >
      {createElement(categoryIcon(icon), { 'aria-hidden': true, strokeWidth: 2 })}
    </span>
  )
}

// Lançamento: a marca reconhecida na descrição vence; senão, o ícone da categoria.
export function EntryTile(props: {
  description?: string | null
  category?: { name: string; icon?: string | null; color?: string | null } | null
  size?: Size
}) {
  const brand = findBrand(props.description)
  if (brand) return <BrandTile brand={brand} size={props.size} />
  return (
    <CategoryTile name={props.category?.name} icon={props.category?.icon} color={props.category?.color} size={props.size} />
  )
}

export function AccountTile({ name, type, size = 'md' }: { name: string; type: AccountType; size?: Size }) {
  const brand = findBrand(name)
  if (brand) return <BrandTile brand={brand} size={size} />
  return (
    <span
      className={cn('flex shrink-0 items-center justify-center bg-secondary text-secondary-foreground', box[size])}
    >
      {createElement(accountTypeIcons[type], { 'aria-hidden': true })}
    </span>
  )
}
