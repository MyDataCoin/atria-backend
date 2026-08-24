-- Разовый перенос кошельков из KYC в профили compliance.
--
-- ЗАЧЕМ. Кошелёк собирается ПОСЛЕ прохождения KYC и пишется в kyc_profiles. Оттуда его должен
-- переносить обработчик BackfillWalletOnKycWalletLinkedHandler — но он появился 21 августа
-- (коммит c05b62d), а адреса, привязанные раньше, так и остались только в kyc_profiles.
--
-- ЧЕМ ЭТО ПЛОХО. Цепочка выпуска читает compliance_profiles.WalletAddress
-- (AddToAllowlistOnInvestmentActivatedHandler). Не найдя адреса, она пишет предупреждение в лог
-- и молча выходит: заявка остаётся одобренной, доли из пула списаны, а в очереди операций пусто.
-- Снаружи выглядит успешно — админка подставляет адрес из реестра инвесторов и помечает его
-- «из профиля», поэтому оператор видит «Готов к минту» там, где минта не будет.
--
-- ЧТО ДЕЛАЕТ СКРИПТ. Ровно то же, что сделал бы обработчик:
--   1. проставляет адрес в профиль compliance, ТОЛЬКО если он там пуст
--      (домен разрешает лишь это — см. ComplianceProfile.SetWalletIfMissing);
--   2. дозаполняет адрес в записях whitelist, ещё ждущих его (Pending = 0, Ready = 1).
-- Записи, уже переданные бирже (Batched, Minted), не трогаются: партия должна называть тот
-- адрес, с которым ушла, иначе то, что вернётся, не сойдётся.
--
-- ЧЕГО СКРИПТ НЕ ДЕЛАЕТ. Не ставит операции в очередь. Событие активации по уже одобренной
-- заявке отгремело, задним числом его никто не повторит: такую заявку нужно аннулировать
-- и подать заново.
--
-- ОСИРОТЕВШИЕ ПРОФИЛИ. В базе есть строки compliance_profiles, чей InvestorId не существует
-- в users (в том числе одна с этим же адресом). Скрипт их НЕ удаляет — сначала решение,
-- потом чистка. Шаг 0 их показывает.
--
--   psql "$CONNECTION_STRING" -f scripts/backfill-compliance-wallets.sql
--
-- Транзакция: либо всё, либо ничего.

BEGIN;

-- 0. Что найдено (только чтение).
SELECT 'адрес есть в KYC, но не в compliance' AS "что", u."PhoneNumber", k."WalletAddress"
FROM kyc_profiles k
JOIN users u ON u."Id" = k."UserId"
JOIN compliance_profiles c ON c."InvestorId" = k."UserId"
WHERE k."WalletAddress" IS NOT NULL
  AND (c."WalletAddress" IS NULL OR c."WalletAddress" = '')
UNION ALL
SELECT 'осиротевший профиль compliance', c."InvestorId"::text, coalesce(c."WalletAddress", '—')
FROM compliance_profiles c
WHERE NOT EXISTS (SELECT 1 FROM users u WHERE u."Id" = c."InvestorId");

-- 1. Профили compliance: заполняем только пустое.
UPDATE compliance_profiles c
SET "WalletAddress" = k."WalletAddress",
    "UpdatedAtUtc"  = now()
FROM kyc_profiles k
WHERE k."UserId" = c."InvestorId"
  AND k."WalletAddress" IS NOT NULL
  AND (c."WalletAddress" IS NULL OR c."WalletAddress" = '');

-- 2. Записи whitelist, ещё ждущие адреса. Batched и Minted не трогаем.
UPDATE whitelist_entries w
SET "WalletAddress" = k."WalletAddress"
FROM kyc_profiles k
WHERE k."UserId" = w."InvestorId"
  AND k."WalletAddress" IS NOT NULL
  AND (w."WalletAddress" IS NULL OR w."WalletAddress" = '')
  AND w."Status" IN (0, 1);                              -- Pending, Ready

-- 3. Контроль: расхождений между KYC и compliance остаться не должно.
SELECT 'осталось расхождений: ' || count(*)::text AS check
FROM kyc_profiles k
JOIN compliance_profiles c ON c."InvestorId" = k."UserId"
WHERE k."WalletAddress" IS NOT NULL
  AND (c."WalletAddress" IS NULL OR c."WalletAddress" = '');

COMMIT;
