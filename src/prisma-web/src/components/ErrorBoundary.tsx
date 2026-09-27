import { Component, type ReactNode } from 'react'
import { useLocation } from 'react-router'
import { ErrorScreen } from '@/components/ErrorScreen'
import { reportClientError } from '@/lib/clientErrors'
import { crashKind } from '@/lib/crash'

type Props = { resetKey: string; children: ReactNode }
type State = { error: unknown; failed: boolean; resetKey: string }

// Erro de renderização ou tela sob demanda que não carregou: em vez da página em branco, a tela
// de erro. Trocar de rota (resetKey) tenta de novo, para o "voltar" do navegador funcionar.
class Boundary extends Component<Props, State> {
  state: State = { error: undefined, failed: false, resetKey: this.props.resetKey }

  static getDerivedStateFromError(error: unknown): Partial<State> {
    return { error, failed: true }
  }

  // A tela que quebrou vai ao log do servidor (etapa H.3b); a de versão nova não é bug e fica de fora.
  componentDidCatch(error: unknown) {
    void reportClientError(error, 'crash')
  }

  static getDerivedStateFromProps(props: Props, state: State): Partial<State> | null {
    return props.resetKey === state.resetKey ? null : { error: undefined, failed: false, resetKey: props.resetKey }
  }

  render() {
    if (!this.state.failed) return this.props.children
    return <ErrorScreen kind={crashKind(this.state.error)} error={this.state.error} />
  }
}

export function ErrorBoundary({ children }: { children: ReactNode }) {
  const { pathname } = useLocation()
  return <Boundary resetKey={pathname}>{children}</Boundary>
}
