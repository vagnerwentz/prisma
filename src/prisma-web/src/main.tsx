import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { toast } from 'sonner'
import { App } from './App'
import { Toaster } from './components/Toaster'
import { reportClientError } from './lib/clientErrors'
import { createQueryClient } from './lib/queryClient'
import './index.css'

// Erro fora da montagem da tela (num clique, numa promessa): antes sumia sem ninguém saber; agora vai ao
// log do servidor (etapa H.3b).
window.addEventListener('error', (event) => void reportClientError(event.error ?? event.message, 'silent'))
window.addEventListener('unhandledrejection', (event) => void reportClientError(event.reason, 'silent'))

const queryClient = createQueryClient(() => toast('Sua sessão expirou. Entre de novo para continuar.'))

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
      <Toaster />
    </QueryClientProvider>
  </StrictMode>,
)
