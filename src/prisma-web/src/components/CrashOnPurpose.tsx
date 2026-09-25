import { useSearchParams } from 'react-router'

// Só em desenvolvimento (App.tsx): lança um erro para mostrar a tela de erro. Com
// ?tipo=atualizacao, imita a mensagem do navegador quando falta um arquivo de uma versão antiga.
export default function CrashOnPurpose(): never {
  const [params] = useSearchParams()
  if (params.get('tipo') === 'atualizacao') throw new TypeError('Failed to fetch dynamically imported module: /assets/Tela.js')
  throw new Error('Erro provocado em /dev/erro para conferir a tela.')
}
