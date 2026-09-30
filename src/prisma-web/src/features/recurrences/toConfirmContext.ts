import { createContext, useContext } from 'react'

// Abre o painel "Para conferir" de qualquer tela: o sino no cabeçalho e a linha do Resumo (docs/fase-2.md, 2.15).
export const OpenToConfirmContext = createContext<() => void>(() => {})

export const useOpenToConfirm = () => useContext(OpenToConfirmContext)
