-- Вернуть в очередь операции, упавшие по уже устранённой причине.
--
-- ЗАЧЕМ. Воркер берёт из очереди только строки в статусе Pending. Операция, исчерпавшая попытки,
-- остаётся Failed навсегда: причину устранили, а сама она об этом не узнает. После замены ключа
-- агента и переключения режима подписи (25.08.2026) в очереди осталось четыре таких строки.
--
-- ЧТО ДЕЛАЕТ. Переводит в Pending и обнуляет счётчик попыток ТОЛЬКО у операций, относящихся
-- к заявкам, которые всё ещё живы (Reserved = 0, Active = 1).
--
-- ПОЧЕМУ НЕ ВСЕ ПОДРЯД. Операции аннулированных заявок оживлять нельзя: доли по ним возвращены
-- в пул объекта, и выпуск против такой операции создал бы доли, за которыми не стоит заявка —
-- ровно то расхождение, ради которого весь этот прогон и затевался. Их место в истории.
--
-- ЧЕГО НЕ ДЕЛАЕТ. Не трогает строки с TransactionRef: транзакция уже ушла в сеть, её судьбу
-- решает подтверждение, а не повторная отправка. Повторить отправленное — это выпустить доли
-- дважды.
--
-- ПРИНАДЛЕЖНОСТЬ ЗАЯВКЕ. У TokenAllocation идентификатор заявки лежит в Payload (поле
-- investmentId), у AllowlistAdd его нет вовсе — там только сеть и адрес. Поэтому AllowlistAdd
-- сопоставляется с заявкой по адресу кошелька в записи whitelist: это тот же адрес, на который
-- пойдёт минт.
--
--   psql "$CONNECTION_STRING" -f scripts/requeue-failed-operations.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- Строки, которые будут возвращены в очередь: упавшие, не отправленные, за живой заявкой.
CREATE TEMP TABLE to_requeue ON COMMIT DROP AS
SELECT o."Id"
FROM blockchain_operations o
WHERE o."Status" = 3                                     -- Failed
  AND (o."TransactionRef" IS NULL OR o."TransactionRef" = '')
  AND (
        -- Выпуск долей: заявка названа в полезной нагрузке.
        (o."Type" = 2 AND EXISTS (
            SELECT 1 FROM investments i
            WHERE i."Id" = (o."Payload"::jsonb ->> 'investmentId')::uuid
              AND i."Status" IN (0, 1)))                 -- Reserved, Active
        OR
        -- Добавление в белый список: заявки в нагрузке нет, сопоставляем по адресу.
        (o."Type" = 0 AND EXISTS (
            SELECT 1
            FROM whitelist_entries w
            JOIN investments i ON i."Id" = w."InvestmentId"
            WHERE lower(w."WalletAddress") = lower(o."Payload"::jsonb ->> 'walletAddress')
              AND i."Status" IN (0, 1)))
      );

-- 0. Что будет возвращено и что останется (только чтение).
SELECT 'вернуть в очередь' AS "действие", o."Type", o."IdempotencyKey", o."CreatedAtUtc"
FROM blockchain_operations o
WHERE o."Id" IN (SELECT "Id" FROM to_requeue)
UNION ALL
SELECT 'оставить как есть', o."Type", o."IdempotencyKey", o."CreatedAtUtc"
FROM blockchain_operations o
WHERE o."Status" = 3
  AND o."Id" NOT IN (SELECT "Id" FROM to_requeue)
ORDER BY 1, 4;

-- 1. Возврат в очередь. Счётчик попыток обнуляется: причина была внешней и устранена,
--    прежние неудачи о новой попытке ничего не говорят.
UPDATE blockchain_operations
SET "Status" = 0,                                        -- Pending
    "Attempts" = 0,
    "Error" = NULL
WHERE "Id" IN (SELECT "Id" FROM to_requeue);

-- 2. Контроль.
SELECT 'возвращено в очередь: ' || count(*)::text AS check
FROM blockchain_operations WHERE "Status" = 0;

COMMIT;
