import { Checkbox } from '@/components/ui/checkbox'

// A caixa do aceite (etapa A.4), no cadastro e na tela de aceite: vem desmarcada, e os textos abrem em outra aba, para
// não perder o que já foi digitado. As páginas são HTML pronto servido pela API (/termos, /privacidade), fora do
// React Router: link comum. Aceita os termos e diz que leu a política: a política informa (base legal: contrato), não
// pede consentimento, como dizem os termos (seção 2).
export function TermsConsent({
  checked,
  onCheckedChange,
  invalid,
}: {
  checked: boolean
  onCheckedChange: (checked: boolean) => void
  invalid?: boolean
}) {
  return (
    <div className="flex items-start gap-3">
      <Checkbox
        id="acceptedTerms"
        checked={checked}
        onCheckedChange={(value) => onCheckedChange(value === true)}
        aria-invalid={invalid}
        className="mt-0.5 shrink-0"
      />
      <label htmlFor="acceptedTerms" className="text-sm leading-relaxed">
        Li e aceito os{' '}
        <a href="/termos" target="_blank" rel="noopener" className="font-medium underline underline-offset-4">
          Termos de uso
        </a>
        , li a{' '}
        <a href="/privacidade" target="_blank" rel="noopener" className="font-medium underline underline-offset-4">
          Política de privacidade
        </a>{' '}
        e tenho 18 anos ou mais.
      </label>
    </div>
  )
}

// Os mesmos links, discretos, no pé das telas de entrada.
export function LegalLinks() {
  return (
    <nav aria-label="Documentos" className="flex justify-center gap-5 text-xs text-muted-foreground">
      <a href="/privacidade" className="hover:text-foreground">
        Privacidade
      </a>
      <a href="/termos" className="hover:text-foreground">
        Termos de uso
      </a>
    </nav>
  )
}
