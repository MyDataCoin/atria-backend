-- Вернуть в очередь сжигание, упавшее до того, как оно вообще было реализовано.
--
-- ЗАЧЕМ. Операция TokenBurn не разбиралась воркером: он знал три типа, а всё остальное отправлял
-- кастодиальному подписанту, которого на тестовой сети нет. Отказ инвестора при этом проходил
-- наполовину — доли возвращались в пул, обязательство возврата создавалось, реестр очищался, —
-- а на цепи доли оставались. Те же доли можно продать второй раз.
--
-- Теперь у воркера есть ветка для TokenBurn и ключ COMPLIANCE_ROLE. Упавшая операция об этом не
-- узнает: воркер берёт только Pending, а она в Failed с исчерпанными попытками.
--
-- ЧТО ДЕЛАЕТ. Возвращает в очередь операции сжигания, которые упали, НЕ успев отправить
-- транзакцию, и относятся к отозванной или аннулированной заявке.
--
-- ПОЧЕМУ ИМЕННО ТАК. У TokenBurn в нагрузке нет investmentId — только propertyId и holder, поэтому
-- общий scripts/requeue-failed-operations.sql её не находит. Принадлежность заявке определяется
-- по адресу держателя и объекту: сжигать имеет смысл только то, от чего инвестор отказался.
--
-- ЧЕГО НЕ ДЕЛАЕТ. Не трогает операции с TransactionRef — отправленное повторять нельзя, иначе
-- доли сгорят дважды. Не трогает сжигание по живой заявке: её никто не отзывал.
--
-- ВАЖНО ПЕРЕД ЗАПУСКОМ. Убедиться, что развёрнута версия с веткой TokenBurn в
-- BlockchainOperationWorker и что задан Blockchain:TokenSigning:CompliancePrivateKey. Иначе
-- операция просто упадёт снова.
--
--   psql "$CONNECTION_STRING" -f scripts/requeue-failed-burn.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- 0. Что найдено и по какой заявке (только чтение).
SELECT o."Id",
       o."Payload"::jsonb ->> 'holder'  AS "держатель",
       o."Payload"::jsonb ->> 'amount'  AS "долей",
       o."Payload"::jsonb ->> 'reason'  AS "причина",
       o."Attempts",
       left(coalesce(o."Error", '—'), 60) AS "ошибка"
FROM blockchain_operations o
WHERE o."Type" = 6                                       -- TokenBurn
  AND o."Status" = 3                                     -- Failed
  AND (o."TransactionRef" IS NULL OR o."TransactionRef" = '');

-- 1. Возврат в очередь. Условие по заявке оставлено намеренно: сжигание имеет смысл только там,
--    где инвестор действительно отказался (Withdrawn = 5) или заявку аннулировали (Annulled = 6).
UPDATE blockchain_operations o
SET "Status" = 0,                                        -- Pending
    "Attempts" = 0,
    "Error" = NULL
WHERE o."Type" = 6
  AND o."Status" = 3
  AND (o."TransactionRef" IS NULL OR o."TransactionRef" = '')
  AND EXISTS (
        SELECT 1
        FROM investments i
        WHERE i."PropertyId" = (o."Payload"::jsonb ->> 'propertyId')::uuid
          AND i."Status" IN (5, 6)                       -- Withdrawn, Annulled
      );

-- 2. Контроль.
SELECT 'сжиганий в очереди: ' || count(*)::text AS check
FROM blockchain_operations WHERE "Type" = 6 AND "Status" = 0;

COMMIT;
