-- Снять служебный адрес с профиля инвестора.
--
-- ЗАЧЕМ. EnsureSigningKeysAreNotInvestorWalletsAsync (коммит 3cd9587) не даёт приложению
-- стартовать, пока адрес служебного ключа записан как кошелёк инвестора. На 2026-08-25 прод
-- лежал именно из-за этого: у инвестора +996556018138 в профиле стоял адрес анкорного агента.
--
-- ВАЖНО, В КАКУЮ СТОРОНУ ОШИБКА. Ключи на сервере служебные и всегда такими были — подменять
-- их не нужно. Неверна ЗАПИСЬ В БАЗЕ: инвестору проставили адрес, который платформа использует
-- для подписи. Поэтому скрипт правит базу, а не конфигурацию.
--
-- ЧТО ДЕЛАЕТ. Обнуляет WalletAddress в kyc_profiles и compliance_profiles ТОЛЬКО у строк с
-- этим адресом. Строки не удаляет: профиль остаётся, инвестор остаётся, снимается лишь
-- ошибочная привязка кошелька — её можно завести заново обычным путём, указав личный адрес.
--
-- ЧЕГО НЕ ДЕЛАЕТ. Не трогает whitelist_entries, holder_positions и investments: на момент
-- написания следов этого адреса там не было (0/0/0), а если появятся — их нельзя молча
-- переписывать, партия должна называть тот адрес, с которым ушла. Не трогает осиротевший
-- профиль compliance с чужим адресом — он проверке не мешает, чистка отдельным решением.
--
--   psql "$CONNECTION_STRING" -v addr=0x1e33c38838E4aB8F3d01f003A63D956Ed3d0D506 \
--        -f scripts/clear-signing-key-collision.sql
--
-- Транзакция: либо всё, либо ничего.

\if :{?addr}
\else
  \set addr '0x1e33c38838E4aB8F3d01f003A63D956Ed3d0D506'
\endif

-- 0. Страховка: если за адресом уже есть доли или заявки, значит он живёт своей жизнью и
--    молча снимать его нельзя. Проверка сделана средствами psql (\gset + \if), а не SQL:
--    в DO $$..$$ переменные psql не подставляются, а трюки вроде «уронить запрос на 1/0»
--    срабатывают вхолостую — константа вычисляется при планировании, до выбора ветки CASE.
--    Здесь же счёт сначала читается в переменную, и решение принимает клиент.
SELECT (
    (SELECT count(*) FROM whitelist_entries WHERE lower("WalletAddress") = lower(:'addr'))
  + (SELECT count(*) FROM holder_positions  WHERE lower("WalletAddress") = lower(:'addr'))
  + (SELECT count(*) FROM investments       WHERE lower("WalletAddress") = lower(:'addr'))
) AS used_count \gset

\if :used_count
  \echo 'ОТМЕНА: адрес встречается в whitelist_entries / holder_positions / investments.'
  \echo 'Эти строки нельзя переписывать молча — разберите их вручную.'
  \quit
\endif

\echo 'Следов в whitelist / holder_positions / investments нет — снимаем привязку.'

BEGIN;

-- 1. Что снимаем (для протокола в выводе).
SELECT 'до очистки' AS "этап", 'kyc' AS "таблица", u."PhoneNumber", k."WalletAddress"
FROM kyc_profiles k LEFT JOIN users u ON u."Id" = k."UserId"
WHERE lower(k."WalletAddress") = lower(:'addr')
UNION ALL
SELECT 'до очистки', 'compliance', u."PhoneNumber", c."WalletAddress"
FROM compliance_profiles c LEFT JOIN users u ON u."Id" = c."InvestorId"
WHERE lower(c."WalletAddress") = lower(:'addr');

-- 2. Снятие привязки. Строки профилей сохраняются.
UPDATE kyc_profiles
SET "WalletAddress" = NULL
WHERE lower("WalletAddress") = lower(:'addr');

UPDATE compliance_profiles
SET "WalletAddress" = NULL,
    "UpdatedAtUtc"  = now()
WHERE lower("WalletAddress") = lower(:'addr');

-- 3. Контроль: адреса не должно остаться ни в одной из таблиц, иначе приложение снова не встанет.
SELECT 'осталось строк с этим адресом: ' || (
         (SELECT count(*) FROM kyc_profiles        WHERE lower("WalletAddress") = lower(:'addr'))
       + (SELECT count(*) FROM compliance_profiles WHERE lower("WalletAddress") = lower(:'addr'))
       )::text AS "контроль";

COMMIT;
