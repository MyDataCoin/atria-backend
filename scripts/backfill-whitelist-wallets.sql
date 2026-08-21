-- Разовый бэкфилл: проставить адрес кошелька заявкам, которые остались без него.
--
-- ПОЧЕМУ ОНИ ПУСТЫЕ. Кошелёк по продуктовому сценарию привязывается ПОСЛЕ прохождения KYC
-- (PATCH /kyc/wallet), а compliance-профиль снимал копию адреса в момент ОДОБРЕНИЯ KYC —
-- когда адреса ещё не было. Привязка позже никуда не распространялась: у профиля не было
-- способа получить адрес, а заявки в whitelist копируют его именно оттуда. Итог — «нет
-- кошелька» в очереди оператора навсегда, хотя в kyc_profiles адрес лежит.
--
-- Код это чинит для будущих привязок (KycWalletLinkedEvent), но уже накопленные строки
-- событие не тронет — их закрывает этот скрипт. Запускать ОДИН раз после деплоя.
--
--   psql "$CONNECTION_STRING" -f scripts/backfill-whitelist-wallets.sql
--
-- Транзакция: либо оба шага, либо ни одного.

BEGIN;

-- Сначала посмотреть, что будет затронуто (ничего не меняет).
SELECT
    w."Id"            AS entry_id,
    w."InvestorId",
    w."Status",
    k."WalletAddress" AS wallet_from_kyc
FROM whitelist_entries w
JOIN kyc_profiles k ON k."UserId" = w."InvestorId"
WHERE w."WalletAddress" IS NULL
  AND k."WalletAddress" IS NOT NULL
  AND w."Status" IN (0, 1);   -- Pending, Ready

-- 1. Compliance-профиль — источник, из которого читают все остальные модули.
UPDATE compliance_profiles c
SET    "WalletAddress" = k."WalletAddress"
FROM   kyc_profiles k
WHERE  k."UserId" = c."InvestorId"
  AND  c."WalletAddress" IS NULL
  AND  k."WalletAddress" IS NOT NULL;

-- 2. Заявки в очереди. ТОЛЬКО Pending (0) и Ready (1): заявка, уже отданная бирже
--    (Batched=2, Minted=3), обязана сохранить тот адрес, с которым её отправили, иначе
--    вернувшийся батч не сойдётся с отправленным. Excluded (4) не минтится вовсе.
UPDATE whitelist_entries w
SET    "WalletAddress" = k."WalletAddress"
FROM   kyc_profiles k
WHERE  k."UserId" = w."InvestorId"
  AND  w."WalletAddress" IS NULL
  AND  k."WalletAddress" IS NOT NULL
  AND  w."Status" IN (0, 1);

COMMIT;

-- Проверка: должно вернуть 0 строк.
SELECT count(*) AS still_without_wallet
FROM whitelist_entries w
JOIN kyc_profiles k ON k."UserId" = w."InvestorId"
WHERE w."WalletAddress" IS NULL
  AND k."WalletAddress" IS NOT NULL
  AND w."Status" IN (0, 1);
