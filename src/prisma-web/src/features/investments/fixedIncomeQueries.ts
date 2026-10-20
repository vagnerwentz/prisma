import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, unwrap, type Schemas } from '@/lib/api'
import { fixedIncomeKey } from '@/lib/queryKeys'
import { invalidateMoney } from '@/features/transactions/queries'

export type GoalChanges = Schemas['SaveGoalRequest']
export type NewDeposit = Schemas['SaveDepositCreateRequest']
export type DepositChanges = Schemas['SaveDepositUpdateRequest']
export type NewRedemption = Schemas['RedemptionsCreateRequest']

export function useGoals() {
  return useQuery({
    queryKey: [...fixedIncomeKey, 'goals'],
    queryFn: async () => unwrap(await api.GET('/savings-goals')),
  })
}

// Saldos de todos os objetivos até hoje: a API calcula na hora, com as taxas gravadas.
export function useFixedIncomeBalances() {
  return useQuery({
    queryKey: [...fixedIncomeKey, 'balances'],
    queryFn: async () => unwrap(await api.GET('/fixed-income/balances')),
  })
}

export function useFixedIncomeDeposits(goal: string) {
  return useQuery({
    queryKey: [...fixedIncomeKey, 'deposits', goal],
    queryFn: async () => unwrap(await api.GET('/fixed-income-deposits', { params: { query: { goal } } })),
  })
}

export function useFixedIncomeEvolution(scope: string, from: string, step: 'day' | 'week' | 'month') {
  return useQuery({
    queryKey: [...fixedIncomeKey, 'evolution', scope, from, step],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      unwrap(await api.GET('/fixed-income/evolution', { params: { query: { goal: scope, from, step } } })),
  })
}

// Tudo o que muda um objetivo ou um aporte recarrega as consultas de renda fixa; o aporte com origem mexe também em
// saldos e lançamentos (invalidateMoney).
function useFixedIncomeMutation<TInput, TOutput>(
  mutationFn: (input: TInput) => Promise<TOutput>,
  { money }: { money: boolean } = { money: false },
) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: fixedIncomeKey })
      if (money) await invalidateMoney(queryClient)
    },
  })
}

export const useSaveGoal = () =>
  useFixedIncomeMutation(async ({ id, body }: { id?: string; body: GoalChanges }) =>
    id
      ? unwrap(await api.PUT('/savings-goals/{id}', { params: { path: { id } }, body }))
      : unwrap(await api.POST('/savings-goals', { body })),
  )

export const useDeleteGoal = () =>
  useFixedIncomeMutation(async ({ id, deposits }: { id: string; deposits?: 'keep' | 'delete' }) =>
    unwrap(await api.DELETE('/savings-goals/{id}', { params: { path: { id }, query: { deposits } } })),
  )

export const useRestoreGoal = () =>
  useFixedIncomeMutation(async ({ id, detachedDepositIds }: { id: string; detachedDepositIds: string[] }) => {
    const { error, response } = await api.POST('/savings-goals/{id}/restore', {
      params: { path: { id } },
      body: { detachedDepositIds },
    })
    if (!response.ok) throw new ApiError(response.status, error)
  }, { money: true })

export const useCreateDeposit = () =>
  useFixedIncomeMutation(async (body: NewDeposit) => unwrap(await api.POST('/fixed-income-deposits', { body })), { money: true })

export const useUpdateDeposit = () =>
  useFixedIncomeMutation(
    async ({ id, body }: { id: string; body: DepositChanges }) =>
      unwrap(await api.PUT('/fixed-income-deposits/{id}', { params: { path: { id } }, body })),
    { money: true },
  )

export const useMoveDeposit = () =>
  useFixedIncomeMutation(async ({ id, goalId }: { id: string; goalId: string | null }) =>
    unwrap(await api.PATCH('/fixed-income-deposits/{id}/goal', { params: { path: { id } }, body: { goalId } })),
  )

export const useDeleteDeposit = () =>
  useFixedIncomeMutation(
    async ({ id, transfer }: { id: string; transfer?: 'keep' | 'delete' }) => {
      const { error, response } = await api.DELETE('/fixed-income-deposits/{id}', {
        params: { path: { id }, query: { transfer } },
      })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    { money: true },
  )

export const useRestoreDeposit = () =>
  useFixedIncomeMutation(async (id: string) => {
    const { error, response } = await api.POST('/fixed-income-deposits/{id}/restore', { params: { path: { id } } })
    if (!response.ok) throw new ApiError(response.status, error)
  }, { money: true })

// Resgates (docs/renda-fixa-resgate.md): a lista de um aporte, a prévia (sem gravar) e criar, excluir e desfazer. Todos mexem em saldos, no
// Investido e nos lançamentos quando o resgate cai numa conta, então invalidam também o dinheiro.
export function useRedemptions(depositId: string | null) {
  return useQuery({
    queryKey: [...fixedIncomeKey, 'redemptions', depositId],
    enabled: depositId !== null,
    queryFn: async () =>
      unwrap(await api.GET('/fixed-income-deposits/{id}/redemptions', { params: { path: { id: depositId! } } })),
  })
}

// A prévia do resgate: o saldo na data e, com o valor, o resgate como seria gravado. Mantém a anterior enquanto a nova carrega, para o
// painel não piscar a cada dígito.
export function useRedemptionPreview(
  depositId: string,
  query: { date: string; grossCents: number | null; netCents: number | null },
) {
  return useQuery({
    queryKey: [...fixedIncomeKey, 'redemption-preview', depositId, query.date, query.grossCents, query.netCents],
    placeholderData: keepPreviousData,
    queryFn: async () =>
      unwrap(
        await api.GET('/fixed-income-deposits/{id}/redemptions/preview', {
          params: {
            path: { id: depositId },
            query: {
              date: query.date,
              grossCents: query.grossCents ?? undefined,
              netCents: query.netCents ?? undefined,
            },
          },
        }),
      ),
  })
}

export const useCreateRedemption = () =>
  useFixedIncomeMutation(
    async ({ depositId, body }: { depositId: string; body: NewRedemption }) =>
      unwrap(await api.POST('/fixed-income-deposits/{id}/redemptions', { params: { path: { id: depositId } }, body })),
    { money: true },
  )

export const useDeleteRedemption = () =>
  useFixedIncomeMutation(
    async ({ depositId, id, transfer }: { depositId: string; id: string; transfer?: 'keep' | 'delete' }) => {
      const { error, response } = await api.DELETE('/fixed-income-deposits/{id}/redemptions/{redemptionId}', {
        params: { path: { id: depositId, redemptionId: id }, query: { transfer } },
      })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    { money: true },
  )

export const useRestoreRedemption = () =>
  useFixedIncomeMutation(
    async ({ depositId, id }: { depositId: string; id: string }) => {
      const { error, response } = await api.POST('/fixed-income-deposits/{id}/redemptions/{redemptionId}/restore', {
        params: { path: { id: depositId, redemptionId: id } },
      })
      if (!response.ok) throw new ApiError(response.status, error)
    },
    { money: true },
  )
