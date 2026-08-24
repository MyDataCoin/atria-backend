-- Дополнение к retire-fractional-token-data.sql.
--
-- ЗАЧЕМ. Первый скрипт выводил дробные заявки из оборота ПО СТАТУСУ: отменял, аннулировал,
-- исключал записи whitelist, пересчитывал пулы. Само число TokenCount он у них не трогал —
-- предполагалось, что закрытая заявка миграции не мешает. Это неверно: MakeTokenCountsWhole
-- проверяет колонку целиком, без оглядки на статус, и откажется работать, пока в ней есть
-- хоть одна дробь. Контрольная строка первого скрипта это и показала: «осталось дробных: 20».
--
-- ЧТО ДЕЛАЕТ. Округляет вниз TokenCount у заявок, уже выведенных из оборота (Cancelled = 3,
-- Annulled = 6), и у записей whitelist, уже исключённых из очереди (Excluded = 4).
--
-- ПОЧЕМУ ЭТО БЕЗОПАСНО. Ни одна из этих строк ни на что не влияет:
--   * в пулах объектов они не участвуют — шаг 4 первого скрипта считает остаток только по
--     Reserved и Active, поэтому округление здесь не может разойтись с остатком;
--   * на выпуск они не влияют — записи whitelist исключены, в партии на минт не попадут;
--   * активные заявки НЕ ТРОГАЮТСЯ — они уже целые (проверено: 8 заявок, 620 токенов).
-- Числа мелкие (0,01–0,35), floor превращает их в 0: это верно по смыслу, доли по этим
-- заявкам не выпущены и выпущены не будут.
--
-- Первый SELECT ничего не меняет: показывает, что будет затронуто.
--
--   psql "$CONNECTION_STRING" -f scripts/round-closed-token-counts.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- 0. Что найдено (только чтение).
SELECT 'investments' AS entity, "Status", count(*) AS rows, sum("TokenCount") AS tokens
FROM investments
WHERE "TokenCount" <> trunc("TokenCount")
GROUP BY "Status"
UNION ALL
SELECT 'whitelist_entries', "Status", count(*), sum("TokenCount")
FROM whitelist_entries
WHERE "TokenCount" <> trunc("TokenCount")
GROUP BY "Status"
ORDER BY 1, 2;

-- 1. Заявки, выведенные из оборота. Условие по статусу оставлено намеренно: если дробь
--    обнаружится у Reserved или Active, скрипт её НЕ тронет и контроль в конце это покажет —
--    такую строку округлять нельзя, она участвует в остатке пула.
UPDATE investments
SET "TokenCount" = trunc("TokenCount")
WHERE "Status" IN (3, 6)                                 -- Cancelled, Annulled
  AND "TokenCount" <> trunc("TokenCount");

-- 2. Записи whitelist, исключённые из очереди на минт.
UPDATE whitelist_entries
SET "TokenCount" = trunc("TokenCount")
WHERE "Status" = 4                                       -- Excluded
  AND "TokenCount" <> trunc("TokenCount");

-- 3. Контроль. Теперь по всем колонкам, которые проверяет миграция: 0 — можно накатывать,
--    любое другое число — разбираться, что осталось, и НЕ коммитить вслепую.
SELECT 'осталось дробных: ' || count(*)::text AS check
FROM (
    SELECT 1 FROM properties          WHERE "TotalTokens"     <> trunc("TotalTokens")
                                         OR "AvailableTokens" <> trunc("AvailableTokens")
    UNION ALL SELECT 1 FROM investments          WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM whitelist_entries    WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM mint_lists           WHERE "TotalTokens" <> trunc("TotalTokens")
    UNION ALL SELECT 1 FROM mint_list_items      WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM holder_positions     WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM holder_snapshots     WHERE "TotalTokens" <> trunc("TotalTokens")
    UNION ALL SELECT 1 FROM holder_snapshot_rows WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM payout_runs          WHERE "TotalTokens" <> trunc("TotalTokens")
    UNION ALL SELECT 1 FROM payout_items         WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM refund_obligations   WHERE "TokenCount"  <> trunc("TokenCount")
    UNION ALL SELECT 1 FROM travel_rule_messages WHERE "TokenCount"  <> trunc("TokenCount")
) x;

COMMIT;
