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
