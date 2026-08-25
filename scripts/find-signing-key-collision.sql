-- Кто держит адрес, которым подписывает платформа.
--
-- ЗАЧЕМ. EnsureSigningKeysAreNotInvestorWalletsAsync отказывается стартовать, когда адрес
-- служебного ключа найден в kyc_profiles или compliance_profiles, и называет в сообщении
-- только адрес. Этого мало, чтобы решить, что чинить: ключ в .env или запись в базе.
--
-- ЧТО ПОКАЗЫВАЕТ. Все строки с этим адресом в обеих таблицах и то, живой ли за ними инвестор.
-- Если профиль ОСИРОТЕВШИЙ (InvestorId отсутствует в users) — это мусор от тестового прогона,
-- и чинить надо базу, а не ключ. Если за адресом стоит настоящий пользователь с телефоном и
-- заявками — адрес действительно инвесторский, и менять надо служебный ключ.
--
-- Только чтение, ничего не меняет.
--
--   psql "$CONNECTION_STRING" -v addr=0x1e33c38838E4aB8F3d01f003A63D956Ed3d0D506 \
--        -f scripts/find-signing-key-collision.sql

\if :{?addr}
\else
  \set addr '0x1e33c38838E4aB8F3d01f003A63D956Ed3d0D506'
\endif

\echo '=== 1. compliance_profiles ==='
SELECT c."Id",
       c."InvestorId",
       c."WalletAddress",
       CASE WHEN u."Id" IS NULL THEN 'ОСИРОТЕВШИЙ — инвестора нет в users'
            ELSE 'живой инвестор: ' || coalesce(u."PhoneNumber", '(без телефона)')
       END AS "чей"
FROM compliance_profiles c
LEFT JOIN users u ON u."Id" = c."InvestorId"
WHERE lower(c."WalletAddress") = lower(:'addr');

\echo '=== 2. kyc_profiles ==='
SELECT k."Id",
       k."UserId",
       k."WalletAddress",
       CASE WHEN u."Id" IS NULL THEN 'ОСИРОТЕВШИЙ — пользователя нет в users'
            ELSE 'живой пользователь: ' || coalesce(u."PhoneNumber", '(без телефона)')
       END AS "чей"
FROM kyc_profiles k
LEFT JOIN users u ON u."Id" = k."UserId"
WHERE lower(k."WalletAddress") = lower(:'addr');

\echo '=== 3. следы адреса в остальной системе ==='
-- Если здесь пусто, за адресом нет ни долей, ни заявок — лишний довод, что это мусор.
SELECT 'whitelist_entries' AS "таблица", count(*) AS "строк"
  FROM whitelist_entries WHERE lower("WalletAddress") = lower(:'addr')
UNION ALL SELECT 'holder_positions', count(*)
  FROM holder_positions WHERE lower("WalletAddress") = lower(:'addr')
UNION ALL SELECT 'investments', count(*)
  FROM investments WHERE lower("WalletAddress") = lower(:'addr');
