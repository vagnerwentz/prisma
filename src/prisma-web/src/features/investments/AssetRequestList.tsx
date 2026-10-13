import { toast } from 'sonner'
import { AssetTile } from '@/components/brand/Tiles'
import { Button } from '@/components/ui/button'
import { todayInSaoPaulo } from '@/lib/dates'
import { arrivedItem, requestedLabel, type AssetRequest } from './assetRequests'
import { logoSrc, type AssetItem } from './assets'
import { useRemoveAssetRequest, useRestoreAssetRequest } from './queries'

// Os avisos de ativo faltando na tela de Investimentos (docs/investimentos.md, I.7c3). O que chegou fica no alto, com
// "Lançar nota"; o que espera, no fim. Cancelar e dispensar têm "Desfazer". Os botões do que chegou ficam numa linha
// própria: ao lado, espremiam o nome em 390 px.
function useDropRequest() {
  const remove = useRemoveAssetRequest()
  const restore = useRestoreAssetRequest()
  return (request: AssetRequest, message: string) =>
    remove.mutate(request.id, {
      onSuccess: () =>
        toast(message, {
          action: {
            label: 'Desfazer',
            onClick: () => restore.mutate(request.id, { onError: (error) => toast.error(error.message) }),
          },
        }),
      onError: (error) => toast.error(error.message),
    })
}

export function ArrivedRequests({ requests, onLaunch }: { requests: AssetRequest[]; onLaunch: (asset: AssetItem) => void }) {
  const drop = useDropRequest()
  const arrived = requests.flatMap((request) => {
    const asset = arrivedItem(request)
    return asset ? [{ request, asset }] : []
  })
  if (arrived.length === 0) return null

  return (
    <section aria-label="Ativos que chegaram">
      <ul className="surface overflow-hidden rounded-2xl">
        {arrived.map(({ request, asset }) => (
          <li key={request.id} className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 [&+&]:border-t [&+&]:border-border/60">
            <AssetTile symbol={asset.symbol} logo={logoSrc(asset)} />
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="font-medium">{asset.symbol} chegou</span>
              <span className="truncate text-sm text-muted-foreground">{asset.name}</span>
            </span>
            <span className="flex w-full justify-end gap-1">
              <Button size="sm" variant="ghost" className="rounded-full text-muted-foreground" onClick={() => drop(request, `Aviso de ${asset.symbol} dispensado`)}>
                Dispensar
              </Button>
              <Button size="sm" className="rounded-full" onClick={() => onLaunch(asset)}>
                Lançar nota
              </Button>
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}

export function WaitingRequests({ requests }: { requests: AssetRequest[] }) {
  const drop = useDropRequest()
  const waiting = requests.filter((r) => r.status === 'Waiting')
  if (waiting.length === 0) return null
  const today = todayInSaoPaulo()

  return (
    <section className="flex flex-col gap-3">
      <h2 className="px-1 text-sm font-medium text-foreground/75">Esperando chegar</h2>
      <ul className="surface overflow-hidden rounded-2xl">
        {waiting.map((request) => (
          <li key={request.id} className="flex items-center gap-3 px-4 py-3 [&+&]:border-t [&+&]:border-border/60">
            <AssetTile symbol={request.symbol} />
            <span className="flex min-w-0 flex-1 flex-col">
              <span className="font-medium">{request.symbol}</span>
              <span className="text-sm text-muted-foreground">
                Avisado em {requestedLabel(request.requestedOn, today)}
              </span>
            </span>
            <Button
              size="sm"
              variant="ghost"
              className="shrink-0 rounded-full text-muted-foreground"
              onClick={() => drop(request, `Aviso de ${request.symbol} cancelado`)}
            >
              Cancelar
            </Button>
          </li>
        ))}
      </ul>
    </section>
  )
}
