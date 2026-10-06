import { ArrowLeftRight, CreditCard, PenLine } from 'lucide-react'
import { createElement, useState, type CSSProperties } from 'react'
import type { AccountType } from '@/features/accounts/labels'
import { splitSymbol } from '@/features/investments/assets'
import { findBrand, readableTextOn, type BrandMark } from '@/lib/brands/merchants'
import { cn } from '@/lib/utils'
import { accountTypeIcons, categoryIcon } from './categoryIcons'

type Size = 'sm' | 'md' | 'lg' | 'xl'

const box: Record<Size, string> = {
  sm: 'size-7 rounded-lg [&_svg]:size-3.5 text-[0.6rem]',
  md: 'size-10 rounded-xl [&_svg]:size-5 text-xs',
  lg: 'size-12 rounded-2xl [&_svg]:size-6 text-sm',
  xl: 'size-16 rounded-[1.25rem] [&_svg]:size-8 text-base',
}

// Cor neutra, mas estável, para categorias sem cor (criadas antes da identidade visual).
function fallbackColor(seed: string): string {
  let hash = 0
  for (const char of seed) hash = (hash * 31 + char.charCodeAt(0)) | 0
  const palette = ['#8b5cf6', '#3b82f6', '#22d3ee', '#84cc16', '#f59e0b', '#f43f5e', '#ec4899', '#14b8a6']
  return palette[Math.abs(hash) % palette.length]
}

export function BrandTile({ brand, size = 'md', className }: { brand: BrandMark; size?: Size; className?: string }) {
  // Ícone de banco ou corretora: o desenho já traz o fundo da marca e ocupa o quadro inteiro. Como
  // imagem, cada arquivo fica isolado: os ids internos (máscaras, degradês) não colidem entre si.
  if (brand.kind === 'image')
    return (
      <span role="img" aria-label={brand.name} className={cn('flex shrink-0 overflow-hidden', box[size], className)}>
        <img src={brand.src} alt="" draggable={false} className="size-full" />
      </span>
    )

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
  return <CategoryTile name={props.category?.name} icon={props.category?.icon} color={props.category?.color} size={props.size} />
}

export function AccountTile({ name, type, size = 'md' }: { name: string; type: AccountType; size?: Size }) {
  const brand = findBrand(name)
  if (brand) return <BrandTile brand={brand} size={size} />
  return (
    <span className={cn('flex shrink-0 items-center justify-center bg-secondary text-secondary-foreground', box[size])}>
      {createElement(accountTypeIcons[type], { 'aria-hidden': true })}
    </span>
  )
}

// Transferência: tinta neutra, sem cor de categoria (não é receita nem despesa). O pagamento de
// fatura leva o ícone do cartão.
export function TransferTile({ payment, size = 'md' }: { payment?: boolean; size?: Size }) {
  const Icon = payment ? CreditCard : ArrowLeftRight
  return (
    <span className={cn('flex shrink-0 items-center justify-center border bg-background text-foreground', box[size])}>
      <Icon aria-hidden strokeWidth={1.75} />
    </span>
  )
}

// Descrição ainda sem marca nem categoria: um lápis parado, no lugar que o logo ou o ícone da categoria
// vão ocupar. O espaço fica sempre ocupado, então o texto não pula quando a marca é reconhecida; e não
// é um círculo tracejado, que parecia carregando.
export function DraftTile({ size = 'md' }: { size?: Size }) {
  return (
    <span className={cn('flex shrink-0 items-center justify-center bg-secondary text-muted-foreground', box[size])}>
      <PenLine aria-hidden strokeWidth={1.75} />
    </span>
  )
}

const assetText: Record<Size, { root: string; suffix: string }> = {
  sm: { root: 'text-[0.45rem]', suffix: 'text-[0.4rem]' },
  md: { root: 'text-[0.625rem]', suffix: 'text-[0.55rem]' },
  lg: { root: 'text-xs', suffix: 'text-[0.625rem]' },
  xl: { root: 'text-sm', suffix: 'text-xs' },
}

// Ativo da bolsa (docs/investimentos.md, seção 5 e etapa 4). Com logo (logoSrc), o logo guardado pelo Prisma, como
// imagem: SVG nunca entra no HTML. Sem logo, ou se a imagem falhar, o código no ladrilho, a raiz em destaque e o
// número menor, numa faixa do espectro tirada da raiz. O mesmo ativo tem sempre a mesma cor, e ITSA3 e ITSA4 (a
// mesma empresa) também.
export function AssetTile({
  symbol,
  logo,
  size = 'md',
  className,
}: {
  symbol: string
  logo?: string
  size?: Size
  className?: string
}) {
  const [failed, setFailed] = useState<string | null>(null)
  if (logo && failed !== logo)
    return (
      <span aria-hidden className={cn('flex shrink-0 overflow-hidden bg-muted', box[size], className)}>
        <img
          src={logo}
          alt=""
          draggable={false}
          loading="lazy"
          decoding="async"
          onError={() => setFailed(logo)}
          className="size-full"
        />
      </span>
    )

  const { root, suffix } = splitSymbol(symbol)
  return (
    <span
      aria-hidden
      className={cn(
        'category-tile flex shrink-0 flex-col items-center justify-center leading-none font-semibold tracking-tight',
        box[size],
        className,
      )}
      style={{ '--tile': fallbackColor(root) } as CSSProperties}
    >
      <span className={assetText[size].root}>{root}</span>
      {suffix && <span className={cn('mt-0.5 opacity-75', assetText[size].suffix)}>{suffix}</span>}
    </span>
  )
}
