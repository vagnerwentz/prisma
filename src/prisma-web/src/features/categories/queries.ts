import { useQuery } from '@tanstack/react-query'
import { api, unwrap, type Schemas } from '@/lib/api'

export type CategoryNode = Schemas['ListCategoriesCategoryNode']

export type CategoryLabel = { name: string; parentName: string | null }

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
    labels.set(root.id, { name: root.name, parentName: null })
    for (const sub of root.subcategories) labels.set(sub.id, { name: sub.name, parentName: root.name })
  }
  return labels
}
