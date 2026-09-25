import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { toast } from 'sonner'
import { App } from './App'
import { Toaster } from './components/Toaster'
import { createQueryClient } from './lib/queryClient'
import './index.css'

const queryClient = createQueryClient(() => toast('Sua sessão expirou. Entre de novo para continuar.'))

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
      <Toaster />
    </QueryClientProvider>
  </StrictMode>,
)
