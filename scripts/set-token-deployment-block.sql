-- Проставить блок развёртывания токена уже привязанному выпуску.
--
-- ЗАЧЕМ. Синхронизация реестра проигрывает события Transfer окнами по 5 000 блоков, начиная с
-- курсора. У выпуска, привязанного до появления поля TokenDeploymentBlock, курсора нет, и первый
-- запуск начинал с блока 0 — то есть с начала истории ВСЕЙ цепи, а не этого контракта. В BSC
-- testnet это 127 миллионов блоков: свыше 25 000 вызовов по диапазонам, где контракта ещё не
-- существовало, причём самые старые узел просто отказывается отдавать. Отсюда 500 на
-- POST /holders/sync.
--
-- ЧТО ДЕЛАЕТ. Записывает блок развёртывания объекту Borsan Residence. Дальше первый запуск
-- синхронизации откроет окно прямо на контракте.
--
-- ОТКУДА ЗНАЧЕНИЕ. Из чека транзакции развёртывания в atria-contracts:
-- broadcast/Deploy.s.sol/97/run-latest.json, транзакция типа CREATE,
-- 0x1c870ea227ec7a4fec882ca81a50645b9506cfbbcddb3cbcb012af41642ca19c → блок 126927728.
--
-- ЧЕГО НЕ ДЕЛАЕТ. Не трогает объекты без привязанного контракта и не переписывает уже
-- проставленный блок: он неизменен по смыслу, а расхождение с реальностью лучше увидеть,
-- чем молча исправить.
--
-- ПОСЛЕ ЗАПУСКА. Если синхронизация уже создавала курсор, его надо удалить — иначе она
-- продолжит с того места, где остановилась. Проверка на это есть ниже.
--
--   psql "$CONNECTION_STRING" -f scripts/set-token-deployment-block.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- 0. Что найдено (только чтение).
SELECT p."Name",
       p."TokenContractAddress",
       p."TokenChain",
       coalesce(p."TokenDeploymentBlock"::text, '— не задан') AS "блок развёртывания",
       (SELECT count(*) FROM chain_sync_cursors c WHERE c."PropertyId" = p."Id") AS "курсоров"
FROM properties p
WHERE p."TokenContractAddress" IS NOT NULL;

-- 1. Borsan Residence — единственный выпуск с развёрнутым контрактом на 25.08.2026.
UPDATE properties
SET "TokenDeploymentBlock" = 126927728
WHERE "Id" = 'b6c59d6c-3b03-495c-a34f-f73cabb9d488'
  AND lower("TokenContractAddress") = lower('0x440bc3b478d6d5c18ec537431e7C3e602E46c088')
  AND "TokenDeploymentBlock" IS NULL;

-- 2. Курсор, созданный неудачным запуском, начинается с нуля и обесценит правку выше.
--    Удаляем только курсоры, стоящие ДО блока развёртывания: продвинувшийся курсор означает,
--    что переводы уже применены, и сбрасывать его нельзя — они применились бы дважды.
DELETE FROM chain_sync_cursors c
USING properties p
WHERE p."Id" = c."PropertyId"
  AND p."TokenDeploymentBlock" IS NOT NULL
  AND c."LastProcessedBlock" < p."TokenDeploymentBlock";

-- 3. Контроль.
SELECT p."Name",
       p."TokenDeploymentBlock",
       (SELECT count(*) FROM chain_sync_cursors c WHERE c."PropertyId" = p."Id") AS "курсоров осталось"
FROM properties p
WHERE p."TokenContractAddress" IS NOT NULL;

COMMIT;
