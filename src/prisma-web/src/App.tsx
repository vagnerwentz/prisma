import { lazy, Suspense } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router'
import { AppLayout } from '@/components/AppLayout'
import { ErrorBoundary } from '@/components/ErrorBoundary'
import { NotFoundPage } from '@/components/NotFoundPage'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { RedirectIfAuthenticated, RequireAuth } from '@/features/auth/guards'
import { SummaryPage } from '@/features/dashboard/SummaryPage'

// O resumo (tela inicial) vai no pacote principal; as demais telas são baixadas quando abertas.
const LoginPage = lazy(() => import('@/features/auth/LoginPage').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() => import('@/features/auth/RegisterPage').then((m) => ({ default: m.RegisterPage })))
const TransactionsPage = lazy(() =>
  import('@/features/transactions/TransactionsPage').then((m) => ({ default: m.TransactionsPage })),
)
const NewTransactionPage = lazy(() =>
  import('@/features/transactions/NewTransactionPage').then((m) => ({ default: m.NewTransactionPage })),
)
const AnalysisPage = lazy(() => import('@/features/dashboard/AnalysisPage').then((m) => ({ default: m.AnalysisPage })))
const AccountsPage = lazy(() => import('@/features/accounts/AccountsPage').then((m) => ({ default: m.AccountsPage })))
const AccountDetailPage = lazy(() =>
  import('@/features/accounts/AccountDetailPage').then((m) => ({ default: m.AccountDetailPage })),
)

// Só em desenvolvimento: abre a tela de erro de propósito, para conferir o visual
// (/dev/erro e /dev/erro?tipo=atualizacao).
const CrashOnPurpose = import.meta.env.DEV ? lazy(() => import('@/components/CrashOnPurpose')) : null

function Loading() {
  return (
    <div className="flex min-h-dvh items-center justify-center">
      <PrismLogo animated className="size-12" />
    </div>
  )
}

export function App() {
  return (
    <BrowserRouter>
      <ErrorBoundary>
        <Suspense fallback={<Loading />}>
          <Routes>
            <Route element={<RedirectIfAuthenticated />}>
              <Route path="/entrar" element={<LoginPage />} />
              <Route path="/cadastro" element={<RegisterPage />} />
            </Route>
            <Route element={<RequireAuth />}>
              <Route path="/lancar" element={<NewTransactionPage />} />
              <Route element={<AppLayout />}>
                <Route path="/" element={<SummaryPage />} />
                <Route path="/lancamentos" element={<TransactionsPage />} />
                <Route path="/analise" element={<AnalysisPage />} />
                <Route path="/contas" element={<AccountsPage />} />
                <Route path="/contas/:id" element={<AccountDetailPage />} />
              </Route>
            </Route>
            {CrashOnPurpose && <Route path="/dev/erro" element={<CrashOnPurpose />} />}
            <Route path="*" element={<NotFoundPage />} />
          </Routes>
        </Suspense>
      </ErrorBoundary>
    </BrowserRouter>
  )
}
