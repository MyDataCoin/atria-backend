-- Seed the credential-login accounts (SuperAdmin / Admin / Realtor / Accountant / Lawyer).
--
-- Login is DB-only: each account is an ordinary users row with a username + BCrypt password hash
-- (work factor 12, same as the app). They log in via POST /auth/admin/login (admin & super-admin)
-- or /auth/realtor/login with username + password. No config/secrets involved.
--
-- Default credentials (CHANGE the passwords for anything but local/demo — reset via the super-admin
-- flow or regenerate the hash and UPDATE):
--     superadmin / superadmin   (Role 4 = SuperAdmin)
--     admin      / admin        (Role 0 = Admin)
--     realtor    / realtor      (Role 3 = Realtor)
--     buhgalter  / buhgalter    (Role 5 = Finance)  -- бухгалтер УК
--     yurist     / yurist       (Role 7 = Auditor)  -- юрист УК, read-only по деньгам
--
-- The management company's two staff accounts reuse existing roles rather than adding new ones:
-- Finance already gates the operating periods and the payout run, and Auditor is read-only across
-- the platform, which is exactly what a lawyer needs and exactly what they must not exceed.
--
-- EF maps columns in PascalCase (no snake_case), so they are quoted. Idempotent per username.
--
-- The block below adds the columns from the SuperAdminBanAndPasswords + UserUsername migrations in
-- case the app hasn't applied them yet (no-ops when it has, e.g. Database__MigrateOnStartup=true).

ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsBanned"          boolean NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS "MustResetPassword" boolean NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS "PasswordHash"      character varying(200) NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS "Username"          character varying(64)  NULL;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_Username" ON users ("Username");

-- Record the migrations so EF does not try to re-apply them on the next MigrateOnStartup.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
    ('20260715081114_SuperAdminBanAndPasswords', '9.0.17'),
    ('20260717063858_UserUsername', '9.0.17')
ON CONFLICT ("MigrationId") DO NOTHING;

INSERT INTO users
    ("Id", "PhoneNumber", "Username", "Role", "IsActive", "IsPhoneVerified",
     "IsBanned", "PasswordHash", "MustResetPassword", "CreatedAtUtc")
VALUES
    (gen_random_uuid(), NULL, 'superadmin', 4, TRUE, FALSE, FALSE,
     '$2a$12$Pmdi1XXn89zKlycBbfpR0uhP349PhsVX/C1WMa/rQgJLwocCOOHUq', FALSE, NOW() AT TIME ZONE 'utc'),
    (gen_random_uuid(), NULL, 'admin', 0, TRUE, FALSE, FALSE,
     '$2a$12$XWJV2dhQWahva8iEjr2dKu3lrDUCNFRb6vGqEI3uxyvV3O1eB0mmi', FALSE, NOW() AT TIME ZONE 'utc'),
    (gen_random_uuid(), NULL, 'realtor', 3, TRUE, FALSE, FALSE,
     '$2a$12$9Ft3rwcoDlhT72geIZ/E1OvDRFtglw/pbaXaAMGaeWVfacujnLFzm', FALSE, NOW() AT TIME ZONE 'utc'),
    (gen_random_uuid(), NULL, 'buhgalter', 5, TRUE, FALSE, FALSE,
     '$2a$12$kIU2mbTiVq.1lscjKtEyueT.tm/2PJ1/n0W6vXaM9IIv/DicxXdB.', FALSE, NOW() AT TIME ZONE 'utc'),
    (gen_random_uuid(), NULL, 'yurist', 7, TRUE, FALSE, FALSE,
     '$2a$12$4Ix99Nc0vQnID9Ili7zABeOhamvu80EyGtjMjTOZOw2TxITYiBQ9.', FALSE, NOW() AT TIME ZONE 'utc')
ON CONFLICT ("Username") DO NOTHING;
