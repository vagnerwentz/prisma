import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'

export type CategoryNode = Schemas['ListCategoriesCategoryNode']

export type CategoryLabel = { name: string; parentName: string | null; icon: string | null; color: string | null }

export const categoriesKey = ['categories'] as const

export function useCategories() {
  return useQuery({
    queryKey: categoriesKey,
    queryFn: async () => unwrap(await api.GET('/categories')),
  })
}

// Nome de cada categoria e subcategoria por id, para exibir nas transações.
export function categoryLabels(tree: CategoryNode[]): Map<string, CategoryLabel> {
  const labels = new Map<string, CategoryLabel>()
  for (const root of tree) {
    labels.set(root.id, { name: root.name, parentName: null, icon: root.icon ?? null, color: root.color ?? null })
    for (const sub of root.subcategories)
      labels.set(sub.id, {
        name: sub.name,
        parentName: root.name,
        icon: sub.icon ?? root.icon ?? null,
        color: sub.color ?? root.color ?? null,
      })
  }
  return labels
}

// Categoria escolhida (id de categoria ou de subcategoria) entre as raízes do tipo.
export function resolveCategory(roots: CategoryNode[], categoryId: string) {
  const root = roots.find((r) => r.id === categoryId || r.subcategories.some((s) => s.id === categoryId))
  const sub = root?.subcategories.find((s) => s.id === categoryId)
  const label: Omit<CategoryLabel, 'parentName'> | undefined = root && {
    name: sub?.name ?? root.name,
    icon: sub?.icon ?? root.icon ?? null,
    color: sub?.color ?? root.color ?? null,
  }
  return { root, label }
}
