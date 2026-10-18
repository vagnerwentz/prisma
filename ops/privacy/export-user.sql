-- Cópia dos dados de uma pessoa, em JSON (LGPD, art. 18, II e V; docs/operacao.md, seção 9). Atende o pedido de cópia
-- feito pelo contato@ enquanto o "Baixar meus dados" (A.3b) não existe.
--
-- Rode como prisma_backup (só lê), com o TCP Proxy ligado só durante isso:
--   psql "<conexão do prisma_backup>" -v user_id=<id da conta> -At -f ops/privacy/export-user.sql > copia.json
-- O id sai de: SELECT id FROM users WHERE normalized_email = upper('<e-mail>');
--
-- Vai só o que a pessoa vê no app: nada excluído (o "Desfazer" apaga de vez em 30 minutos), nada de segurança da
-- conta (hash da senha, carimbo de segurança, tentativas). Valores em centavos, como o app guarda; datas em ISO.
-- Tabela nova com user_id entra aqui também (o AccountEraser e a limpeza têm teste; esta consulta não).
SELECT jsonb_pretty(jsonb_build_object(
  'geradoEm', now(),
  'conta', (
    SELECT jsonb_build_object(
      'email', u.email,
      'criadaEm', u.created_at,
      'emailConfirmado', u.email_confirmed,
      'versaoDosTermosAceita', u.terms_version,
      'versaoDaPoliticaAceita', u.privacy_version,
      'aceiteEm', u.terms_accepted_at)
    FROM users u WHERE u.id = :'user_id'),
  'contas', (
    SELECT coalesce(jsonb_agg(to_jsonb(a) - 'user_id' - 'deleted_at' ORDER BY a.created_at), '[]')
    FROM accounts a WHERE a.user_id = :'user_id' AND a.deleted_at IS NULL),
  'categorias', (
    SELECT coalesce(jsonb_agg(to_jsonb(c) - 'user_id' - 'deleted_at' - 'template_key' ORDER BY c.created_at), '[]')
    FROM categories c WHERE c.user_id = :'user_id' AND c.deleted_at IS NULL),
  'faturas', (
    SELECT coalesce(jsonb_agg(to_jsonb(s) - 'user_id' - 'deleted_at' ORDER BY s.due_date), '[]')
    FROM statements s WHERE s.user_id = :'user_id' AND s.deleted_at IS NULL),
  'lancamentos', (
    SELECT coalesce(jsonb_agg(to_jsonb(t) - 'user_id' - 'deleted_at' ORDER BY t.purchase_date, t.created_at), '[]')
    FROM transactions t WHERE t.user_id = :'user_id' AND t.deleted_at IS NULL),
  'comprasParceladas', (
    SELECT coalesce(jsonb_agg(to_jsonb(p) - 'user_id' - 'deleted_at' ORDER BY p.purchase_date), '[]')
    FROM installment_purchases p WHERE p.user_id = :'user_id' AND p.deleted_at IS NULL),
  'recorrencias', (
    SELECT coalesce(jsonb_agg(to_jsonb(r) - 'user_id' - 'deleted_at' ORDER BY r.start_date), '[]')
    FROM recurrences r WHERE r.user_id = :'user_id' AND r.deleted_at IS NULL),
  'recorrenciasPendentes', (
    SELECT coalesce(jsonb_agg(to_jsonb(p) - 'user_id' - 'deleted_at' ORDER BY p.occurrence_date), '[]')
    FROM recurrence_pendings p WHERE p.user_id = :'user_id' AND p.deleted_at IS NULL),
  'carteira', (
    SELECT coalesce(jsonb_agg(jsonb_build_object('ativo', a.symbol, 'nome', a.name, 'desde', h.created_at) ORDER BY a.symbol), '[]')
    FROM holdings h JOIN assets a ON a.id = h.asset_id WHERE h.user_id = :'user_id' AND h.deleted_at IS NULL),
  'notasDeCorretagem', (
    SELECT coalesce(jsonb_agg(
      to_jsonb(n) - 'user_id' - 'deleted_at' || jsonb_build_object('operacoes', (
        SELECT coalesce(jsonb_agg(to_jsonb(tr) - 'trade_note_id' || jsonb_build_object('ativo', a.symbol) ORDER BY tr.line_number), '[]')
        FROM trades tr JOIN assets a ON a.id = tr.asset_id WHERE tr.trade_note_id = n.id))
      ORDER BY n.trade_date), '[]')
    FROM trade_notes n WHERE n.user_id = :'user_id' AND n.deleted_at IS NULL),
  'avisosDeAtivoFaltando', (
    SELECT coalesce(jsonb_agg(jsonb_build_object('codigo', r.symbol, 'pedidoEm', r.created_at) ORDER BY r.created_at), '[]')
    FROM asset_requests r WHERE r.user_id = :'user_id' AND r.deleted_at IS NULL),
  'conferenciasDeEventos', (
    SELECT coalesce(jsonb_agg(to_jsonb(e) - 'user_id' - 'deleted_at' ORDER BY e.created_at), '[]')
    FROM event_reviews e WHERE e.user_id = :'user_id' AND e.deleted_at IS NULL),
  'objetivos', (
    SELECT coalesce(jsonb_agg(to_jsonb(g) - 'user_id' - 'deleted_at' - 'xmin' ORDER BY g.name), '[]')
    FROM savings_goals g WHERE g.user_id = :'user_id' AND g.deleted_at IS NULL),
  'aportesRendaFixa', (
    SELECT coalesce(jsonb_agg(to_jsonb(d) - 'user_id' - 'deleted_at' - 'xmin' - 'client_request_id' ORDER BY d.deposit_date, d.created_at), '[]')
    FROM fixed_income_deposits d WHERE d.user_id = :'user_id' AND d.deleted_at IS NULL)
));
