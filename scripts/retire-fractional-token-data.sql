-- Разовая подготовка данных к переходу на ЦЕЛЫЕ токены.
--
-- ЗАЧЕМ. Токен на контракте неделим (decimals = 0), а в базе количество хранилось как decimal —
-- поэтому в заявках и в остатках пулов накопились дробные значения (0,35 доли; остаток пула
-- 56,47). Миграция MakeTokenCountsWhole меняет тип колонок на bigint и НАМЕРЕННО отказывается
-- работать, пока хоть одна дробь на месте: округлить их «на месте» нельзя — при floor каждой
-- строки пул перестаёт сходиться (57 = 56,47 свободных + 0,53 размещённых превращается в
-- 57 = 56 + 0, и одна доля исчезает без следа).
--
-- ЧТО ДЕЛАЕТ СКРИПТ (порядок из раздела 7 задания):
--   1. Заявки в резерве — отменяет (Cancelled) и возвращает доли в пул. Инвестор оформляет
--      заново по новым правилам: честнее, чем округлять уже согласованное с ним количество.
--   2. Активные заявки с дробным количеством — аннулирует (Annulled). Это ТЕСТОВЫЙ контур:
--      на бою такие заявки округлять нельзя, их разбирают руками.
--   3. Пулы объектов — пересчитывает остаток так, чтобы он сходился с целыми размещёнными.
--
-- ПРОВЕРИТЬ ПЕРЕД ЗАПУСКОМ. Скрипт трогает боевые сущности. Решение о его запуске — за Азимом.
-- Первый SELECT ничего не меняет: он показывает, что именно будет затронуто.
--
--   psql "$CONNECTION_STRING" -f scripts/retire-fractional-token-data.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- 0. Что найдено (только чтение).
SELECT 'investments' AS entity, i."Status", count(*) AS rows, sum(i."TokenCount") AS tokens
FROM investments i
WHERE i."TokenCount" <> trunc(i."TokenCount")
GROUP BY i."Status"
UNION ALL
SELECT 'properties', p."Status", count(*), sum(p."AvailableTokens")
FROM properties p
WHERE p."TotalTokens" <> trunc(p."TotalTokens")
   OR p."AvailableTokens" <> trunc(p."AvailableTokens")
GROUP BY p."Status";

-- 1. Заявки в резерве (Reserved = 0) — отменить все без исключения. Доли в пул здесь не
--    возвращаются: шаг 4 пересчитывает остаток от того, что осталось держать, поэтому
--    возврат по строкам был бы вторым источником правды о том же числе.
UPDATE investments
SET "Status" = 3,                                        -- Cancelled
    "RejectionReason" = 'Переход на целые токены: заявку нужно оформить заново'
WHERE "Status" = 0;

-- 2. Активные заявки с дробным количеством (Active = 1) — аннулировать. Целые активные
--    заявки остаются как есть: они уже выражены в том, что контракт умеет выпустить.
UPDATE investments
SET "Status" = 6,                                        -- Annulled
    "RejectionReason" = 'Переход на целые токены: дробное количество не может быть выпущено'
WHERE "Status" = 1 AND "TokenCount" <> trunc("TokenCount");

-- 3. Заявки в whitelist, потерявшие свою инвестицию, выводятся из очереди на минт.
UPDATE whitelist_entries w
SET "Status" = 4                                         -- Excluded
FROM investments i
WHERE i."Id" = w."InvestmentId"
  AND i."Status" IN (3, 6)                               -- Cancelled, Annulled
  AND w."Status" IN (0, 1);                              -- Pending, Ready

-- 4. Пулы объектов. Общий выпуск округляется ВНИЗ до целого, а остаток пересчитывается от него
--    минус то, что реально размещено целыми долями — так пул сходится по определению, а не по
--    совпадению. Отрицательный остаток невозможен: размещённое уже целое и не больше выпуска.
UPDATE properties p
SET "TotalTokens"     = trunc(p."TotalTokens"),
    "AvailableTokens" = greatest(
        trunc(p."TotalTokens") - coalesce((
            SELECT sum(i."TokenCount")
            FROM investments i
            WHERE i."PropertyId" = p."Id" AND i."Status" IN (0, 1)   -- Reserved, Active
        ), 0),
        0);

-- 5. Реестр держателей и снимки — производные от заявок; на тестовом контуре пересобираются
--    синхронизацией с сетью. Здесь только дробные позиции, которым больше нечего описывать.
DELETE FROM holder_positions WHERE "TokenCount" <> trunc("TokenCount");

-- 6. Контроль: после этого ни одна из проверяемых миграцией колонок не должна быть дробной.
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
