-- Выгрузка того, что осталось незаписанным в atria-contracts/deployments/README.md по ATRP-BR1:
-- адреса получателей и хеши транзакций белого списка и минта.
--
-- Зачем из БД, а не из сети: публичные узлы BNB testnet хранят историю около двух суток, оба выпуска
-- (24 августа и 3 сентября 2026) старше — eth_getLogs и исторические вызовы состояния отвечают
-- "history has been pruned", архивного состояния публичные RPC не отдают, ключа BscScan нет.
-- Бэкенд записал эти данные в момент операции, и сейчас это единственный их источник.
--
-- Объект: Borsan Residence, b6c59d6c-3b03-495c-a34f-f73cabb9d488.
-- Ожидаемый итог: 108 долей суммарно — 100 на контракте 0x440bc3b4…46c088 и 8 на 0x263a28a4…a04c78.
--
-- Запуск:
--   psql "$POSTGRES_CONNECTION" -f scripts/atrp-br1-issuance-report.sql
--
-- Проверен на схеме, поднятой `dotnet ef database update` из этого репозитория: все пять запросов
-- выполнены с ON_ERROR_STOP=1, затем повторены на подставных строках — два минта на разных
-- контрактах, заявка без выхода в сеть, запись белого списка и две операции в очереди. Запросы 1-2
-- правильно отбрасывают заявку с OnChainStatus = 0 и раскладывают доли по контрактам; запрос 4
-- находит и хеш белого списка (по propertyId в payload), и хеш минта (по адресу контракта).
-- На боевых данных не запускался — доступа к боевой БД у автора скрипта не было.

\set propertyId 'b6c59d6c-3b03-495c-a34f-f73cabb9d488'

\echo '=== 1. Минты: получатель, количество, хеш, контракт ==='

-- Разложено по TokenContractAddress: у объекта два контракта, и суммы по ним должны сойтись с
-- totalSupply() каждого (100 и 8). Если не сходятся — расхождение реестра с сетью, а не опечатка.
SELECT
    i."WalletAddress"          AS holder_address,
    i."TokenContractAddress"   AS token_contract,
    i."TokenCount"             AS shares,
    i."TransactionHash"        AS mint_tx,
    i."OnChainStatus"          AS onchain_status,
    i."ActivatedAtUtc"         AS activated_utc,
    i."Status"                 AS investment_status,
    i."Id"                     AS investment_id
FROM investments i
WHERE i."PropertyId" = :'propertyId'::uuid
  AND i."OnChainStatus" <> 0          -- 0 = None: до сети не дошло
ORDER BY i."ActivatedAtUtc";

\echo '=== 2. Сверка с сетью: сумма долей по каждому контракту ==='

SELECT
    i."TokenContractAddress"  AS token_contract,
    SUM(i."TokenCount")       AS shares_in_registry,
    COUNT(*)                  AS mints
FROM investments i
WHERE i."PropertyId" = :'propertyId'::uuid
  AND i."OnChainStatus" <> 0
GROUP BY i."TokenContractAddress"
ORDER BY 1;

\echo '=== 3. Белый список: адрес и статус ==='

-- WalletAddress здесь — снимок на момент заявки. Он и есть адрес, на который ушёл минт, даже если
-- инвестор потом сменил кошелёк в профиле.
SELECT
    w."WalletAddress"    AS holder_address,
    w."TokenCount"       AS shares,
    w."Status"           AS whitelist_status,
    w."RequestedAtUtc"   AS requested_utc,
    w."ApprovedAtUtc"    AS approved_utc,
    w."MintedAtUtc"      AS minted_utc,
    w."MintListId"       AS mint_list_id,
    w."InvestmentId"     AS investment_id
FROM whitelist_entries w
WHERE w."PropertyId" = :'propertyId'::uuid
ORDER BY w."RequestedAtUtc";

\echo '=== 4. Транзакции белого списка и минта из очереди операций ==='

-- Хеш транзакции добавления в белый список живёт только здесь: в investments пишется хеш минта.
-- Payload содержит адрес и контракт, поэтому фильтруем по нему.
SELECT
    o."Type"           AS operation_type,
    o."TransactionRef" AS tx_hash,
    o."Status"         AS status,
    o."ConfirmedAtUtc" AS confirmed_utc,
    o."Payload"        AS payload
FROM blockchain_operations o
WHERE o."Payload" LIKE '%' || :'propertyId' || '%'
   OR o."Payload" LIKE '%0x440bc3b478d6d5c18ec537431e7C3e602E46c088%'
   OR o."Payload" LIKE '%0x263a28a498c35e9396a171cf11aafb9fa9a04c78%'
ORDER BY o."CreatedAtUtc";

\echo '=== 5. Какой контракт бэкенд считает действующим ==='

-- Ради этого стоит запустить отчёт даже отдельно: в README два контракта пока равноправны, а
-- Property.TokenContractAddress называет ровно один. Второй после этого помечается устаревшим.
SELECT
    p."Name"                  AS property_name,
    p."TokenContractAddress"  AS current_contract,
    p."TokenChain"            AS chain,
    p."IssuerWalletAddress"   AS issuer_wallet,
    p."TokenDeploymentBlock"  AS deployment_block
FROM properties p
WHERE p."Id" = :'propertyId'::uuid;
