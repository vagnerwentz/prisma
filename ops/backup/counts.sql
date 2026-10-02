-- Contagem exata de linhas de cada tabela do app, uma por linha ("tabela quantidade").
-- O backup a registra no log e a restauração a repete: as duas listas devem bater.
-- Só números: nenhum dado sai daqui.
SELECT c.relname,
       (xpath('/row/n/text()',
              query_to_xml(format('SELECT count(*) AS n FROM public.%I', c.relname), false, true, '')))[1]::text
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'r'
ORDER BY c.relname;
