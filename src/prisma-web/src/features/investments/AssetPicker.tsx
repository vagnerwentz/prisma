import { Search } from 'lucide-react'
import { useId, useState } from 'react'
import { AssetTile } from '@/components/brand/Tiles'
import { StaleFade } from '@/components/StaleFade'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { assetKindLabel, logoSrc, type AssetItem } from './assets'
import { useAssetSearch } from './queries'

// Escolher um ativo da bolsa (docs/investimentos.md, seção 8, etapa 3): por código ou nome, sem acento.
// Combobox da WAI-ARIA com a lista logo abaixo do campo. Escolhido, o campo dá lugar ao ativo, com "Trocar".
export function AssetPicker({
  value,
  onChange,
  autoFocus,
}: {
  value: AssetItem | null
  onChange: (asset: AssetItem | null) => void
  autoFocus?: boolean
}) {
  const [text, setText] = useState('')
  const [active, setActive] = useState(-1)
  const listId = useId()
  const search = useAssetSearch(value ? '' : text)

  const typed = text.trim() !== ''
  const items = typed ? (search.data ?? []) : []
  const searched = typed && search.isSuccess && !search.isPlaceholderData
  const nothing = searched && items.length === 0

  // Quantos achou: só quando o resultado chega, para o leitor de tela não falar a cada letra.
  const announcement = !searched
    ? ''
    : items.length
      ? `${items.length} ${items.length === 1 ? 'ativo' : 'ativos'}. Setas para navegar, Enter para escolher.`
      : 'Nenhum ativo encontrado.'

  const choose = (asset: AssetItem) => {
    onChange(asset)
    setText('')
    setActive(-1)
  }

  if (value)
    return (
      <div className="flex items-center gap-3 rounded-2xl border bg-card py-2 pr-2 pl-2">
        <AssetTile symbol={value.symbol} logo={logoSrc(value)} />
        <AssetText asset={value} />
        <Button type="button" size="sm" variant="ghost" className="shrink-0 rounded-full" onClick={() => onChange(null)}>
          Trocar
        </Button>
        <p role="status" className="sr-only">
          {value.symbol} escolhido.
        </p>
      </div>
    )

  const optionId = (i: number) => `${listId}-${i}`

  return (
    <div className="flex flex-col gap-2">
      <label className="flex items-center gap-3 rounded-2xl border bg-card py-2 pr-3 pl-3 focus-within:ring-2 focus-within:ring-ring/50">
        <Search aria-hidden className="size-5 shrink-0 text-muted-foreground" strokeWidth={1.75} />
        <input
          value={text}
          autoFocus={autoFocus}
          placeholder="Código ou nome, ex.: BBAS3, Maxi Renda"
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          maxLength={50}
          role="combobox"
          aria-label="Ativo"
          aria-autocomplete="list"
          aria-expanded={items.length > 0}
          aria-controls={listId}
          aria-activedescendant={active >= 0 && active < items.length ? optionId(active) : undefined}
          className="h-10 min-w-0 flex-1 bg-transparent text-base outline-none placeholder:text-muted-foreground"
          onChange={(event) => {
            setText(event.target.value)
            setActive(-1)
          }}
          onKeyDown={(event) => {
            const n = items.length
            if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && n) {
              event.preventDefault()
              const step = event.key === 'ArrowDown' ? 1 : -1
              setActive((current) => (current < 0 ? (step > 0 ? 0 : n - 1) : (current + step + n) % n))
            } else if (event.key === 'Enter' && n) {
              // Enter sem destaque escolhe o primeiro: digitar o código exato e dar Enter basta.
              event.preventDefault()
              choose(items[active >= 0 ? active : 0])
            } else if (event.key === 'Escape' && typed) {
              event.preventDefault()
              setText('')
              setActive(-1)
            }
          }}
        />
      </label>

      <StaleFade stale={search.isPlaceholderData}>
        <ul id={listId} role="listbox" aria-label="Ativos encontrados" className="flex flex-col">
          {items.map((asset, i) => (
            <li
              key={asset.id}
              id={optionId(i)}
              role="option"
              aria-selected={i === active}
              onPointerDown={(event) => {
                event.preventDefault()
                choose(asset)
              }}
              className={cn(
                'flex cursor-pointer items-center gap-3 rounded-xl px-2 py-2 select-none hover:bg-muted/50',
                i === active && 'bg-primary/8 ring-1 ring-primary/40 hover:bg-primary/8',
              )}
            >
              <AssetTile symbol={asset.symbol} logo={logoSrc(asset)} />
              <AssetText asset={asset} />
            </li>
          ))}
        </ul>
      </StaleFade>

      {nothing && <p className="px-2 text-sm text-muted-foreground">Nenhum ativo com “{text.trim()}”.</p>}
      {search.isError && typed && <p className="px-2 text-sm text-muted-foreground">Não deu para buscar agora. Tente de novo.</p>}

      <p role="status" className="sr-only">
        {announcement}
      </p>
    </div>
  )
}

// Código e tipo na primeira linha, nome na segunda. O ativo que saiu da bolsa continua escolhível
// (rendimento antigo), com o aviso.
function AssetText({ asset }: { asset: AssetItem }) {
  return (
    <span className="flex min-w-0 flex-1 flex-col">
      <span className="flex items-baseline gap-2">
        <span className="font-medium">{asset.symbol}</span>
        <span className="truncate text-xs text-muted-foreground">
          {assetKindLabel(asset.kind)}
          {!asset.isActive && ' · saiu da bolsa'}
        </span>
      </span>
      <span className="truncate text-sm text-muted-foreground">{asset.name}</span>
    </span>
  )
}
