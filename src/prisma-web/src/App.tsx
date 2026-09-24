import { lazy, Suspense } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { AppLayout } from '@/components/AppLayout'
import { PrismLogo } from '@/components/brand/PrismLogo'
import { RedirectIfAuthenticated, RequireAuth } from '@/features/auth/guards'
import { TransactionsPage } from '@/features/transactions/TransactionsPage'

// A lista (tela inicial) vai no pacote principal; as demais telas são baixadas quando abertas.
const LoginPage = lazy(() => import('@/features/auth/LoginPage').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() => import('@/features/auth/RegisterPage').then((m) => ({ default: m.RegisterPage })))
const NewTransactionPage = lazy(() =>
  import('@/features/transactions/NewTransactionPage').then((m) => ({ default: m.NewTransactionPage })),
)
const AccountsPage = lazy(() => import('@/features/accounts/AccountsPage').then((m) => ({ default: m.AccountsPage })))

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
      <Suspense fallback={<Loading />}>
        <Routes>
          <Route element={<RedirectIfAuthenticated />}>
            <Route path="/entrar" element={<LoginPage />} />
            <Route path="/cadastro" element={<RegisterPage />} />
          </Route>
          <Route element={<RequireAuth />}>
            <Route path="/lancar" element={<NewTransactionPage />} />
            <Route element={<AppLayout />}>
              <Route path="/" element={<TransactionsPage />} />
              <Route path="/contas" element={<AccountsPage />} />
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </Suspense>
    </BrowserRouter>
  )
}
