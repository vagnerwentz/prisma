// Tipos de legal-pages.mjs, para o vite.config.ts (tsconfig.node.json).
export type LegalPage = { source: string; file: string; path: string; description: string }

export const legalPages: LegalPage[]

export function renderLegalPage(template: string, page: LegalPage): { html: string; sections: number }
