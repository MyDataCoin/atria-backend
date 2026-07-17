-- Seed a SuperAdmin account.
--
-- Password: superadmin   (BCrypt, work factor 12 — same as the app's BcryptPasswordHasher)
-- Role: 4 = SuperAdmin (Atria.Domain.Users.Role)
--
-- The "Id" here MUST equal SuperAdmin__UserId / SUPERADMIN_USER_ID in the app config, so the
-- issued JWT (sub = that id) maps to this row and ban/password operations target it.
--
-- EF maps columns in PascalCase (no snake_case), so they are quoted. Idempotent: re-running does
-- nothing if the id already exists. Change the password afterwards via the super-admin reset flow,
-- or regenerate the hash and UPDATE "PasswordHash".
--
-- The block below adds the columns from the SuperAdminBanAndPasswords migration in case the app
-- hasn't applied it yet. If you run the app with Database__MigrateOnStartup=true these already
-- exist and the ADD COLUMN IF NOT EXISTS statements are no-ops.

ALTER TABLE users ADD COLUMN IF NOT EXISTS "IsBanned"          boolean NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS "MustResetPassword" boolean NOT NULL DEFAULT false;
ALTER TABLE users ADD COLUMN IF NOT EXISTS "PasswordHash"      character varying(200) NULL;

-- Record the migration so EF does not try to re-apply it on the next MigrateOnStartup.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260715081114_SuperAdminBanAndPasswords', '9.0.17')
ON CONFLICT ("MigrationId") DO NOTHING;

INSERT INTO users
    ("Id", "PhoneNumber", "Role", "IsActive", "IsPhoneVerified",
     "IsBanned", "PasswordHash", "MustResetPassword", "CreatedAtUtc")
VALUES
    ('44444444-4444-4444-4444-444444444444',           -- MUST match SUPERADMIN_USER_ID
     NULL,                                              -- no phone (credential login, not OTP)
     4,                                                 -- Role.SuperAdmin
     TRUE,                                              -- IsActive
     FALSE,                                             -- IsPhoneVerified
     FALSE,                                             -- IsBanned
     '$2a$12$r7eGOiC0RZQ55/6YSnvTduJMnTxsabgJS6dfZbjG/0wXqvPCbVxq6',  -- BCrypt("superadmin")
     FALSE,                                             -- MustResetPassword
     NOW() AT TIME ZONE 'utc')                          -- CreatedAtUtc
ON CONFLICT ("Id") DO NOTHING;
