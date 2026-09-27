import { BottomSheet } from '@/components/BottomSheet'
import { SheetTitle } from '@/components/ui/sheet'
import { AccountForm } from './AccountForm'
import type { Account } from './queries'

// Painel de criar conta, aberto em Contas e no Lançar (quando ainda não há conta). Cada tela decide o
// que vem depois: Contas abre o detalhe da conta nova; o Lançar só fecha e segue para o lançamento.
export function NewAccountSheet({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (account: Account) => void
}) {
  return (
    <BottomSheet open={open} onClose={onClose}>
      <header className="px-4 pt-2 pb-3">
        <SheetTitle className="font-display text-2xl font-normal">Nova conta</SheetTitle>
      </header>
      <AccountForm formId="new-account" submitLabel="Criar conta" onSaved={onCreated} />
    </BottomSheet>
  )
}
