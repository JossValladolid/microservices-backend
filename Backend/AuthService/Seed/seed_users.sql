
-- Prueba:
--   admin@example.com    / Admin123!
--   usuario@example.com  / Usuario123!

INSERT INTO "Users" ("Id", "Name", "Email", "Password", "BirthDate", "IsAdmin", "IsActive", "CreatedAt", "UpdatedAt")
VALUES
    (
        '11111111-1111-1111-1111-111111111111',
        'Admin Demo',
        'admin@example.com',
        '$2a$11$dcj.IrxSM2bcK6hAkSNVqeyu69nEwF9cJXYwBmXwXj8mTr55cMdOi',
        '1990-01-01',
        true,
        true,
        now(),
        now()
    ),
    (
        '22222222-2222-2222-2222-222222222222',
        'Usuario Demo',
        'usuario@example.com',
        '$2a$11$/z1/A3If6c4fPJ1XGi17g.qMoKLxN483RKgy9rmDyD30O72zRIluK',
        '1990-01-01',
        false,
        true,
        now(),
        now()
    )
ON CONFLICT ("Email") DO NOTHING;
